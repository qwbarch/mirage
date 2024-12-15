module Mirage.Core.Task.Lock

open System
open System.Threading
open System.Threading.Tasks
open FSharpPlus
open IcedTasks

type Lock =
    { semaphore: SemaphoreSlim }
    interface IDisposable with
        member this.Dispose() =
            dispose this.semaphore

let createLock () = { semaphore = new SemaphoreSlim(1) }

// Returns a key to send back to release the lock.
// It is written in this way to catch bugs with thread A acquiring a lock and thread B tries to release the lock that thread A acquired.
let inline lockAcquire lock = lock.semaphore.WaitAsync()

let inline lockRelease lock =
    ignore <| lock.semaphore.Release()

/// Try to acquire the lock, immediately returning <b>true</b> if the lock is acquired, or <b>false</b> if it failed, without blocking the thread.<br />
let inline tryAcquire lock = lock.semaphore.Wait TimeSpan.Zero

/// Implicitly acquire/release the given locks by entering/exiting the scope of the given program.
let withLock<'A> lock (program: unit -> ValueTask<'A>) =
    valueTask {
        do! lockAcquire lock
        try return! program()
        finally lockRelease lock
    }