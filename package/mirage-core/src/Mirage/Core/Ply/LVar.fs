module Mirage.Core.Ply.LVar

open System
open FSharpPlus
open FSharp.Control.Tasks.Affine.Unsafe
open Mirage.Core.Ply.Lock

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
    uply {
        return! withLock lvar.lock <| fun () ->
            uply {
                return lvar.value
            }
    }

/// Writes a new value into the LVar and returns the previous value
let writeLVar lvar newValue =
    uply {
        return! withLock lvar.lock <| fun () ->
            uply {
                let oldValue = lvar.value
                lvar.value <- newValue
                return oldValue
            }
    }

/// Same as <b>writeLVar</b>, but except it discards the return value.
let writeLVar_ lvar newValue = 
    uply {
        do! writeLVar lvar newValue
    }

let accessLVar lvar f =
    uply {
        return! withLock lvar.lock <| fun () ->
            uply {
                return f lvar.value
            }
    }

let modifyLVar (lvar: LVar<'T>) (f : 'T -> 'T) =
    uply {
        return! withLock lvar.lock <| fun () ->
            uply {
                lvar.value <- f lvar.value
            }
    }
