namespace Diffluxum.DbVersioning.Types

open System

type ScriptName =
    {
        Module : int list
        Number : int;
    }
    override x.ToString() =
        let rec dotify (ints: int seq) =
            let ints = List.ofSeq ints
            match ints with
            |[] -> ""
            |subv::tail -> sprintf "%i.%s" subv (dotify tail)
        sprintf "%s%i" (dotify x.Module) x.Number
    static member Parse (name:string) = 
                                        let nameParts =
                                            try 
                                                 name.Split('.') |> List.ofArray |> List.map Int32.Parse
                                            with
                                                | :? System.FormatException -> failwithf "'%s' is not a valid script name" name
                                        let rev = nameParts |> List.rev
                                        let number = rev.Head
                                        let moduleName = rev.Tail |> List.rev
                                        {Module = moduleName; Number = number}

type DbScriptSpec = 
    {
        Name : ScriptName; 
        Path : string;
        Description : string;
        DependentOn : ScriptName option
    }
    override x.ToString() =
        let dependenyString =
            match x.DependentOn with
            | None -> ""
            | Some(name) -> sprintf " ->%s" (name.ToString())
        sprintf "%s: %s%s" (x.Name.ToString()) x.Path dependenyString

type ApplyUndoScript = {
    ApplyScript : string;
    UndoScript : string;
    }

type IConnectionResource =
    inherit IDisposable
    abstract ExecuteScript : string * string option -> unit
    abstract Commit : unit -> unit
    abstract GetAlreadyExecuted : unit -> seq<ScriptName>
    abstract RegisterExecuted : DbScriptSpec -> unit
    abstract UnRegisterExecuted : DbScriptSpec -> unit

type IConnectionResourceProvider =
    abstract CreateConnection : unit -> IConnectionResource

type IScriptRepository =
    abstract GetAvailableScripts : unit -> seq<DbScriptSpec>
    abstract LoadScript : DbScriptSpec -> ApplyUndoScript

[<System.Flags>]
type LogImportance =
   | Low = 1
   | Medium = 2
   | High = 3
   

type ILogger =
    abstract LogMessage : string * LogImportance -> unit
    abstract LogError : string -> unit