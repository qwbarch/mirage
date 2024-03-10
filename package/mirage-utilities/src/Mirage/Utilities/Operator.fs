module Mirage.Utilities.Operator

let inline ( += ) (source: ref<'A>) (value: 'A) = source.Value <- source.Value + value
let inline ( -= ) (source: ref<'A>) (value: 'A) = source.Value <- source.Value - value
let inline ( *= ) (source: ref<'A>) (value: 'A) = source.Value <- source.Value * value
let inline ( /= ) (source: ref<'A>) (value: 'A) = source.Value <- source.Value / value