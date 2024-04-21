module Mirage.Core.Async.Lazy

open System.Threading.Tasks

type Lazy =
    /// Convert the given <b>Task</b> to <b>Async</b> without evaluating the task.
    static member toAsync (task: unit -> Task<'A>) : Async<'A> =
        async {
            return! Async.AwaitTask(task())
        }
    /// Convert the given <b>Task</b> to <b>Async</b> without evaluating the task.
    static member toAsync (task: unit -> Task) : Async<Unit> =
        async {
            return! Async.AwaitTask(task())
        }