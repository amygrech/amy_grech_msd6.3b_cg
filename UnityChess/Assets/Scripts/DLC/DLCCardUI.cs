using System;
using UnityEngine;
using UnityEngine.UI;

public class DLCCardUI : MonoBehaviour
{
    [SerializeField] private Text titleText;
    [SerializeField] private Image previewImage;
    [SerializeField] private Text priceText;
    [SerializeField] private Button actionButton;
    [SerializeField] private Text buttonText;
    
    private DLCManager.DLCSkin skin;
    private Action<DLCManager.DLCSkin> onPurchaseClicked;
    private Action<DLCManager.DLCSkin> onImageLoaded;
    
    public string SkinId => skin?.id;
    
    public void Initialize(DLCManager.DLCSkin skinData, Action<DLCManager.DLCSkin> purchaseCallback, Action<DLCManager.DLCSkin> imageLoadedCallback)
    {
        skin = skinData;
        onPurchaseClicked = purchaseCallback;
        onImageLoaded = imageLoadedCallback;
        
        titleText.text = skin.name;
        UpdateButtonState();
        
        // Set a placeholder image until the real one loads
        previewImage.color = Color.gray;
        
        // Set price text
        priceText.text = skin.isPurchased ? "Owned" : skin.price + " Credits";
        
        // Set up button
        actionButton.onClick.AddListener(OnButtonClicked);
    }
    
    public void UpdatePreviewImage(Sprite sprite)
    {
        if (sprite != null)
        {
            previewImage.sprite = sprite;
            previewImage.color = Color.white;
        }
    }
    
    public void UpdatePurchaseState(bool isPurchased)
    {
        if (skin != null)
        {
            skin.isPurchased = isPurchased;
            UpdateButtonState();
            priceText.text = isPurchased ? "Owned" : skin.price + " Credits";
        }
    }
    
    private void UpdateButtonState()
    {
        buttonText.text = skin.isPurchased ? "Use" : "Purchase";
    }
    
    private void OnButtonClicked()
    {
        if (onPurchaseClicked != null)
        {
            onPurchaseClicked(skin);
        }
    }
    
    private void OnDestroy()
    {
        actionButton.onClick.RemoveListener(OnButtonClicked);
    }
}