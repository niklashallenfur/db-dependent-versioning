module FileLoader

open Diffluxum.DbVersioning
open Diffluxum.DbVersioning.DbScriptRepository
open Diffluxum.DbVersioning.SqlServerSpecific
open Diffluxum.DbVersioning.Types
open System
open System.IO
open System.Text

let moduleDirRegex = @"[a-zA-Z]*(?<moduleName>[\d\.]+).*"
let moduleNameSeparator = '.'
let connString = @"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;"

let baseDir = @"D:\Proj\db-versioning\DbScripts"


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

let fileLogger = new FileLogger(@"d:\temp\diffscript.sql")

let connectionCreator = SqlConnectionFactory(connString, consoleLogger, Some(fileLogger :> Diffluxum.DbVersioning.Types.ILogger)) :> IConnectionResourceProvider
let scriptRepository = FileScriptRepository(baseDir, moduleDirRegex, moduleNameSeparator) :> IScriptRepository

let versioner = DbVersioner(connectionCreator, scriptRepository, consoleLogger)
versioner.ApplyLatestScripts(true)                        
(fileLogger :> IDisposable).Dispose()

System.Console.ReadKey() |> ignore