module FileLoader

open Diffluxum.DbVersioning.Types
open Diffluxum.DbVersioning.DbScriptRepository
open Diffluxum.DbVersioning.SqlServerSpecific

open System
open System.IO
open System.Text.RegularExpressions
open System.Text
    
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

let loadScript spec =
    let fileText = File.ReadAllText(spec.Path, Encoding.Default)
    let parts = fileText.Split([|"--//@UNDO"|], StringSplitOptions.None)
    (parts.[0], parts.[1])

let applyScript (connection : IConnectionResource) (spec : DbScriptSpec) =
    let (script, _) = loadScript spec
    Console.WriteLine (sprintf "--Applying %s" (spec.Name.ToString()))
    connection.ExecuteScript script
    connection.RegisterExecuted spec
    Console.WriteLine()

let undoScript (connection : IConnectionResource) (spec : DbScriptSpec) =
    let (_, undoScript) = loadScript spec
    Console.WriteLine (sprintf "--Undoing %s" (spec.Name.ToString()))
    connection.ExecuteScript undoScript
    connection.UnRegisterExecuted spec
    Console.WriteLine()

let lastItem sequence =
    let folder acc item =
        Some(item)
    Seq.fold folder None sequence

let connString = @"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;"
let baseDir = @"D:\Proj\db-versioning\DbScripts"
let connectionCreator = SqlConnectionFactory(connString) :> IConnectionResourceProvider
let scriptRepository = FileScriptRepository(baseDir) :> IScriptRepository


let program testUndo =
    let scripts = scriptRepository.GetAvailableScripts() |> List.ofSeq
    let nameSorted = List.sort scripts
  
    let dependent = TransformToItemDependent nameSorted
    let dependencySorted = List.sortWith (DependencyCompare dependent) dependent

    use connection = connectionCreator.CreateConnection()
    let alreadyExecuted = connection.GetAlreadyExecuted()
    let lastExecuted = lastItem alreadyExecuted
    
    let scriptsToExecute = dependencySorted |> List.filter (fun script -> not (Seq.exists (fun existingScriptName -> script.Name = existingScriptName) alreadyExecuted))
    let scriptsToExecute = ReplaceDependencies lastExecuted scriptsToExecute

    let apply() = scriptsToExecute |> List.map (fun s -> applyScript connection s) |> ignore
    let undo() = scriptsToExecute |> List.rev |> List.map (fun s -> undoScript connection s) |> ignore
    let testUndoScripts() =
        undo()
        apply()

    apply()
    if testUndo then testUndoScripts()

    connection.Commit()

program true
Console.ReadKey() |> ignore