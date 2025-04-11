using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class ChessPieceState
{
    public string pieceType;
    public string color;
    public string position;
}

[Serializable]
public class GameStateWrapper
{
    public List<ChessPieceState> pieces = new List<ChessPieceState>();
}

public class GameSessionSyncManager : NetworkBehaviour
{
    private string currentMatchId;
    private GameStateWrapper currentGameState = new GameStateWrapper();

    public void SetMatchId(string matchId)
    {
        currentMatchId = matchId;
    }

    public void SetCurrentGameState(List<ChessPieceState> boardState)
    {
        currentGameState.pieces = boardState;
    }

    [ContextMenu("Save Current Game State")]
    public void SaveSessionState()
    {
        if (!IsServer) return;

        if (string.IsNullOrEmpty(currentMatchId))
            currentMatchId = Guid.NewGuid().ToString();

        string json = JsonUtility.ToJson(currentGameState);

        FirebaseManager.Instance.SaveGameState(currentMatchId, json, success =>
        {
            if (success)
            {
                Debug.Log("[Server] Game state saved successfully.");
                NotifyClientsOfSaveClientRpc(currentMatchId);
            }
        });
    }

    [ClientRpc]
    private void NotifyClientsOfSaveClientRpc(string matchId)
    {
        Debug.Log($"[Client] Game state saved for match ID: {matchId}");
    }

    [ContextMenu("Restore Game State")]
    public void RestoreSessionState(string matchId)
    {
        if (!IsServer) return;

        FirebaseManager.Instance.LoadGameState(matchId, json =>
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("No state found for match ID: " + matchId);
                return;
            }

            GameStateWrapper restored = JsonUtility.FromJson<GameStateWrapper>(json);
            currentGameState = restored;
            currentMatchId = matchId;

            Debug.Log("[Server] Restored game state.");
            BroadcastRestoredStateClientRpc(json);
        });
    }

    [ClientRpc]
    private void BroadcastRestoredStateClientRpc(string restoredJson)
    {
        GameStateWrapper restored = JsonUtility.FromJson<GameStateWrapper>(restoredJson);

        foreach (var piece in restored.pieces)
        {
            Debug.Log($"[Client] Restore {piece.color} {piece.pieceType} at {piece.position}");
            // Instantiate or move pieces on the board here
        }
    }
}
