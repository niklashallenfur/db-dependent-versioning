module FileLoader

open Diffluxum.DbVersioning
open Diffluxum.DbVersioning.DbScriptRepository
open Diffluxum.DbVersioning.SqlServerSpecific
open Diffluxum.DbVersioning.Types
open System
open System.IO
open System.Text


let tempColor color = let oldCol = Console.ForegroundColor
                      Console.ForegroundColor <- color
                      {new IDisposable with member this.Dispose() = Console.ForegroundColor <- oldCol}


let consoleLogger = ConsoleLogger()



let moduleDirRegex = @"[a-zA-Z]*(?<moduleName>[\d\._]+).*"
let moduleNameSeparator = '_'
let connString = @"Data Source=localhost;UID=cwo;PWD=cwo;Initial Catalog=SOV;"
let baseDir = @"E:\caletfs\Trunk\Database\CwoDB\Change Scripts"


let fileLogger = new FileLogger(@"e:\temp\diffscript.sql")

let moduleNameMapper = Diffluxum.DbVersioning.Remap.createRemappings "2.11:0.2.11, 2.12:0.2.12, 2.13:0.2.13, 2.14:0.2.14, 2.15:0.2.15"

let connectionCreator = SqlConnectionFactory(connString, consoleLogger, None, moduleNameMapper) :> IConnectionResourceProvider
let scriptRepository = FileScriptRepository(baseDir, moduleDirRegex, moduleNameSeparator, consoleLogger, moduleNameMapper) :> IScriptRepository

let versioner = DbVersioner(connectionCreator, scriptRepository, consoleLogger,moduleNameMapper)
versioner.DownGradeUpGrade(false,"","", "nikhal manual history update")
(fileLogger :> IDisposable).Dispose()

System.Console.ReadKey() |> ignore