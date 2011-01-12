namespace Diffluxum.DbVersioning

open Diffluxum.DbVersioning.Types

open System
open System.IO
open System.Text

type FileLogger(file) =
    let textBuilder = StringBuilder()
    interface Diffluxum.DbVersioning.Types.ILogger with
        member this.LogMessage(text, importance) = textBuilder.AppendLine(text) |> ignore
        member this.LogError(text) = textBuilder.AppendLine(text) |> ignore

    interface IDisposable with
        member this.Dispose() = File.WriteAllText(file, textBuilder.ToString())

type DbVersioner(connectionCreator : IConnectionResourceProvider, scriptRepository : IScriptRepository, logger : Diffluxum.DbVersioning.Types.ILogger) =
    let TransformToItemDependent items =
        let rec innerTransformToItemDependent (previous:ScriptName option) items =
            match items with
            |[] -> []
            |scriptSpec::tail ->
                match scriptSpec.DependentOn with
                | Some(_) -> scriptSpec::(innerTransformToItemDependent (Some scriptSpec.Name) tail)
                | None ->
                    match previous with
                    | None -> scriptSpec::(innerTransformToItemDependent (Some(scriptSpec.Name)) tail)
                    | Some(name) when name.Module = scriptSpec.Name.Module 
                        -> {scriptSpec with DependentOn = previous}::(innerTransformToItemDependent (Some(scriptSpec.Name)) tail)
                    | _ -> scriptSpec::(innerTransformToItemDependent (Some(scriptSpec.Name)) tail)
        innerTransformToItemDependent None items

    let rec ReplaceDependencies previous items =
        match items with
        |[] -> []
        |scriptSpec::tail -> {scriptSpec with DependentOn = previous}::(ReplaceDependencies (Some(scriptSpec.Name)) tail)

    let DependencyNameList allScripts scriptName =
        let findScript name = List.find (fun x -> x.Name = name) allScripts
        let rec dnl scriptName dependencies =
            let script = findScript scriptName
            let currentBuilt = (scriptName::dependencies)
            match script.DependentOn with
                |None -> currentBuilt
                |Some(dependentName) -> dnl dependentName currentBuilt
        dnl scriptName []

    let DependencyCompare allScripts script1 script2 =
        let dnl = DependencyNameList allScripts
        let dependencyChain1 = dnl script1.Name
        let dependencyChain2 = dnl script2.Name
        if dependencyChain1 < dependencyChain2 then -1 else 1

    let applyScript (connection : IConnectionResource) (scriptLoader : DbScriptSpec -> ApplyUndoScript) (spec : DbScriptSpec) =
        let applyUndo = (scriptLoader spec)
        logger.LogMessage(sprintf "--Applying %s" (spec.Name.ToString()), LogImportance.High)
        connection.ExecuteScript applyUndo.ApplyScript
        connection.RegisterExecuted spec
        logger.LogMessage("", LogImportance.Low)
        

    let undoScript (connection : IConnectionResource) (scriptLoader : DbScriptSpec -> ApplyUndoScript) (spec : DbScriptSpec) =
        let applyUndo = (scriptLoader spec)
        Console.WriteLine (sprintf "--Undoing %s" (spec.Name.ToString()))
        connection.ExecuteScript applyUndo.UndoScript
        connection.UnRegisterExecuted spec
        Console.WriteLine()

    let lastItem sequence =
        let folder acc item =
            Some(item)
        Seq.fold folder None sequence

    member x.ApplyLatestScripts (testUndo) =
        logger.LogMessage("Getting available scripts ", LogImportance.Low)
        let scripts = scriptRepository.GetAvailableScripts() |> List.ofSeq
        logger.LogMessage(sprintf "Number of available scripts: %i" (List.length scripts), LogImportance.Low)

        let nameSorted = List.sort scripts
  
        let dependent = TransformToItemDependent nameSorted
        let dependencySorted = List.sortWith (DependencyCompare dependent) dependent
        
        logger.LogMessage("Opening connection", LogImportance.Low)
        use connection = connectionCreator.CreateConnection()
        logger.LogMessage("Getting already executed scripts", LogImportance.Low)
        let alreadyExecuted = connection.GetAlreadyExecuted()
        let lastExecuted = lastItem alreadyExecuted

        logger.LogMessage(sprintf "Number of already executed scripts: %i" (List.length scripts), LogImportance.Low)
        match lastExecuted with
        |Some(x) -> logger.LogMessage(sprintf "Last executed script is %s" (x.ToString()), LogImportance.Medium)
        |_ -> ignore 0
    
        let scriptsToExecute = dependencySorted |> List.filter (fun script -> not (Seq.exists (fun existingScriptName -> script.Name = existingScriptName) alreadyExecuted))
        let scriptsToExecute = ReplaceDependencies lastExecuted scriptsToExecute

        logger.LogMessage(sprintf "Number of scripts to execute: %i" (List.length scriptsToExecute), LogImportance.Medium)        

        let apply() = scriptsToExecute |> List.map (fun s -> applyScript connection scriptRepository.LoadScript s) |> ignore
        let undo() = scriptsToExecute |> List.rev |> List.map (fun s -> undoScript connection scriptRepository.LoadScript s) |> ignore
        let testUndoScripts() =
            logger.LogMessage("--Undoing scripts", LogImportance.Medium)
            undo()
            logger.LogMessage("--Re-applying scripts", LogImportance.Medium)
            apply()
        
        logger.LogMessage("--Applying scripts", LogImportance.Medium)
        apply()
        if testUndo then testUndoScripts()

        logger.LogMessage("--Committing changes", LogImportance.Low)
        connection.Commit()
        logger.LogMessage("--Done committing changes", LogImportance.Low)
