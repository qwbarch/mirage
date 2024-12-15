module Mirage.Core.Task.Loop

open System.Threading.Tasks
open IcedTasks

/// Run the given effect forever.
let inline forever (program: unit -> ValueTask<unit>) =
    valueTask {
        while true do
            do! program()
    }