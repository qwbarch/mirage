module Mirage.Core.Task.Fork

open IcedTasks
open System.Threading.Tasks
open Mirage.Core.Task.Channel

/// Run the given program from the async thread pool, and then return the value to the caller thread.  
/// If an exception is caught, the exception is re-thrown in the caller's thread.
let inline forkReturn<'A> cancellationToken (program: unit -> ValueTask<'A>) =
    valueTask {
        let channel = Channel cancellationToken
        let thread () =
            valueTask {
                try
                    let! element = program()
                    writeChannel channel <| Ok element
                with | error ->
                    writeChannel channel <| Error error
            }
        ignore <| Task.Run(thread, cancellationToken)
        let! value = readChannel channel
        return
            match value with
                | Error error -> raise error
                | Ok v -> v
    }

/// Run the given program from the async thread pool.  
/// If an exception is caught, the exception is re-thrown in the caller's thread.  
let inline fork cancellationToken = ignore << forkReturn cancellationToken