using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class ChessPieceNetwork : NetworkBehaviour {
    public void RequestMove(string startSquareName, string targetSquareName) {
        if (!IsOwner) return;
        if (GameManager.Instance == null) {  // If using the new GameManager, update the reference accordingly.
            Debug.LogWarning("ChessPieceNetwork: GameManager instance is null!");
            return;
        }
        ulong pieceNetworkId = GetComponent<NetworkObject>().NetworkObjectId;
        // Assuming the GameManager (networked version) has been renamed to GameManager.
        GameManager.Instance.RequestChessMoveServerRpc(startSquareName, targetSquareName, pieceNetworkId);
    }
}