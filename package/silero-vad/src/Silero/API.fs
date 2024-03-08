module Silero.API

open System
open Silero.Foreign
open System.IO

type SileroVAD = private SileroVAD of IntPtr

type LogLevel
    = Verbose = 0
    | Info = 1
    | Warning = 2
    | Error = 3
    | Fatal = 4

type SileroInitParams =
    {   cpuThreads: int
        workers: int
    }

/// <summary>
/// Initialize <b>SileroVAD</b> in order to detect if speech is found in audio.
/// </summary>
let initSilero initParams =
    let vad =
        init_silero
            {   model_path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model/silero-vad/silero_vad.onnx")
                intra_threads = initParams.cpuThreads
                inter_threads = initParams.workers
                log_level = int LogLevel.Error
            }
    SileroVAD vad

/// <summary>
/// Release all native resources held by <b>SileroVAD</b>.
/// </summary>
let releaseSilero (SileroVAD vad) = release_silero vad

/// <summary>
/// Detects if speech is found in the given audio samples. This assumes the following:<br />
/// - Pcm data contains a single frame containing 30ms of audio.
/// - Sample rate is 16khz. If the audio isn't 16khz, this will result in undefined behaviour.
/// 
/// While you <i>can</i> pass more than 30ms of audio, the resulting probability is only for the first frame.
/// </summary>
/// <returns>
/// A number within the range 0f-1f.<br />
/// The closer the number to 0f, the less likely speech is found.<br />
/// The closer the number to 1f, the more likely speech is found.
/// </returns>
let detectSpeech (SileroVAD vad) pcmData = detect_speech(vad, pcmData, pcmData.Length)