namespace Mirage

open BepInEx
open System.IO
open System.Reflection
open Mirage.PluginInfo
open Mirage.Compatibility
open Mirage.Domain.Netcode
open Mirage.Domain.Setting
open Mirage.Domain.Config
open Mirage.Hook.AudioSpatializer
open Mirage.Hook.Prefab
open Mirage.Hook.Config
open Mirage.Hook.Microphone
open Mirage.Hook.Dissonance
open Mirage.Hook.MaskedPlayerEnemy
open Mirage.Domain.Directory

open Mirage.Core.Audio.Opus.Reader
open Mirage.Core.Audio.Opus.Codec

module Baz =
    let foobar () =
        Async.RunSynchronously <| async {
            let filePath = "C:/hello.opus"
            let! opusReader = readOpusFile "C:/hello.opus"
            printfn $"samples per packet: {SamplesPerPacket}"
            printfn $"done. samples: {opusReader.totalSamples}"


            let decoder = OpusDecoder()
            while opusReader.reader.HasNextPacket do 
                let packet = opusReader.reader.ReadNextRawPacket()
                if not <| isNull packet then
                    let pcmData = Array.zeroCreate<byte> <| PacketPcmLength
                    let decodedLength = decoder.Decode(packet, packet.Length, pcmData, PacketPcmLength)
                    printfn $"pcm length: {pcmData.Length} decodedLength: {decodedLength}"
                    ()


            //let frames = List<byte>()
            //let mutable frame = opusReader.reader.ReadNextRawPacket()

            //printfn $"total samples: {opusReader.totalSamples}" 
            //let decoder = OpusDecoder()
            
            //while not <| isNull frame do
            //    //let samples = Array.zeroCreate<float32> <| OpusSampleRate * OpusChannels
            //    let samples = Array.zeroCreate<float32> 100000000
            //    let decodedLength = decoder.Decode(frame, samples, samples.Length)
            //    if decodedLength <= 0 then
            //        printfn $"decodedLength: {decodedLength}"
            //    
            //    frame <- opusReader.reader.ReadNextRawPacket()
        }

[<BepInPlugin(pluginId, pluginName, pluginVersion)>]
[<BepInDependency(LethalSettings.GeneratedPluginInfo.Identifier, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LobbyCompatibility.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)>]
[<BepInDependency(LethalConfig.PluginInfo.Guid, BepInDependency.DependencyFlags.SoftDependency)>]
type Plugin() =
    inherit BaseUnityPlugin()

    member _.Awake() =
        Baz.foobar()

        let assembly = Assembly.GetExecutingAssembly()
        ignore <| Directory.CreateDirectory mirageDirectory

        // Credits goes to DissonanceLagFix: https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/
        //for category in Seq.cast<LogCategory> <| Enum.GetValues typeof<LogCategory> do
        //    Logs.SetLogLevel(category, LogLevel.Error)

        initLethalConfig assembly localConfig.General
        initLobbyCompatibility pluginName pluginVersion
        initSettings <| Path.Join(mirageDirectory, "settings.json")
        initNetcodePatcher()
        //Async.StartImmediate deleteRecordings
        //Application.add_quitting(fun _ -> Async.StartImmediate deleteRecordings)

        // Hooks.
        cacheDissonance()
        disableAudioSpatializer()
        registerPrefab()
        syncConfig()
        readMicrophone recordingDirectory
        hookMaskedEnemy()