using UnityEngine;

public class DLCStoreAnalyticsIntegration : MonoBehaviour
{
    private DLCStoreManager storeManager;
    private GameAnalyticsManager analyticsManager;
    
    private void Awake()
    {
        storeManager = GetComponent<DLCStoreManager>();
        analyticsManager = FindObjectOfType<GameAnalyticsManager>();
        
        if (storeManager == null)
        {
            Debug.LogError("DLCStoreManager component not found on the same GameObject. Analytics integration will be disabled.");
            enabled = false;
        }
        
        if (analyticsManager == null)
        {
            Debug.LogWarning("GameAnalyticsManager not found in the scene. Purchase analytics will not be tracked.");
        }
    }
    
    private void Start()
    {
        // Hook up to the purchase event
        // We'll use a helper method to connect to existing code without modifying it directly
        AddPurchaseListener();
    }
    
    private void AddPurchaseListener()
    {
        // Add a listener to each avatar card's purchase button
        if (storeManager.avatarCards != null && storeManager.avatarCards.Length > 0)
        {
            foreach (var card in storeManager.avatarCards)
            {
                if (card != null && card.purchaseButton != null)
                {
                    // Add our listener that will fire alongside the original OnPurchaseAvatar method
                    card.purchaseButton.onClick.AddListener(() => OnAvatarPurchase(card));
                }
            }
            
            Debug.Log($"Added analytics tracking to {storeManager.avatarCards.Length} DLC items");
        }
        else
        {
            Debug.LogWarning("No avatar cards found in DLCStoreManager. Purchase analytics may not work correctly.");
        }
    }
    
    private void OnAvatarPurchase(AvatarCard card)
    {
        // Only log the purchase if the player has enough credits (avoiding duplicate logs if they click but can't afford)
        if (storeManager.playerCredits >= card.price)
        {
            // Log to analytics after a short delay to ensure the purchase is actually completed
            Invoke("LogPurchase", 0.1f);
        }
    }
    
    private void LogPurchase(AvatarCard card)
    {
        if (analyticsManager != null)
        {
            analyticsManager.LogDLCPurchaseEvent(card.avatarPath, card.avatarName, card.price);
            Debug.Log($"Logged purchase analytics for {card.avatarName}");
        }
    }
}