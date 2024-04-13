module Mirage.Unity.RpcBehaviour

open UnityEngine
open Photon.Pun
open MyceliumNetworking
open Mirage.Core.Logger

let [<Literal>] private NetworkId = 4293986444u

[<AllowNullLiteral>]
type RpcBehaviour() =
    inherit MonoBehaviour()

    member val PhotonView: PhotonView = null with get, set

    member val IsHost = false with get, set

    abstract member Awake : unit -> unit
    default this.Awake() =
        this.IsHost <- PhotonNetwork.IsMasterClient
        this.PhotonView <- this.GetComponent<PhotonView>()
        MyceliumNetwork.RegisterNetworkObject(
            this,
            NetworkId,
            this.PhotonView.ViewID
        )

    abstract member OnDestroy : unit -> unit
    default this.OnDestroy() =
        MyceliumNetwork.DeregisterNetworkObject(
            this,
            NetworkId,
            this.PhotonView.ViewID
        )

/// Run an rpc method on the server, using the specified reliability.
let private serverRpc' (this: RpcBehaviour) reliability methodName payload =
    if this.IsHost then logError "serverRpc can only be invoked by a non-host."
    else
        MyceliumNetwork.RPCTargetMasked(
            NetworkId,
            methodName,
            MyceliumNetwork.LobbyHost,
            reliability,
            this.PhotonView.ViewID,
            payload
        )

/// Run an rpc method on the server, using <b>ReliableType.Reliable</b> by default.
let internal serverRpc this = serverRpc' this ReliableType.Reliable 

/// Run an rpc method on all clients, using the specified reliability.
let internal clientRpc' (this: RpcBehaviour) reliability methodName payload =
    if not this.IsHost then logError "clientRpc can only be invoked by the host."
    else
        MyceliumNetwork.RPCMasked(
            NetworkId,
            methodName,
            reliability,
            this.PhotonView.ViewID,
            payload
        )

/// Run an rpc method on all clients, using <b>ReliableType.Reliable</b> by default.
let internal clientRpc this = clientRpc' this ReliableType.Reliable