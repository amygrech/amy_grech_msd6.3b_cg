using UnityEngine;
using UnityEngine.UI;

public class DLCStoreInitializer : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject dlcStorePanel;
    [SerializeField] private Button openStoreButton;
    [SerializeField] private Button closeStoreButton;
    [SerializeField] private Text creditsText;
    
    [Header("Prefabs")]
    [SerializeField] private GameObject avatarCardPrefab;
    
    [Header("Purchase UI")]
    [SerializeField] private GameObject purchaseConfirmationPanel;
    [SerializeField] private Text confirmationText;
    [SerializeField] private Button confirmPurchaseButton;
    [SerializeField] private Button cancelPurchaseButton;
    
    private void Start()
    {
        // Hide the store panel initially
        dlcStorePanel.SetActive(false);
        purchaseConfirmationPanel.SetActive(false);
        
        // Set up button listeners
        openStoreButton.onClick.AddListener(OpenDLCStore);
        closeStoreButton.onClick.AddListener(CloseDLCStore);
        
        // Initialize DLC Manager
        DLCManager dlcManager = FindObjectOfType<DLCManager>();
        if (dlcManager == null)
        {
            dlcManager = gameObject.AddComponent<DLCManager>();
        }
        
        // Pass references to the DLC Manager
        dlcManager.InitializeUIReferences(
            dlcStorePanel.transform,
            avatarCardPrefab,
            creditsText,
            purchaseConfirmationPanel,
            confirmationText,
            confirmPurchaseButton,
            cancelPurchaseButton
        );
    }
    
    private void OpenDLCStore()
    {
        dlcStorePanel.SetActive(true);
    }
    
    private void CloseDLCStore()
    {
        dlcStorePanel.SetActive(false);
    }
    
    private void OnDestroy()
    {
        if (openStoreButton != null)
            openStoreButton.onClick.RemoveListener(OpenDLCStore);
            
        if (closeStoreButton != null)
            closeStoreButton.onClick.RemoveListener(CloseDLCStore);
    }
}