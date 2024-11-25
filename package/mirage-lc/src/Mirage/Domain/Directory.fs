module Mirage.Domain.Directory

open System.IO
open System.Diagnostics

let baseDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName)
let mirageDirectory = Path.Join(baseDirectory, "Mirage")
let recordingDirectory = Path.Join(mirageDirectory, "Recording")