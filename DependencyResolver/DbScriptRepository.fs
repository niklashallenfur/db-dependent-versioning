module Diffluxum.DbVersioning.DbScriptRepository

open System
open System.IO
open System.Text.RegularExpressions
open System.Text
open Diffluxum.DbVersioning.Types

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

let readAvailableScripts baseDir =
    let moduleDirs = Directory.GetDirectories(baseDir) |> List.ofArray |> List.filter (fun dirName -> int(DirectoryInfo(dirName).Attributes &&& FileAttributes.Hidden) = 0) 
    let scripts = moduleDirs |> List.map getModule |> List.collect (fun (_, scripts) -> scripts)
    scripts

