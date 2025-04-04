using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Controls whether a chess piece can be moved based on network state and player side.
/// This is a simpler alternative to using NetworkObject components.
/// </summary>
public class PieceNetworkController : MonoBehaviour
{
    private VisualPiece visualPiece;
    
    void Start()
    {
        visualPiece = GetComponent<VisualPiece>();
        
        // If we're in a networked game, we need to check if this piece is movable
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            UpdatePieceMoveability();
        }
    }
    
    void Update()
    {
        // Only check in networked games
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            UpdatePieceMoveability();
        }
    }
    
    /// <summary>
    /// Updates whether this piece can be moved based on the network state and player side.
    /// </summary>
    private void UpdatePieceMoveability()
    {
        if (visualPiece != null && ChessNetworkManager.Instance != null)
        {
            // Check if current player can move this piece
            bool canMove = ChessNetworkManager.Instance.CanMoveCurrentPiece(visualPiece.PieceColor);
            
            // Only change if needed to avoid constant updates
            if (visualPiece.enabled != canMove)
            {
                visualPiece.enabled = canMove;
            }
        }
    }
    
    /// <summary>
    /// This method can be called when the piece is created to set its color.
    /// </summary>
    public void SetPieceColor(Side color)
    {
        if (visualPiece != null)
        {
            visualPiece.PieceColor = color;
            UpdatePieceMoveability();
        }
    }
}