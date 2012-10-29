module Diffluxum.DbVersioning.Remap

open System
open Diffluxum.DbVersioning.Types

let rec beginsWith beginning list =
    match beginning with
    |[]-> true
    |bHead::bTail ->
        match list with
        |[] -> false
        |lHead::lTail ->
            if bHead = lHead then
                (beginsWith bTail lTail)
            else
                false

let rec skip n list =
    match n with
    |0 -> list
    |x ->
        match list with
        | [] -> failwith "not enough elements"
        | head::tail -> skip (n-1) tail
    

let remapListStart pattern replacement inputList =
    match beginsWith pattern inputList with
    |false -> inputList
    |true -> List.concat [replacement; (skip (List.length pattern) inputList)]


let remap (matchpattern : string) (replacement : string) =
    let pattern =
            matchpattern.Split([|'.'|])
                |> List.ofArray
                |> List.map Int32.Parse

    let repl =
            replacement.Split([|'.'|])
                |> List.ofArray
                |> List.map Int32.Parse

    remapListStart pattern repl

let parseRemapper (remapSpec : string) =
    let parts = 
        remapSpec.Split([|":"|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun x -> x.Trim())
    remap parts.[0] parts.[1]
    
let createRemappings (remapSpec : string) text =
    let remappers = remapSpec.Split([|","|], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun x-> parseRemapper x)
    Array.fold (fun text remapper -> remapper text) text remappers
