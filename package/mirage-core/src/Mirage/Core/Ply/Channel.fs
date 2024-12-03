module Mirage.Core.Ply.Channel

open System
open System.Threading
open System.Collections.Concurrent
open FSharpPlus
open FSharp.Control.Tasks.Affine.Unsafe

type Channel<'A> =
    private
        {   semaphore: SemaphoreSlim
            queue: ConcurrentQueue<'A>
        }
    interface IDisposable with
        member this.Dispose() =
            dispose this.semaphore

let Channel () =
    {   semaphore = new SemaphoreSlim(0)
        queue = ConcurrentQueue()
    }

/// Write a value to the channel.
let writeChannel channel element =
    channel.queue.Enqueue element
    ignore <| channel.semaphore.Release()

/// Read a value from the channel, waiting if the channel is empty.
let readChannel channel (cancellationToken: CancellationToken) =
    uply {
        do! channel.semaphore.WaitAsync cancellationToken
        let mutable element = Unchecked.defaultof<_>
        if not <| channel.queue.TryDequeue(&element) then
            raise <| InvalidProgramException "Failed to dequeue during readChannel. This should not be possible."
        return element
    }

/// Read a value from the channel, waiting if the channel is empty.        
/// 
/// __Note: This does not get cancelled via a cancellation token, so use this with caution.__
let readChannel' channel =
    uply {
        do! channel.semaphore.WaitAsync()
        let mutable element = Unchecked.defaultof<_>
        if not <| channel.queue.TryDequeue(&element) then
            raise <| InvalidProgramException "Failed to dequeue during readChannel. This should not be possible."
        return element
    }