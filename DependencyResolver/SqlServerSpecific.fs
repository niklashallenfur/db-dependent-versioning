﻿namespace Diffluxum.DbVersioning.SqlServerSpecific

open System
open System.Data.SqlClient
open Diffluxum.DbVersioning.Types
open Diffluxum.DbVersioning.Resources

type SqlConnectionFactory (connStr) =
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

    let assertVersioningTableExists createCommand =
        let versioningTables = use command : SqlCommand = createCommand "SELECT COUNT(*) FROM sys.objects o WHERE o.name ='DbVersioningHistory' AND o.type ='U'"
                               command.ExecuteScalar() :?> int
        match versioningTables with
        |0 ->   executeSql createCommand SqlResources.CreateDbVersionHistory
                false
        |1 ->   true
        |x -> failwith (sprintf "Strange number of DbVersioningHistory tables in db: %i" x)
    
    let getAlreadyExecuted createCommand =    
            match assertVersioningTableExists createCommand with
            |false -> []
            |true ->
                use command : SqlCommand = createCommand "SELECT ScriptVersion FROM DbVersioningHistory ORDER BY ID ASC"
                use reader = command.ExecuteReader()    
                seq {  while reader.Read() do
                        yield ScriptName.Parse (string(reader.["ScriptVersion"]))}
                    |> List.ofSeq        

    let registerCreated sqlExecuter (script : DbScriptSpec) : unit =
            let dependency = match script.DependentOn with
                                | None -> "NULL"
                                | Some(name) -> sprintf "'%s'" (name.ToString())        
            let registerScript = sprintf "INSERT INTO DbVersioningHistory(ScriptVersion, ExecutedFrom, Description, DependentOnScriptVersion, DateExecutedUtc) VALUES('%s', '%s','%s', %s, '%s')" (script.Name.ToString()) (script.Path.ToString()) (script.Description.ToString()) dependency (DateTime.UtcNow.ToString("yyyy-MM-dd hh:mm:ss.fff"))
            sqlExecuter registerScript

    let unRegisterCreated sqlExecuter (script : DbScriptSpec) =                
            let unregisterScript = sprintf "DELETE FROM DbVersioningHistory WHERE ScriptVersion='%s'" (script.Name.ToString())
            sqlExecuter unregisterScript

    let createSqlConnection connStr = 
        let conn = new SqlConnection(connStr)
        let trans = conn.Open() 
                    conn.BeginTransaction(Data.IsolationLevel.Serializable)
        let createCommand commandText = new SqlCommand(commandText, conn, trans)
        let sqlExecuter = executeSql createCommand
        {new IConnectionResource with        
            member this.GetAlreadyExecuted() = getAlreadyExecuted createCommand :> seq<ScriptName>
            member this.ExecuteScript(toExecute) = sqlExecuter toExecute
            member this.RegisterExecuted(scriptSpec) = registerCreated sqlExecuter scriptSpec
            member this.UnRegisterExecuted(scriptSpec) = unRegisterCreated sqlExecuter scriptSpec
            member this.Commit() = trans.Commit()        
            member this.Dispose() = trans.Dispose()
                                    conn.Dispose()}

    interface IConnectionResourceProvider with
        member this.CreateConnection() = createSqlConnection connStr