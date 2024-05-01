module Silero.Foreign

open System
open System.Runtime.InteropServices

let [<Literal>] dll = "SileroVAD.API.dll"

[<Struct>]
[<StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)>]
type internal SileroInitParams =
    {   onnxruntime_path: string
        model_path: string
        log_level: int
        inter_threads: int
        intra_threads: int
        window_size: int
    }

[<DllImport(dll)>]
extern IntPtr internal init_silero(SileroInitParams)

[<DllImport(dll)>]
extern void internal release_silero(IntPtr)

[<DllImport(dll)>]
extern float32 internal detect_speech(IntPtr, float32[], int64)