module Silero.VAD

open FSharpPlus
open System
open System.IO
open Microsoft.ML.OnnxRuntime;
open System.Collections.Generic

// TODO: Support CUDA
type SileroVAD =
    private
        {   session: InferenceSession
            runOptions: RunOptions
            mutable h: OrtValue
            mutable c: OrtValue
        }

let private createTensor tensorData =
    OrtValue.CreateTensorValueFromMemory(tensorData, [|tensorData.Length|])

let private createTensor2D tensorData =
    let flattenedData = join tensorData
    OrtValue.CreateTensorValueFromMemory(flattenedData, [|tensorData.Length; tensorData[0].Length|])

let private createTensor3D tensorData =
    let flattenedData = join <| join tensorData
    OrtValue.CreateTensorValueFromMemory(
        flattenedData,
        [|tensorData.Length; tensorData[0].Length; tensorData.[0].[0].Length|]
    )

type InitSileroParams =
    {   workers: int
        cpuThreads: int
    }

let initSilero modelParams =
    let baseDirectory = AppDomain.CurrentDomain.BaseDirectory
    let options = new SessionOptions()
    options.InterOpNumThreads <- modelParams.workers
    options.IntraOpNumThreads <- modelParams.cpuThreads
    let init3D () = Array.init 2 << konst << Array.init 64 << konst <| Array.zeroCreate<float32> 4
    {   session = new InferenceSession(Path.Join(baseDirectory, "model/silero-vad/silero_vad.onnx"))
        runOptions = new RunOptions()
        h = createTensor3D <| init3D()
        c = createTensor3D <| init3D()
    }

/// <summary>
/// Detect if the audio samples contains speech.<br />
/// Note: This assumes the sample rate is 16khz, and does not perform any input validation.
/// </summary>
let detectSpeech silero (samples: array<float32>) =
    // TODO: convert run to async
    // TODO: dispose the tensors
    let inputs = new Dictionary<string, OrtValue>()
    printfn "before create tensor input"
    inputs["input"] <- createTensor2D [|samples|]
    inputs["sr"] <- createTensor [|16000L|]
    inputs["h"] <- silero.h
    inputs["c"] <- silero.c
    printfn "before session run"
    use outputs = silero.session.Run(silero.runOptions, inputs, silero.session.OutputNames)
    printfn "before outputs value"
    silero.h <- outputs[1].Value
    silero.c <- outputs[2].Value
    printfn "before return"
    outputs[0].Value.GetTensorMutableDataAsSpan<float32>().ToArray()