module App

open FSharpPlus
open NAudio.Lame
open NAudio.Wave
open Silero.API
open System
open Mirage.Core.Audio.PCM
open Mirage.Core.Audio.Speech
open Mirage.Core.Audio.File.Mp3Writer
open System.IO

let [<Literal>] WindowSize = 1024
let [<Literal>] private WriterPreset = LAMEPreset.STANDARD
let private WriterFormat = WaveFormat(16000, 1)

[<EntryPoint>]
let main _ =
    let rootDirectory = $"{Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)}"
    ignore <| LameDLL.LoadNativeDLL [|rootDirectory|]
    let silero = SileroVAD <| WindowSize
    let mutable writer = None
    let onSpeechDetected detection =
        async {
            match detection with
                | SpeechStart ->
                    printfn "speech started"
                    let filePath = $"{rootDirectory}/Audio/{DateTime.UtcNow.ToFileTime()}.mp3"
                    let! mp3Writer = createMp3Writer filePath WriterFormat WriterPreset
                    writer <- Some mp3Writer
                | SpeechEnd ->
                    printfn "speech ended"
                    do! closeMp3Writer writer.Value
                    writer <- None
                | SpeechFound samples ->
                    do! writeMp3File writer.Value samples
        }
    let speechDetector =  SpeechDetector (result << detectSpeech silero) onSpeechDetected
    let waveIn = new WaveInEvent()
    waveIn.WaveFormat <- new WaveFormat(16000, 16, 1)
    waveIn.BufferMilliseconds <- int <| float WindowSize / 16000.0 * 1000.0
    let onDataAvailable _ (event: WaveInEventArgs) =
        let samples = fromPCMBytes <| event.Buffer
        Async.RunSynchronously <| writeSamples speechDetector samples
    waveIn.DataAvailable.AddHandler <| new EventHandler<WaveInEventArgs>(onDataAvailable)
    waveIn.StartRecording()
    for _ in 0 .. 100 do
        printfn ""
    ignore <| Console.ReadLine()
    releaseSilero silero
    0