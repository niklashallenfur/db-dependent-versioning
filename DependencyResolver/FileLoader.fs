module FileLoader

open System
open System.IO
open System.Text.RegularExpressions
open System.Text
open Diffluxum.DbVersioning.Types
open Diffluxum.DbVersioning.SqlServerSpecific

let dependsOnRegex = @"\s*--\s*//@DEPENDSON\s*=\s*(?<dependsOnScript>[\d\.]+)\s*"

let getScriptDependency moduleFile =    
    let firstLine =
        use file = File.OpenText(moduleFile)
        file.ReadLine()
    let rm = Regex.Match(firstLine, dependsOnRegex)
    match rm.Success with
    | false -> None 
    | true -> Some(ScriptName.Parse(rm.Groups.Item("dependsOnScript").Value))

let getModuleScript moduleName moduleFile =
    let fileName = Path.GetFileNameWithoutExtension(moduleFile)
    let indexOfSeparator = fileName.IndexOf('_')
    let (fileNumber, description) =
        if indexOfSeparator > 0 then
            (fileName.Substring(0, indexOfSeparator) |> Int32.Parse,
             fileName.Substring(indexOfSeparator + 1))
        else
            (fileName |> Int32.Parse, "")
    let scriptName = {Module = moduleName; Number = fileNumber}
    {   Name = scriptName;
        Description = description;
        Path = moduleFile;
        DependentOn = getScriptDependency(moduleFile)}

let getModule moduleDir =
    let moduleNameUnparsed = Path.GetFileName(moduleDir)
    let moduleNameParts = moduleNameUnparsed.Split('.')
    let moduleName = moduleNameParts |> Array.map (Int32.Parse) |> List.ofArray
    let moduleFiles = Directory.GetFiles(moduleDir)
    let moduleScripts = moduleFiles |> List.ofArray |> List.map (getModuleScript moduleName)
    (moduleName, moduleScripts)
    
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

let executeScript scriptExecuter scriptChooser (spec : DbScriptSpec) = 
    let (script, undoScript) = loadScript spec
    let toExecute : string = scriptChooser (script, undoScript)
    Console.WriteLine (sprintf "Executing %s" (spec.Name.ToString()))
    scriptExecuter toExecute
    Console.WriteLine()

open System.Transactions

let connString = @"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;"

let baseDir = @"D:\Proj\db-versioning\DbScripts"

let moduleDirs = Directory.GetDirectories(baseDir) |> List.ofArray |> List.filter (fun dirName -> int(DirectoryInfo(dirName).Attributes &&& FileAttributes.Hidden) = 0) 
let scripts = moduleDirs |> List.map getModule |> List.collect (fun (_, scripts) -> scripts)

let nameSorted = List.sort scripts

let dependent = TransformToItemDependent nameSorted
let dependencySorted = List.sortWith (DependencyCompare dependent) dependent

let connection = createSqlConnection connString

let executeAndRegister scriptSpec =
    let fns = [executeScript connection.ExecuteScript fst; registerCreated connection.ExecuteScript]
    List.map (fun f -> f(scriptSpec)) fns

let undoAndUnRegister scriptSpec =
    let fns = [unRegisterCreated connection.ExecuteScript; executeScript connection.ExecuteScript snd]
    List.map (fun f -> f(scriptSpec)) fns

let alreadyExecuted = connection.GetAlreadyExecuted()
let scriptsToExecute = dependencySorted |> List.filter (fun script -> not (Seq.exists (fun existingScriptName -> script.Name = existingScriptName) alreadyExecuted))

scriptsToExecute |> List.map (fun s -> executeAndRegister s) |> ignore

dependencySorted |> List.rev |> List.map (fun s -> undoAndUnRegister s) |> ignore

connection.Commit()
connection.Dispose()

Console.ReadKey() |> ignore



