module FileLoader

open Diffluxum.DbVersioning

let moduleDirRegex = @"[a-zA-Z]*(?<moduleName>[\d\.]+).*"
let moduleNameSeparator = '.'
//let connString = @"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;"
let connString = @"..."
let baseDir = @"..."

let versioner = DbVersioner(connString, baseDir, moduleDirRegex, moduleNameSeparator)
versioner.ApplyLatestScripts(true)

System.Console.ReadKey() |> ignore