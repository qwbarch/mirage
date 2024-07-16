module Predictor.DomainBytes
open Domain
open FSharpPlus

let getSizeCompressedObs (obs: CompressedObservation): int64 = 
    let getSizeEmb (emb: ObsEmbedding): int64 =
        match emb with
        | Prev -> 4
        | Value None -> 4
        | Value (Some (str, textEmbedding: TextEmbedding)) ->
            let textEmbSize: int64 = int64 <| 4 * Embedding.EMBEDDING_SIZE
            4L + 4L*int64(str.Length) + textEmbSize
    // DateTime: 8 bytes
    8L + 4L + 4L + getSizeEmb obs.heardEmbedding + getSizeEmb obs.spokeEmbedding

let getSizeAction (action: FutureAction): int64 =
    match action with
    | NoAction -> 4
    | QueueAction q ->
        let audioResponse: AudioResponse = q.action
        let responseSize = 
            let embeddingSize: int64 =
                match audioResponse.embedding with
                | None -> 4
                | Some (str, textEmbedding) ->
                    let textEmbSize: int64 = int64 <| 4 * Embedding.EMBEDDING_SIZE
                    4L + 4L*int64(str.Length) + textEmbSize
            let whisperTimingsSize: int64 = 
                let innerSize (inner: int * SpokeAtom): int64 =
                    let _, spokeAtom = inner
                    4L + 4L + 8L + 8L + 8L + 4L*int64(spokeAtom.text.Length)
                audioResponse.whisperTimings
                |> map innerSize
                |> sum
            let vadSize: int64 = 20L * int64(audioResponse.vadTimings.Length)
            8L + 4L + embeddingSize + whisperTimingsSize + vadSize
        4L + responseSize