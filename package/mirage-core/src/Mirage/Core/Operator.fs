module Mirage.Core.Operator

let inline ( += ) (source: byref<'A>) (value: 'A) = source <- source + value
let inline ( -= ) (source: byref<'A>) (value: 'A) = source <- source - value
let inline ( *= ) (source: byref<'A>) (value: 'A) = source <- source * value
let inline ( /= ) (source: byref<'A>) (value: 'A) = source <- source / value