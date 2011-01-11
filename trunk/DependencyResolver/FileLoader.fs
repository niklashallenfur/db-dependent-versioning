module FileLoader

open Diffluxum.DbVersioning

let moduleDirRegex = @".*(?<moduleName>[\d_]+).*"
let moduleNameSeparator = '_'
//let connString = @"Server=.;AttachDbFilename=|DataDirectory|TestDb.mdf;Trusted_Connection=Yes;"
let connString = @"Data Source=localhost;UID=cwo;PWD=cwo;Initial Catalog=cwoDev;"
let baseDir = @"D:\tfs\trunk\Database\CwoDB\Change Scripts"

let versioner = DbVersioner(connString, baseDir, moduleDirRegex, moduleNameSeparator)
versioner.ApplyLatestScripts(true)

System.Console.ReadKey() |> ignore