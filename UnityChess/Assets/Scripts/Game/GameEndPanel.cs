using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityChess;

/// <summary>
/// Controls the game end UI panel displayed when a chess game concludes
/// </summary>
public class GameEndPanel : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button resignButton;
    [SerializeField] private Button rematchButton;
    
    [Header("Panel Settings")]
    [SerializeField] private Color whiteWinColor = new Color(0.9f, 0.9f, 1.0f);
    [SerializeField] private Color blackWinColor = new Color(0.3f, 0.3f, 0.3f);
    [SerializeField] private Color drawColor = new Color(0.7f, 0.7f, 0.7f);
    [SerializeField] private Image panelBackground;
    
    private NetworkGameEndDetector endDetector;
    
    private void Awake()
    {
        // Hide the panel on start
        gameObject.SetActive(false);
        
        // Set up button listeners
        SetupButtonListeners();
        
        // Find the end detector
        endDetector = FindObjectOfType<NetworkGameEndDetector>();
    }
    
    private void OnEnable()
    {
        // Update the panel when it's shown
        UpdatePanel();
    }
    
    /// <summary>
    /// Sets up the button click listeners
    /// </summary>
    private void SetupButtonListeners()
    {
        if (resignButton != null)
        {
            resignButton.onClick.AddListener(() => {
                if (endDetector != null)
                {
                    // Get the local player's side
                    Side localPlayerSide = ChessNetworkManager.Instance.GetLocalPlayerSide();
                    int resigningSideInt = localPlayerSide == Side.White ? 0 : 1;
                    
                    // Request resignation
                    endDetector.RequestResignationServerRpc(resigningSideInt);
                    
                    // Disable resign button after clicking
                    resignButton.interactable = false;
                }
            });
        }
        
        if (rematchButton != null)
        {
            rematchButton.onClick.AddListener(() => {
                if (endDetector != null)
                {
                    // Request rematch
                    endDetector.RequestRematchServerRpc();
                    
                    // Hide the panel
                    gameObject.SetActive(false);
                }
            });
        }
    }
    
    /// <summary>
    /// Updates the panel UI based on the game end state
    /// </summary>
    public void UpdatePanel()
    {
        if (endDetector == null)
        {
            messageText.text = "Game Ended";
            return;
        }
        
        // Get the current game end state
        NetworkGameEndDetector.GameEndReason endReason = endDetector.GetGameEndReason();
        Side winningSide = endDetector.GetWinningSide();
        
        // Set the panel color based on the winner
        if (panelBackground != null)
        {
            if (winningSide == Side.White)
                panelBackground.color = whiteWinColor;
            else if (winningSide == Side.Black)
                panelBackground.color = blackWinColor;
            else
                panelBackground.color = drawColor;
        }
        
        // Set the title and message
        if (titleText != null)
        {
            switch (endReason)
            {
                case NetworkGameEndDetector.GameEndReason.Checkmate:
                    titleText.text = "Checkmate!";
                    break;
                case NetworkGameEndDetector.GameEndReason.Stalemate:
                    titleText.text = "Stalemate";
                    break;
                case NetworkGameEndDetector.GameEndReason.Resignation:
                    titleText.text = "Resignation";
                    break;
                case NetworkGameEndDetector.GameEndReason.Timeout:
                    titleText.text = "Time's Up";
                    break;
                case NetworkGameEndDetector.GameEndReason.Disconnection:
                    titleText.text = "Player Disconnected";
                    break;
                default:
                    titleText.text = "Game Over";
                    break;
            }
        }
        
        // Set message text based on end reason
        if (messageText != null)
        {
            switch (endReason)
            {
                case NetworkGameEndDetector.GameEndReason.Checkmate:
                    messageText.text = $"{winningSide} wins by checkmate!";
                    break;
                case NetworkGameEndDetector.GameEndReason.Stalemate:
                    messageText.text = "The game is a draw by stalemate.";
                    break;
                case NetworkGameEndDetector.GameEndReason.Resignation:
                    Side resigningSide = winningSide == Side.White ? Side.Black : Side.White;
                    messageText.text = $"{resigningSide} resigned. {winningSide} wins!";
                    break;
                case NetworkGameEndDetector.GameEndReason.Timeout:
                    Side timeoutSide = winningSide == Side.White ? Side.Black : Side.White;
                    messageText.text = $"{timeoutSide} ran out of time. {winningSide} wins!";
                    break;
                case NetworkGameEndDetector.GameEndReason.Disconnection:
                    Side disconnectedSide = winningSide == Side.White ? Side.Black : Side.White;
                    messageText.text = $"{disconnectedSide} disconnected. {winningSide} wins!";
                    break;
                default:
                    messageText.text = "The game has ended.";
                    break;
            }
        }
        
        // Disable resign button if the game is already over
        if (resignButton != null)
        {
            resignButton.interactable = !endDetector.IsGameOver();
        }
    }
    
    /// <summary>
    /// Shows the panel with the game result
    /// </summary>
    public void ShowPanel()
    {
        UpdatePanel();
        gameObject.SetActive(true);
    }
    
    /// <summary>
    /// Hides the panel
    /// </summary>
    public void HidePanel()
    {
        gameObject.SetActive(false);
    }
}