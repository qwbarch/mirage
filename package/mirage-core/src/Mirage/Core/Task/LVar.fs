module Mirage.Core.Task.LVar

open System
open FSharpPlus
open IcedTasks
open Mirage.Core.Task.Lock

/// A locked variable.
type LVar<'A> =
    private {   
        lock: Lock
        mutable value: 'A
    }
    interface IDisposable with
        member this.Dispose() =
            dispose this.lock

let newLVar value =
    {   lock = createLock()
        value = value
    }

/// Be mindful that 'T could be a reference and the underlying data can still be modified through it.
/// Create a copy of the data with accessLVar instead if necessary.
let readLVar lvar =
    withLock lvar.lock <| fun () ->
        valueTask {
            return lvar.value
        }

/// Writes a new value into the LVar and returns the previous value
let writeLVar lvar newValue =
    withLock lvar.lock <| fun () ->
        valueTask {
            let oldValue = lvar.value
            lvar.value <- newValue
            return oldValue
        }

/// Same as <b>writeLVar</b>, but except it discards the return value.
let inline writeLVar_ lvar = map ignore << writeLVar lvar

let accessLVar lvar f =
    withLock lvar.lock <| fun () ->
        valueTask {
            return f lvar.value
        }

let modifyLVar lvar f =
    withLock lvar.lock <| fun () ->
        valueTask {
            lvar.value <- f lvar.value
        }
