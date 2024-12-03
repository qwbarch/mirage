module Mirage.Core.Ply.Fork

open FSharp.Control.Tasks.Affine.Unsafe
open Mirage.Core.Ply.Channel
open System.Threading.Tasks

/// Run the given program from the async thread pool, and then return the value to the caller thread.
/// If an exception is thrown, the exception is caught and returned in the result.
let forkReturn program =
    uply {
        let! value =
            uply {
                let channel = Channel()
                ignore <| Task.Run(fun () ->
                    uply {
                        try
                            let! element = program()
                            writeChannel channel <| Ok element
                        with | error ->
                            writeChannel channel <| Error error
                    }
                )
                return! readChannel channel
            }
        return
            match value with
                | Error error -> raise error
                | Ok v -> v
    }