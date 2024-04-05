module Mirage.Core.Monad

open FSharpPlus
open FSharpx.Control
open FSharpPlus.Data
open Cysharp.Threading.Tasks

/// Run the <b>Async</b> on the current thread, cancelling the program when the token is cancelled. This operation is **non-blocking**.
let runAsync token program = Async.StartImmediateAsTask(program, token).AsUniTask().Forget()

/// Run the <b>Async</b> on a separate thread, cancelling the program when the token is cancelled.
let forkAsync token program = Async.Start(program, token)

/// Run the given program from the async thread pool, and then return the value to the caller thread.
let forkReturn<'A> (program: Async<'A>) : Async<'A> =
    async {
        let agent = new BlockingQueueAgent<'A>(1)
        Async.Start(agent.AsyncAdd =<< program)
        return! agent.AsyncGet()
    }

/// Lift a <b>Result</b> into a <b>ResultT</b>.
let inline liftResult (program: Result<'A, 'B>) : ResultT<'``Monad<Result<'A, 'B>>``> =
    ResultT <| result program