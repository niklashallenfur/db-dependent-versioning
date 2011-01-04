module FileLoader

open System
open System.IO
open System.Text.RegularExpressions
open System.Text


type ScriptName =
    {
        Module : int list
        Number : int;
    }
    override x.ToString() =
        let rec dotify (ints: int seq) =
            let ints = List.ofSeq ints
            match ints with
            |[] -> ""
            |subv::tail -> sprintf "%i.%s" subv (dotify tail)
        sprintf "%s%i" (dotify x.Module) x.Number

type DbScriptSpec = 
    {
        Name : ScriptName; 
        Path : string;
        Description : string;
        DependentOn : ScriptName option
    }
    override x.ToString() =
        let dependenyString =
            match x.DependentOn with
            | None -> ""
            | Some(name) -> sprintf " ->%s" (name.ToString())
        sprintf "%s: %s%s" (x.Name.ToString()) x.Path dependenyString

let parseScriptName (name : string) =
    let nameParts = name.Split('.') |> List.ofArray |> List.map Int32.Parse
    let rev = nameParts |> List.rev
    let number = rev.Head
    let moduleName = rev.Tail |> List.rev
    {Module = moduleName; Number = number}

let dependsOnRegex = @"\s*--\s*//@DEPENDSON\s*=\s*(?<dependsOnScript>[\d\.]+)\s*"

let getScriptDependency moduleFile =    
    let firstLine =
        use file = File.OpenText(moduleFile)
        file.ReadLine()
    let rm = Regex.Match(firstLine, dependsOnRegex)
    match rm.Success with
    | false -> None 
    | true -> Some(parseScriptName (rm.Groups.Item("dependsOnScript").Value))

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
    let toExecute = scriptChooser (script, undoScript)
    Console.WriteLine (sprintf "Executing %s" (spec.Name.ToString()))
    scriptExecuter toExecute
    Console.WriteLine()

open System.Data.SqlServerCe
open System.Data.SqlClient
open System.Data.Common

let executeSql (script : string) =
    use conn = new SqlConnection(@"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;")
    conn.Open()
    let statements =
        script.Split([|"GO"|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun s -> s.Trim())
        |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    for statement in statements do
        Console.WriteLine statement |> ignore
        Console.WriteLine "GO" |> ignore
        Console.WriteLine() |> ignore
        use command = new SqlCommand(statement, conn)
        command.ExecuteNonQuery() |> ignore

let registerCreated sqlExecuter (script : DbScriptSpec) =
        let dependency = match script.DependentOn with
                            | None -> "NULL"
                            | Some(name) -> sprintf "'%s'" (name.ToString())        
        let registerScript = sprintf "INSERT INTO DbVersioningHistory(ScriptVersion, ExecutedFrom, Description, DependentOnScriptVersion, DateExecutedUtc) VALUES('%s', '%s','%s', %s, '%s')" (script.Name.ToString()) (script.Path.ToString()) (script.Description.ToString()) dependency (DateTime.UtcNow.ToString("yyyy-MM-dd hh:mm:ss.nnn"))
        sqlExecuter registerScript

let baseDir = @"D:\Proj\db-versioning\DbScripts"
let moduleDirs = Directory.GetDirectories(baseDir) |> List.ofArray |> List.filter (fun dirName -> int(DirectoryInfo(dirName).Attributes &&& FileAttributes.Hidden) = 0) 
let modules = moduleDirs |> List.map getModule |> List.collect (fun (_, scripts) -> scripts)

let scripts = modules
let nameSorted = List.sort scripts

let dependent = TransformToItemDependent nameSorted

let dependencySorted = List.sortWith (DependencyCompare dependent) dependent

let executeAndRegister scriptSpec =
    let fns = [executeScript executeSql fst; registerCreated executeSql]
    List.map (fun f -> f(scriptSpec)) fns


dependencySorted |> List.map (fun s -> executeAndRegister s) |> ignore
dependencySorted |> List.rev |> List.map (executeScript executeSql snd) |> ignore

Console.ReadKey() |> ignore



