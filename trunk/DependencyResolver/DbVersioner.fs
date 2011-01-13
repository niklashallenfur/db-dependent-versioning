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
        let message = sprintf "Applying %s" (spec.Name.ToString())
        logger.LogMessage(message, LogImportance.High)
        connection.ExecuteScript (applyUndo.ApplyScript, Some(message))
        connection.RegisterExecuted spec        

    let undoScript (connection : IConnectionResource) (scriptLoader : DbScriptSpec -> ApplyUndoScript) (spec : DbScriptSpec) =
        let applyUndo = (scriptLoader spec)
        let message = sprintf "Undoing %s" (spec.Name.ToString())
        logger.LogMessage(message, LogImportance.High)
        connection.ExecuteScript (applyUndo.UndoScript, Some(message))
        connection.UnRegisterExecuted spec

    let lastItem sequence =
        let folder acc item =
            Some(item)
        Seq.fold folder None sequence

    member x.DownGrade(belowVersion, connection : IConnectionResource) =
        logger.LogMessage(sprintf "Trying to downgrade below %s " belowVersion, LogImportance.High)
        let scriptName = ScriptName.Parse(belowVersion)
        logger.LogMessage("Getting available scripts ", LogImportance.Low)
        let scripts = scriptRepository.GetAvailableScripts() |> List.ofSeq
        logger.LogMessage(sprintf "Number of available scripts: %i" (List.length scripts), LogImportance.Low)
        logger.LogMessage("Getting already executed scripts", LogImportance.Low)
        let alreadyExecuted = connection.GetAlreadyExecuted()

        let rec buildToExecute name acc existing =
            match existing with
            |[] -> ([], false)
            |x::tail when x = name -> (x::acc, true)
            |x::tail -> buildToExecute name (x::acc) tail
            
        let (namesToExecute, nameFound) = buildToExecute scriptName [] (List.ofSeq alreadyExecuted |> List.rev)        
        
        match nameFound with
        |false -> ignore 0
        |true ->    let findScript name = List.find (fun x -> x.Name = name) scripts
                    let scriptsToExecute = List.map findScript namesToExecute |> List.rev          
                    let undo() = scriptsToExecute |> List.map (fun s -> undoScript connection scriptRepository.LoadScript s) |> ignore                        
                    logger.LogMessage("Undoing scripts", LogImportance.Medium)
                    undo()

    member x.Upgrade (testUndo, toVersion, connection : IConnectionResource) =
        logger.LogMessage(sprintf "Trying to upgrade to %s " toVersion, LogImportance.High)
        let scriptName = ScriptName.Parse(toVersion)
        logger.LogMessage("Getting available scripts ", LogImportance.Low)
        let scripts = scriptRepository.GetAvailableScripts() |> List.ofSeq
        logger.LogMessage(sprintf "Number of available scripts: %i" (List.length scripts), LogImportance.Low)

        let nameSorted = List.sort scripts
  
        let dependent = TransformToItemDependent nameSorted
        let dependencySorted = List.sortWith (DependencyCompare dependent) dependent
        
        logger.LogMessage("Getting already executed scripts", LogImportance.Low)
        let alreadyExecuted = connection.GetAlreadyExecuted()
        let lastExecuted = lastItem alreadyExecuted

        logger.LogMessage(sprintf "Number of already executed scripts: %i" (Seq.length alreadyExecuted), LogImportance.Low)
        match lastExecuted with
        |Some(x) -> logger.LogMessage(sprintf "Last executed script is %s" (x.ToString()), LogImportance.Medium)
        |_ -> ignore 0
    
        let notExecuted = dependencySorted |> List.filter (fun script -> not (Seq.exists (fun existingScriptName -> script.Name = existingScriptName) alreadyExecuted))
        let notExecuted = ReplaceDependencies lastExecuted notExecuted
        
        logger.LogMessage(sprintf "not executed: %i" (List.length notExecuted),LogImportance.Low)

        let rec buildToExecute name acc existing =
            match existing with
            |[] -> []
            |x::tail when x.Name = name -> x::acc
            |x::tail -> buildToExecute name (x::acc) tail
            
        let scriptsToExecute = buildToExecute scriptName [] notExecuted |> List.rev

        logger.LogMessage(sprintf "Number of scripts to execute: %i" (List.length scriptsToExecute), LogImportance.Medium)

        let apply() = scriptsToExecute |> List.map (fun s -> applyScript connection scriptRepository.LoadScript s) |> ignore
        let undo() = scriptsToExecute |> List.rev |> List.map (fun s -> undoScript connection scriptRepository.LoadScript s) |> ignore
        let testUndoScripts() =
            logger.LogMessage("Undoing scripts", LogImportance.Medium)
            undo()
            logger.LogMessage("Re-applying scripts", LogImportance.Medium)
            apply()
        
        logger.LogMessage("Applying scripts", LogImportance.Medium)
        apply()
        if testUndo then testUndoScripts()

    member x.DownGradeUpGrade(testUndo, belowVersion, toVersion) =
        use connection = connectionCreator.CreateConnection()
        x.DownGrade(belowVersion, connection)
        x.Upgrade(testUndo, toVersion, connection)
        connection.Commit()

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

        logger.LogMessage(sprintf "Number of already executed scripts: %i" (Seq.length alreadyExecuted), LogImportance.Low)
        match lastExecuted with
        |Some(x) -> logger.LogMessage(sprintf "Last executed script is %s" (x.ToString()), LogImportance.Medium)
        |_ -> ignore 0
    
        let scriptsToExecute = dependencySorted |> List.filter (fun script -> not (Seq.exists (fun existingScriptName -> script.Name = existingScriptName) alreadyExecuted))
        let scriptsToExecute = ReplaceDependencies lastExecuted scriptsToExecute

        logger.LogMessage(sprintf "Number of scripts to execute: %i" (List.length scriptsToExecute), LogImportance.Medium)

        let apply() = scriptsToExecute |> List.map (fun s -> applyScript connection scriptRepository.LoadScript s) |> ignore
        let undo() = scriptsToExecute |> List.rev |> List.map (fun s -> undoScript connection scriptRepository.LoadScript s) |> ignore
        let testUndoScripts() =
            logger.LogMessage("Undoing scripts", LogImportance.Medium)
            undo()
            logger.LogMessage("Re-applying scripts", LogImportance.Medium)
            apply()
        
        logger.LogMessage("Applying scripts", LogImportance.Medium)
        apply()
        if testUndo then testUndoScripts()

        logger.LogMessage("Committing changes", LogImportance.Low)
        connection.Commit()
        logger.LogMessage("Done committing changes", LogImportance.Low)


open NUnit.Framework
open Rhino.Mocks
open MockExtensions

[<TestFixture>]
type DbVersionerSpecs() =

    let tempColor color = let oldCol = Console.ForegroundColor
                          Console.ForegroundColor <- color
                          {new IDisposable with member this.Dispose() = Console.ForegroundColor <- oldCol}



    let consoleLogger = {new ILogger with 
                  member this.LogMessage(text, importance) =    let col =   match importance with
                                                                            |LogImportance.High -> ConsoleColor.White
                                                                            |LogImportance.Medium -> ConsoleColor.Gray
                                                                            |LogImportance.Low -> ConsoleColor.DarkGray
                                                                            |x -> failwithf "%A not implemented" x
                                                                use c = tempColor col
                                                                Console.WriteLine(text);
                  member this.LogError(text) =  use c = tempColor ConsoleColor.Red
                                                Console.WriteLine(text)}


    let mocks = MockRepository()

    let connectionProvider = mocks.Stub<IConnectionResourceProvider>()
    let connection = mocks.Stub<IConnectionResource>()
    let scriptRepository = mocks.Stub<IScriptRepository>()
    let logger = consoleLogger
    let versioner = DbVersioner(connectionProvider, scriptRepository, logger)

    let moduleName = [1;10]
    let templateName = {Module = moduleName; Number = -1}
    let scriptName1 = {templateName with Number = 1}
    let scriptName2 = {templateName with Number = 2}
    let scriptName3 = {templateName with Number = 3}
    let scriptName4 = {templateName with Number = 4}

    let scriptTemplate = {Name = templateName; Path=""; Description=""; DependentOn = None}
    let script1 = {scriptTemplate with Name = scriptName1}
    let script2 = {scriptTemplate with Name = scriptName2; DependentOn = Some(scriptName1)}
    let script3 = {scriptTemplate with Name = scriptName3; DependentOn = Some(scriptName2)}
    let script4 = {scriptTemplate with Name = scriptName4; DependentOn = Some(scriptName3)}

    let text1 = {ApplyScript="a1"; UndoScript="u1"}
    let text2 = {ApplyScript="a2"; UndoScript="u2"}
    let text3 = {ApplyScript="a3"; UndoScript="u3"}
    let text4 = {ApplyScript="a4"; UndoScript="u4"}

    [<SetUp>]
    member x.Setup() =
        mocks.BackToRecordAll()
        Expect.Call(connectionProvider.CreateConnection()).Return(connection).Repeat.Any() |> ignore        
        ignore 0


    [<Test>]
    member x.DownGrade_VersionExists() =
        let alreadyExecuted = [scriptName1;scriptName2;scriptName3;scriptName4]
        let availableScripts = [script1;script2;script3;script4]

        Expect.Call(connection.GetAlreadyExecuted()).Return(alreadyExecuted) |> ignore
        Expect.Call(scriptRepository.GetAvailableScripts()).Return(availableScripts) |> ignore
        
        Expect.Call(scriptRepository.LoadScript(script3)).Return(text3) |> ignore
        Expect.Call(scriptRepository.LoadScript(script4)).Return(text4) |> ignore
        
        connection.ExecuteScript(text4.UndoScript, Some("Undoing 1.10.4"))
        connection.UnRegisterExecuted script4

        connection.ExecuteScript(text3.UndoScript, Some("Undoing 1.10.3"))
        connection.UnRegisterExecuted script3

        mocks.ReplayAll()
        versioner.DownGrade("1.10.2", connection)

        mocks.VerifyAll()
        ignore 0


    [<Test>]
    member x.Upgrade_VersionExists() =
        let alreadyExecuted = [scriptName1;scriptName3;]
        let availableScripts = [script1;script2;script3;script4]

        Expect.Call(connection.GetAlreadyExecuted()).Return(alreadyExecuted) |> ignore
        Expect.Call(scriptRepository.GetAvailableScripts()).Return(availableScripts) |> ignore
        
        Expect.Call(scriptRepository.LoadScript(script2)).Return(text2) |> ignore
        Expect.Call(scriptRepository.LoadScript(script4)).Return(text4) |> ignore
        
        connection.ExecuteScript(text2.ApplyScript, Some("Applying 1.10.2"))
        connection.UnRegisterExecuted script2

        connection.ExecuteScript(text4.ApplyScript, Some("Applying 1.10.4"))
        connection.UnRegisterExecuted script4

        mocks.ReplayAll()
        versioner.Upgrade(false, "1.10.4", connection)

        mocks.VerifyAll()
        ignore 0

