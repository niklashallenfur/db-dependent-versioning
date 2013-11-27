module FileLoader

open Diffluxum.DbVersioning
open Diffluxum.DbVersioning.DbScriptRepository
open Diffluxum.DbVersioning.SqlServerSpecific
open Diffluxum.DbVersioning.Types
open System
open System.IO
open System.Text


let printUsage()=
    Console.WriteLine "Usage:"
    Console.WriteLine "DbVersioner.exe /ConnectionString=<cs> /ScriptDirectory=<sd> /Signature=<sign> [/SqlOutputFile=<sqlFile>] [/ModuleDirRegex=<regEx>] [/ModuleNameSeparator=<separator>] [/ScriptNameRemap=<map>] [/?] [/help]" 

let getOrDefault key def map =
    match Map.tryFind key map with
    |Some(x) -> x
    |None -> def


[<EntryPoint>]
let main args =       
    let argDict =               
            args
                |> Seq.ofArray
                |> Seq.map (fun x ->
                    let parts = x.Split('=')
                    (parts.[0].Trim('/'), String.Join("=", Seq.skip 1 (Seq.ofArray parts)).Trim()))
                |> Map.ofSeq

    let (connStr, baseDir, signature) =
        try    
            (Map.find "ConnectionString" argDict,
             Map.find "ScriptDirectory" argDict,
             Map.find "Signature" argDict )   
        with
            _ ->
                printUsage();
                failwith "Invalid Usage"

    let moduleDirRegex = getOrDefault "ModuleDirRegex" @"[a-zA-Z]*(?<moduleName>[\d\._]+).*" argDict
    let moduleNameSeparator = (getOrDefault "ModuleNameSeparator" "_" argDict).[0]
    let scriptNameRemap = getOrDefault "ScriptNameRemap" "" argDict
    
    // Initialize logfile, if used
    let (sqlOutput, outputFileDisposable) =
        let sqlOutFile = getOrDefault "SqlOutputFile" "" argDict
        match String.IsNullOrEmpty(sqlOutFile) with
        |true -> (None, {new IDisposable with member x.Dispose() = ignore()})
        |false ->   let fileLogger = new FileLogger(sqlOutFile)
                    (Some(fileLogger :> Diffluxum.DbVersioning.Types.ILogger), fileLogger :> IDisposable)
    use d = outputFileDisposable
    let logger = ConsoleLogger()

    let remapModuleName = Diffluxum.DbVersioning.Remap.createRemappings scriptNameRemap

    let connectionCreator = SqlConnectionFactory(3600, connStr, logger, sqlOutput, remapModuleName) :> IConnectionResourceProvider
    let scriptRepository = FileScriptRepository(baseDir, moduleDirRegex, moduleNameSeparator, logger, remapModuleName) :> IScriptRepository

    let versioner = new DbVersioner(connectionCreator, scriptRepository, logger, remapModuleName)    
    versioner.DownGradeUpGrade(false, "", "", signature)
    
    // Return 0. This indicates success.    
    0