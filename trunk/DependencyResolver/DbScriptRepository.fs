namespace Diffluxum.DbVersioning.DbScriptRepository

open System
open System.IO
open System.Text.RegularExpressions
open System.Text
open Diffluxum.DbVersioning.Types

type FileScriptRepository (baseDir, moduleDirRegex, moduleNameSeparator : char) =    
    let getScriptDependency moduleFile =
        let dependsOnRegex = @"\s*--\s*//@DEPENDSON\s*=\s*(?<dependsOnScript>[\d\.]+)\s*"
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

    let getModuleName dirName =
        let rm = Regex.Match(dirName, moduleDirRegex)
        match rm.Success with
            |false -> failwithf "the module directory name '%s' does not match the required format of '%s'" dirName moduleDirRegex
            |true ->
                let matched =  rm.Groups.Item("moduleName").Value
                let moduleNameParts = matched.Split([|moduleNameSeparator|], StringSplitOptions.RemoveEmptyEntries)
                let moduleName = moduleNameParts |> Array.map (Int32.Parse) |> List.ofArray
                moduleName

    let getModule moduleDir =
        let moduleName = getModuleName (Path.GetFileName(moduleDir))
        let moduleFiles = Directory.GetFiles(moduleDir,"*.sql")
        let moduleScripts = moduleFiles |> List.ofArray |> List.map (getModuleScript moduleName)
        (moduleName, moduleScripts)

    let readAvailableScripts baseDir =
        let ignoreFolder =
            let ignoreFile = Path.Combine(baseDir, "IgnoreFolders.txt")
            match File.Exists(ignoreFile) with
            |false ->   fun x -> true
            |true ->    let ignored = File.ReadAllLines(ignoreFile) |> List.ofArray |> List.map (fun dir -> dir.Trim()) |> List.filter (fun x -> not (String.IsNullOrEmpty(x)))
                        fun folderPath -> List.exists (fun x -> x = Path.GetFileName(folderPath)) ignored
            
        let moduleDirs = Directory.GetDirectories(baseDir) |> List.ofArray |> List.filter (fun dirName ->   int(DirectoryInfo(dirName).Attributes &&& FileAttributes.Hidden) = 0 && not(ignoreFolder dirName))//&& Regex.IsMatch(Path.GetFileName(dirName), moduleDirRegex))
        let scripts = moduleDirs |> List.map getModule |> List.collect (fun (_, scripts) -> scripts)
        scripts

    let loadScript spec =
        let fileText = File.ReadAllText(spec.Path, Encoding.Default)
        let parts = fileText.Split([|"--//@UNDO"|], StringSplitOptions.None)
        {ApplyScript = parts.[0]; UndoScript = parts.[1]}

    interface IScriptRepository with
        member this.GetAvailableScripts() = Seq.ofList (readAvailableScripts baseDir)
        member this.LoadScript(spec) = loadScript spec