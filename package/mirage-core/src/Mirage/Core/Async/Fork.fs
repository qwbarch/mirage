module Mirage.Core.Async.Fork

open FSharpx.Control

/// Run the given program from the async thread pool, and then return the value to the caller thread.
/// If an exception is thrown, the exception is caught and returned in the result.
let forkReturn<'A> (program: Async<'A>) : Async<'A> =
    async {
        let! value =
            async {
                let agent = new BlockingQueueAgent<Result<'A, exn>>(1)
                Async.Start <| async {
                    try
                        let! value = program
                        do! agent.AsyncAdd <| Ok value
                    with | error -> agent.Add <| Result.Error error
                }
                return! agent.AsyncGet()
            }
        return
            match value with
                | Result.Error error -> raise error
                | Ok v -> v
    }