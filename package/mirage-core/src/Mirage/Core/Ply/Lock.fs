module Mirage.Core.Ply.Lock

open System
open System.Threading
open FSharpPlus
open FSharp.Control.Tasks.Affine.Unsafe

type Lock =
    private { semaphore: SemaphoreSlim }
    interface IDisposable with
        member this.Dispose() =
            dispose this.semaphore

let createLock () = { semaphore = new SemaphoreSlim(1) }

// Returns a key to send back to release the lock.
// It is written in this way to catch bugs with thread A acquiring a lock and thread B tries to release the lock that thread A acquired.
let lockAcquire lock =
    uply {
        do! lock.semaphore.WaitAsync()
    }

let lockRelease lock =
    ignore <| lock.semaphore.Release()

/// Try to acquire the lock, immediately returning <b>true</b> if the lock is acquired, or <b>false</b> if it failed, without blocking the thread.<br />
let tryAcquire lock = lock.semaphore.Wait TimeSpan.Zero

/// Implicitly acquire/release the given locks by entering/exiting the scope of the given program.
let withLock lock program =
    uply {
        do! lockAcquire lock
        try return! program()
        finally lockRelease lock
    }