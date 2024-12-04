module Mirage.Core.Task.Utility

open IcedTasks
open System.Threading.Tasks

/// Run the given effect forever.
let forever (program: unit -> ValueTask<unit>) =
    valueTask {
        while true do
            do! program()
    }