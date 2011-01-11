module FileLoader

open Diffluxum.DbVersioning

let moduleDirRegex = @".*(?<moduleName>[\d\.]+).*"
let moduleNameSeparator = '.'
let connString = @"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;"
let baseDir = @"D:\Proj\db-versioning\DbScripts"

let versioner = DbVersioner(connString, baseDir, moduleDirRegex, moduleNameSeparator)
versioner.ApplyLatestScripts(true)

System.Console.ReadKey() |> ignore