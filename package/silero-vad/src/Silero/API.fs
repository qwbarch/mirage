module Silero.API

open System
open Silero.Foreign
open System.IO
open System.Reflection

type SileroVAD = private SileroVAD of IntPtr

type LogLevel
    = Verbose = 0
    | Info = 1
    | Warning = 2
    | Error = 3
    | Fatal = 4

/// <summary>
/// Initialize <b>SileroVAD</b> in order to detect if speech is found in audio.
/// </summary>
let SileroVAD windowSize =
    let baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
    let vad =
        init_silero
            {   onnxruntime_path = Path.Combine(baseDirectory, "onnxruntime.dll")
                model_path = Path.Combine(baseDirectory, "silero_vad.onnx")
                intra_threads = 1
                inter_threads = 1
                log_level = int LogLevel.Error
                window_size = windowSize
            }
    SileroVAD vad

/// <summary>
/// Release all native resources held by <b>SileroVAD</b>.
/// </summary>
let releaseSilero (SileroVAD vad) = release_silero vad

/// <summary>
/// Detects if speech is found in the given audio samples. This assumes the following:<br />
/// - Pcm data contains WINDOW_SIZE samples (constant defined in the C source).
/// - Sample rate is 16khz.
/// - Audio is mono-channel.
/// - Each sample contains 2 bytes.
/// 
/// Not following these requirements results in undefined behaviour, and can potentially crash.
/// </summary>
/// <returns>
/// A number within the range 0f-1f.<br />
/// The closer the number to 0f, the less likely speech is found.<br />
/// The closer the number to 1f, the more likely speech is found.
/// </returns>
let detectSpeech (SileroVAD vad) pcmData pcmLength = detect_speech(vad, pcmData, int pcmLength)