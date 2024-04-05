module Mirage.Networking

open UnityEngine
open Steamworks
open MyceliumNetworking

let [<Literal>] NetworkId = 4293986444u

/// Register the <b>MonoBehaviour</b> to use the <b>CustomRpc</b> attribute.
let registerNetwork (object: MonoBehaviour) =
    MyceliumNetwork.RegisterNetworkObject(
        object,
        NetworkId
        //object.GetComponent<PhotonView>().ViewID
    )

type RpcTarget
    = Host
    | Client of CSteamID

/// Call a method via RPC, using the specified reliability.
let callRpc' reliability methodName target mask payload =
    let recipient =
        match target with
            | Host -> MyceliumNetwork.LobbyHost
            | Client id -> id
    MyceliumNetwork.RPCTargetMasked(NetworkId, methodName, recipient, reliability, mask, payload)

/// Call a method via RPC, using <b>ReliableType.Reliable</b>.
let callRpc = callRpc' ReliableType.Reliable