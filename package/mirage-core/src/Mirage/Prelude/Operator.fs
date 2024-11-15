namespace Mirage.Prelude

[<AutoOpenAttribute>]
module Operator =
    let inline ( += ) (source: byref<'A>) (value: 'A) = source <- source + value
    let inline ( -= ) (source: byref<'A>) (value: 'A) = source <- source - value
    let inline ( *= ) (source: byref<'A>) (value: 'A) = source <- source * value
    let inline ( /= ) (source: byref<'A>) (value: 'A) = source <- source / value
    let inline ( %= ) (source: byref<'A>) (modify: 'A -> 'A) = source <- modify source