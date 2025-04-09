using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// This class represents a single avatar card in the DLC store
public class AvatarCard : MonoBehaviour
{
    [Header("UI Elements")]
    public Image previewImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI priceText;
    public Button purchaseButton;
    public GameObject purchasedOverlay;

    [Header("Avatar Data")]
    public string avatarName;
    public int price;
    public string avatarPath;

    // Reference to purchase callback
    private Action<AvatarCard> onPurchaseCallback;

    public void Initialize(string name, int avatarPrice, string path, Action<AvatarCard> purchaseCallback)
    {
        // Set card data
        avatarName = name;
        price = avatarPrice;
        avatarPath = path;
        onPurchaseCallback = purchaseCallback;

        // Update UI
        nameText.text = avatarName;
        priceText.text = $"{price} Credits";

        // Set button listener
        purchaseButton.onClick.AddListener(() => OnPurchaseButtonClicked());

        // Hide purchased overlay by default
        if (purchasedOverlay != null)
        {
            purchasedOverlay.SetActive(false);
        }
    }

    public void SetPurchaseButtonState(bool interactable)
    {
        purchaseButton.interactable = interactable;
    }

    public void UpdateCardState(bool isPurchased)
    {
        // Show/hide purchased overlay
        if (purchasedOverlay != null)
        {
            purchasedOverlay.SetActive(isPurchased);
        }

        // Disable purchase button if already purchased
        if (isPurchased)
        {
            purchaseButton.interactable = false;
            purchaseButton.GetComponentInChildren<TextMeshProUGUI>().text = "Owned";
        }
        else
        {
            purchaseButton.GetComponentInChildren<TextMeshProUGUI>().text = "Purchase";
        }
    }

    private void OnPurchaseButtonClicked()
    {
        // Call the purchase callback
        onPurchaseCallback?.Invoke(this);
    }
}