namespace Diffluxum.DbVersioning.SqlServerSpecific

open System
open System.Data.SqlClient
open System.Transactions
open Diffluxum.DbVersioning.Types
open Diffluxum.DbVersioning.Resources

type SqlConnectionFactory (commandTimeout, connStr, logger : ILogger, sqlOutput : ILogger option, remapModuleName : (int list -> int list)) =

    let mutable transactionScope : SqlTransaction option = None

    let mergeComment script comment =
        match comment with
        | None -> script
        | Some(c) -> sprintf "--%s%s%s" c Environment.NewLine script

    let executeSql createCommand (script : string) (comment : string option) =
        let script = mergeComment script comment
        let statements =            
            script.Split([|"GO"|], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))
        for statement in statements do
            logger.LogMessage(statement, LogImportance.Low)
            logger.LogMessage("GO", LogImportance.Low)
            logger.LogMessage("", LogImportance.Low)
            use command : SqlCommand = createCommand statement
            command.ExecuteNonQuery() |> ignore

    let assertVersioningTableExists createCommand sqlExecuter =
        let versioningTables = use command : SqlCommand = createCommand "SELECT COUNT(*) FROM sys.objects o WHERE o.name ='DbVersioningHistory' AND o.type ='U'"
                               command.ExecuteScalar() :?> int
        match versioningTables with
        |0 ->   sqlExecuter SqlResources.CreateDbVersionHistory (Some "Creating DbVersioningHistory")
                false
        |1 ->
            // Remove the foreign key!
            sqlExecuter SqlResources.RemoveForeginKey (Some "Removing foreign key in DbVersioningHistory")
            true
        |x -> failwith (sprintf "Strange number of DbVersioningHistory tables in db: %i" x)        
    
    let getAlreadyExecuted sqlExecuter createCommand assertVersioningTableExists =
            let existing =
                match assertVersioningTableExists() with
                |false -> []
                |true ->
                    use command : SqlCommand = createCommand "SELECT ScriptVersion FROM DbVersioningHistory ORDER BY ID ASC"
                    use reader = command.ExecuteReader()    
                    seq {  while reader.Read() do
                            yield  ScriptName.Parse (string(reader.["ScriptVersion"])) }
                        |> List.ofSeq


            let remapped = existing |> List.map (fun x -> {x with Module = remapModuleName x.Module})
            let needsRemapping =
                List.zip existing remapped
                |> List.filter (fun (existing, remapped) -> not (existing.ToString() = remapped.ToString()))

            needsRemapping
                |> List.iter (fun (existing, remapped) -> sqlExecuter (String.Format(SqlResources.RemapScriptVersion, existing, remapped)) None)
            remapped

    let registerCreated sqlExecuter (script : DbScriptSpec) (signature : string) : unit =
            let dependency = match script.DependentOn with
                                | None -> "NULL"
                                | Some(name) -> sprintf "'%s'" (name.ToString())        
            let registerScript = sprintf "INSERT INTO DbVersioningHistory(ScriptVersion, ExecutedFrom, Description, DependentOnScriptVersion, DateExecutedUtc, Signature) VALUES('%s', '%s','%s', %s, GETUTCDATE(), '%s')" (script.Name.ToString()) (script.Path.ToString()) (script.Description.ToString()) dependency (signature.Replace("'", "''"))
            sqlExecuter registerScript (Some(sprintf "Registering %s as applied" (script.Name.ToString())))

    let unRegisterCreated sqlExecuter (script : DbScriptSpec) =
            let unregisterScript = sprintf "DELETE FROM DbVersioningHistory WHERE ScriptVersion='%s'" (script.Name.ToString())
            sqlExecuter unregisterScript (Some(sprintf "Unregistering %s as applied" (script.Name.ToString())))

                           

    let createSqlConnection connStr = 
        let conn = new SqlConnection(connStr)
        conn.Open()
//        let trans = conn.Open()
//                    conn.BeginTransaction(Data.IsolationLevel.ReadCommitted)
        
        let createCommand commandText = let cmd = new SqlCommand(commandText, conn)
                                        cmd.CommandTimeout <- commandTimeout
                                        match transactionScope with
                                        | Some(x) -> cmd.Transaction <- x
                                        | None -> ignore()
                                        cmd

        let beginTransaction() =
            match transactionScope with
            | Some(x) -> failwith "Transaction already started"
            | None ->
                let trans = conn.BeginTransaction()
                transactionScope <- Some(trans)
                {new ITransaction with
                    member this.Dispose() =
                        trans.Dispose()
                        transactionScope <- None
                    member this.Commit() = trans.Commit()}

        let sqlExecuter =
            match sqlOutput with
            | None -> executeSql createCommand
            | Some(output) -> fun script comment -> output.LogMessage(mergeComment script comment, LogImportance.Low)
         
        {new IConnectionResource with
            member this.BeginTransaction() = beginTransaction()
            member this.GetAlreadyExecuted() = getAlreadyExecuted sqlExecuter createCommand (fun() -> assertVersioningTableExists createCommand sqlExecuter):> seq<ScriptName>
            member this.ExecuteScript(toExecute, comment) = sqlExecuter toExecute comment
            member this.RegisterExecuted(scriptSpec, signature) = registerCreated sqlExecuter scriptSpec signature
            member this.UnRegisterExecuted(scriptSpec) = unRegisterCreated sqlExecuter scriptSpec            
            member this.Dispose() = conn.Dispose()}

    interface IConnectionResourceProvider with
        member this.CreateConnection() = createSqlConnection connStr