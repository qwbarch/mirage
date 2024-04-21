module Mirage.Core.Async

open FSharpPlus
open FSharpx.Control
open System.Threading.Tasks

/// Run the given program from the async thread pool, and then return the value to the caller thread.
let forkReturn<'A> (program: Async<'A>) : Async<'A> =
    async {
        let agent = new BlockingQueueAgent<'A>(1)
        Async.Start(agent.AsyncAdd =<< program)
        return! agent.AsyncGet()
    }

type Lazy =
    static member toAsync (task: unit -> Task<'A>) : Async<'A> =
        async {
            return! Async.AwaitTask(task())
        }
    static member toAsync (task: unit -> Task) : Async<Unit> =
        async {
            return! Async.AwaitTask(task())
        }