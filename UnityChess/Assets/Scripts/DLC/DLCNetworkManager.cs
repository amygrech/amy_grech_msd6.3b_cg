using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// This class handles the synchronization of DLC skins between connected clients
public class DLCNetworkManager : NetworkBehaviour
{
    private static DLCNetworkManager instance;
    public static DLCNetworkManager Instance => instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // When a new client connects, send them the current skins in use
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
        }

        // When we join a game, request current skin data
        if (IsClient && !IsServer)
        {
            RequestActiveSkinDataServerRpc(NetworkManager.LocalClientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        }

        base.OnNetworkDespawn();
    }

    private void OnClientConnected(ulong clientId)
    {
        // Send active skin data to the newly connected client
        SendActiveSkinDataToClientRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SkinActivatedServerRpc(string skinId, ulong activatingClientId)
    {
        // The server receives a notification that a client has activated a skin
        // Broadcast this to all other clients
        SkinActivatedClientRpc(skinId, activatingClientId);
    }

    [ClientRpc]
    private void SkinActivatedClientRpc(string skinId, ulong activatingClientId)
    {
        // Skip if this is the client that activated the skin
        if (NetworkManager.LocalClientId == activatingClientId)
            return;

        // Apply the skin for the specified client
        DLCManager.Instance.ApplySkinForPlayer(skinId, activatingClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestActiveSkinDataServerRpc(ulong requestingClientId)
    {
        if (IsServer)
        {
            // Send active skin data to the requesting client
            SendActiveSkinDataToClientRpc(requestingClientId);
        }
    }

    [ClientRpc]
    private void SendActiveSkinDataToClientRpc(ulong targetClientId)
    {
        if (NetworkManager.LocalClientId != targetClientId)
            return;

        // Request active skin information from all clients
        RequestSkinInfoFromAllClientsServerRpc(targetClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSkinInfoFromAllClientsServerRpc(ulong requestingClientId)
    {
        // Relay the request to all clients except the requesting one
        RequestSkinInfoClientRpc(requestingClientId);
    }

    [ClientRpc]
    private void RequestSkinInfoClientRpc(ulong requestingClientId)
    {
        // Skip if this is the requesting client
        if (NetworkManager.LocalClientId == requestingClientId)
            return;

        // Send our active skin to the requesting client
        string activeSkinId = DLCManager.Instance.GetActiveSkinId();
        if (!string.IsNullOrEmpty(activeSkinId))
        {
            SendSkinInfoToClientServerRpc(activeSkinId, NetworkManager.LocalClientId, requestingClientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendSkinInfoToClientServerRpc(string skinId, ulong owningClientId, ulong targetClientId)
    {
        // Relay skin info to the specific client
        ReceiveSkinInfoClientRpc(skinId, owningClientId, targetClientId);
    }

    [ClientRpc]
    private void ReceiveSkinInfoClientRpc(string skinId, ulong owningClientId, ulong targetClientId)
    {
        // Only process if we're the target client
        if (NetworkManager.LocalClientId != targetClientId)
            return;

        // Apply the skin for the specified client
        DLCManager.Instance.ApplySkinForPlayer(skinId, owningClientId);
    }
}