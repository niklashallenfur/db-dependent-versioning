namespace Diffluxum.DbVersioning

open System

open Microsoft.Build.Framework
open Microsoft.Build.Utilities

open Diffluxum.DbVersioning.Types
open Diffluxum.DbVersioning.DbScriptRepository
open Diffluxum.DbVersioning.SqlServerSpecific

type SyncDatabase() =
    inherit Task()

    let mutable connStr = ""
    let mutable baseDir = ""
    let mutable moduleDirRegex = @"[a-zA-Z]*(?<moduleName>[\d_]+).*"
    let mutable moduleNameSeparator = '_'
    let mutable outputFile = ""
    let mutable testUndo = true
    let mutable scriptNameRemap = ""


    let mutable downBelowVersion = ""
    let mutable upToVersion = ""
    let mutable signature = ""

    let getImportance importance = 
        match importance with
        |LogImportance.Low -> MessageImportance.Low        
        |LogImportance.High -> MessageImportance.High
        |_ -> MessageImportance.Normal

    [<Required>] member this.ConnectionString with get() = connStr and set (value) = connStr <- value
    [<Required>] member this.ScriptDirectory with get() = baseDir and set (value) = baseDir <- value
    [<Required>] member this.Signature with get() = signature and set (value) = signature <- value
    
    member this.UpToVersion with get() = upToVersion and set (value) = upToVersion <- value
    member this.DownBelowVersion with get() = downBelowVersion and set (value) = downBelowVersion <- value
    member this.OutputFile with get() = outputFile and set (value) = outputFile <- value
    member this.TestUndo with get() = testUndo and set (value) = testUndo <- value
    member this.ModuleDirRegex  with get() = moduleDirRegex and set (value) = moduleDirRegex <- value
    member this.ModuleNameSeparator with get() = moduleNameSeparator  and set (value) = moduleNameSeparator <- value
    member this.ScriptNameRemap with get() = scriptNameRemap  and set (value) = scriptNameRemap <- value

    override this.Execute() =
        let logger = {new Diffluxum.DbVersioning.Types.ILogger with 
                          member l.LogMessage(text, importance) = this.Log.LogMessage(getImportance(importance) ,text);
                          member l.LogError(text) =  this.Log.LogError(text)
                          member l.LogWarning(text) = this.Log.LogWarning(text)}

        let (sqlOutput, outputFileDisposable) =
            match String.IsNullOrEmpty(outputFile) with
            |true -> (None, {new IDisposable with member x.Dispose() = ignore 0})
            |false ->   let fileLogger = new FileLogger(outputFile)
                        (Some(fileLogger :> Diffluxum.DbVersioning.Types.ILogger), fileLogger :> IDisposable)        
        use d = outputFileDisposable

        let remapModuleName = Diffluxum.DbVersioning.Remap.createRemappings scriptNameRemap

        let connectionCreator = SqlConnectionFactory(connStr, logger, sqlOutput, remapModuleName) :> IConnectionResourceProvider
        let scriptRepository = FileScriptRepository(baseDir, moduleDirRegex, moduleNameSeparator, logger, remapModuleName) :> IScriptRepository

        let versioner = new DbVersioner(connectionCreator, scriptRepository, logger, remapModuleName)    
        versioner.DownGradeUpGrade(testUndo, downBelowVersion, upToVersion, signature)    
        true

