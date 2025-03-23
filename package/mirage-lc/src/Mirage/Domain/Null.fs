// Credits: https://github.com/gilzoide/unity-fsharp/blob/main/Scripts/FSharpGlobals.fs
module Mirage.Domain.Null

open UnityEngine

/// Unity compatible isNotNull check.
let inline isNotNull (o: objnull) =
    match o with
    | null -> false
    | :? Object as unityObject -> Object.op_Implicit unityObject
    | _ -> true

/// Unity compatible isNull check.
let inline isNull (o: objnull) = isNotNull o |> not