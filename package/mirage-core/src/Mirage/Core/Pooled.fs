module Mirage.Core.Pooled

open System
open Collections.Pooled

/// Copy from the pooled list to the destination array.
let inline copyFrom<'A> (pooledList: PooledList<'A>) (destination: 'A[]) count =
    for i in 0 .. count - 1 do
        destination[i] <- pooledList[i]

let inline appendSegment<'A> (pooledList: PooledList<'A>) (segment: ArraySegment<'A>) =
    for i in 0 .. segment.Count - 1 do
        pooledList.Add segment[i]