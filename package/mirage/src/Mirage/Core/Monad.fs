module Mirage.Core.Monad

open System.Threading
open FSharpPlus
open FSharpx.Control
open FSharpPlus.Data

/// <summary>
/// Run the <b>Async</b> synchronously on the current thread, cancelling the program when the token is cancelled.
/// This operation is **non-blocking**.
/// </summary>
let runAsync_<'A> (token: CancellationToken) (program: Async<'A>) : Unit =
    ignore <| Async.StartImmediateAsTask(program, token)

/// <summary>
/// Run the given program from the async thread pool, and then return the value to the caller thread.
/// </summary>
let forkReturn<'A> (program: Async<'A>) : Async<'A> =
    async {
        let agent = new BlockingQueueAgent<'A>(1)
        Async.Start(agent.AsyncAdd =<< program)
        return! agent.AsyncGet()
    }

/// <summary>
/// Lift a <b>Result</b> into a <b>ResultT</b>.
/// </summary>
let inline liftResult (program: Result<'A, 'B>) : ResultT<'``Monad<Result<'A, 'B>>``> =
    ResultT <| result program