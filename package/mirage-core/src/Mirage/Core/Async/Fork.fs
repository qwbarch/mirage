module Mirage.Core.Async.Fork

open FSharpPlus
open FSharpx.Control

/// Run the given program from the async thread pool, and then return the value to the caller thread.
let forkReturn<'A> (program: Async<'A>) : Async<'A> =
    async {
        let agent = new BlockingQueueAgent<'A>(1)
        Async.Start(agent.AsyncAdd =<< program)
        return! agent.AsyncGet()
    }