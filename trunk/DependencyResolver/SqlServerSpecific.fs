module Diffluxum.DbVersioning.SqlServerSpecific

open System
open System.Data.SqlClient
open Diffluxum.DbVersioning.Types

let executeSql createCommand (script : string) =
    let statements =
        script.Split([|"GO"|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun s -> s.Trim())
        |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    for statement in statements do
        Console.WriteLine statement |> ignore
        Console.WriteLine "GO" |> ignore
        Console.WriteLine() |> ignore
        use command : SqlCommand = createCommand statement
        command.ExecuteNonQuery() |> ignore

let getAlreadyExecuted createCommand =    
    let versioningTables = use command : SqlCommand = createCommand "SELECT COUNT(*) FROM sys.objects o WHERE o.name ='DbVersioningHistory' AND o.type ='U'"
                           command.ExecuteScalar() :?> int
    match versioningTables with
        |0 -> []
        |1 ->   use command : SqlCommand = createCommand "SELECT ScriptVersion FROM DbVersioningHistory ORDER BY ID ASC"
                use reader = command.ExecuteReader()    
                seq {  while reader.Read() do
                        yield ScriptName.Parse (string(reader.["ScriptVersion"]))}
                    |> List.ofSeq
        |x -> failwith (sprintf "Strange number of DbVersioningHistory tables in db: %i" x)       

let registerCreated sqlExecuter (script : DbScriptSpec) : unit =
        let dependency = match script.DependentOn with
                            | None -> "NULL"
                            | Some(name) -> sprintf "'%s'" (name.ToString())        
        let registerScript = sprintf "INSERT INTO DbVersioningHistory(ScriptVersion, ExecutedFrom, Description, DependentOnScriptVersion, DateExecutedUtc) VALUES('%s', '%s','%s', %s, '%s')" (script.Name.ToString()) (script.Path.ToString()) (script.Description.ToString()) dependency (DateTime.UtcNow.ToString("yyyy-MM-dd hh:mm:ss"))
        sqlExecuter registerScript

let unRegisterCreated sqlExecuter (script : DbScriptSpec) =        
        let unregisterScript = sprintf "DELETE FROM DbVersioningHistory WHERE ScriptVersion='%s'" (script.Name.ToString())
        sqlExecuter unregisterScript

let createSqlConnection connStr = 
    let conn = new SqlConnection(connStr)    
    let trans = conn.Open() 
                conn.BeginTransaction(Data.IsolationLevel.Serializable)
    let createCommand commandText = new SqlCommand(commandText, conn, trans)

    {new IConnectionResource with        
        member this.GetAlreadyExecuted() = getAlreadyExecuted createCommand :> seq<ScriptName>
        member this.ExecuteScript(string) = executeSql createCommand string
        member this.Commit() = trans.Commit()
        member this.Dispose() = trans.Dispose()
                                conn.Dispose()}
type SqlConnectionFactory (connStr) = 
    interface IConnectionResourceProvider with
        member this.CreateConnection() = createSqlConnection connStr

let sqlConnectionCreator = {new IConnectionResourceProvider with member this.CreateConnection() = createSqlConnection}