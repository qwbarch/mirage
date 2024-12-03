module Mirage.Core.Ply.Fork

open FSharp.Control.Tasks.Affine.Unsafe
open Mirage.Core.Ply.Channel
open System.Threading.Tasks

/// Run the given program from the async thread pool, and then return the value to the caller thread.  
/// If an exception is caught, the exception is re-thrown in the caller's thread.
let forkReturn program cancellationToken =
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
                , cancellationToken)
                return! readChannel' channel
            }
        return
            match value with
                | Error error -> raise error
                | Ok v -> v
    }

/// Run the given program from the async thread pool, and then return the value to the caller thread.  
/// If an exception is caught, the exception is re-thrown in the caller's thread.  
/// 
/// __Note: This does not get cancelled via a cancellation token, so use this with caution.__
let forkReturn' program =
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
                return! readChannel' channel
            }
        return
            match value with
                | Error error -> raise error
                | Ok v -> v
    }

/// Run the given program from the async thread pool.  
/// If an exception is caught, the exception is re-thrown in the caller's thread.  
let fork program cancellationToken =
    ignore <| uply {
        do! forkReturn program cancellationToken
    }

/// Run the given program from the async thread pool.  
/// If an exception is caught, the exception is re-thrown in the caller's thread.  
/// 
/// __Note: This does not get cancelled via a cancellation token, so use this with caution.__
let fork' program =
    ignore <| uply {
        do! forkReturn' program
    }