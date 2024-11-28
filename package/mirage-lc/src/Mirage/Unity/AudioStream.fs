module Mirage.Unity.AudioStream

open System
open FSharpPlus
open UnityEngine
open Unity.Netcode
open Mirage.Core.Audio.PCM
open Mirage.Domain.Logger

[<Struct>]
type AudioStartEvent =
    {   /// Number of sample frames.
        lengthSamples: int
        /// Number of channels per frame.
        channels: int
        /// Sample frequency of the audio.
        frequency: int
    }


[<Struct>]
type AudioReceivedEvent =
    {   /// Audio signal containing a single decompressed mp3 frame.
        samples: Samples
        /// Index of where the sample belongs in, relative to the whole audio clip.
        sampleIndex: int
    }

[<Struct>]
type AudioStreamEvent
    /// Event that is trigered when a new audio clip begins.
    = AudioStartEvent of audioStartEvent: AudioStartEvent
    /// Event that is trigered when audio samples are received.
    | AudioReceivedEvent of audioReceivedEvent: AudioReceivedEvent

type AudioStreamEventArgs(eventData: AudioStreamEvent) =
    inherit EventArgs()
    member _.EventData = eventData

[<AllowNullLiteral>]
type AudioStream() as self =
    inherit NetworkBehaviour()

    let event = Event<EventHandler<_>, _>()
    let onFrameDecompressed (samples: Samples) sampleIndex =
        let eventData =
            AudioReceivedEvent
                {   samples = samples
                    sampleIndex = sampleIndex
                }
        event.Trigger(self, AudioStreamEventArgs(eventData))

    /// Run the callback if the sender client id matches the <b>AllowedSenderId</b> value.
    let onValidSender (this: NetworkBehaviour) (serverRpcParams: ServerRpcParams) callback =
        let clientId = serverRpcParams.Receive.SenderClientId
        if this.IsHost && this.NetworkManager.ConnectedClients.ContainsKey clientId && Some clientId = self.AllowedSenderId then
            callback()

    /// An event that triggers when a new audio clip begins.
    [<CLIEvent>]
    member _.OnAudioStream =  event.Publish

    member val AudioSource: AudioSource = null with get, set

    /// The client id of the client that is allowed to broadcast audio to other clients.
    member val AllowedSenderId: Option<uint64> = None with get, set

    override _.OnDestroy() =
        base.OnDestroy()
        //iter dispose audioSender
        //iter dispose audioReceiver

    /// Stream audio from the player (can be host or non-host) to all other players.
    member this.StreamAudioFromFile(filePath) = ()