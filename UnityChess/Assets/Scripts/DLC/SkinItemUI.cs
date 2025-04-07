using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkinItemUI : MonoBehaviour
{
    public Image previewImage;
    public TMP_Text nameText;
    public TMP_Text priceText;
    public Button buyButton;

    private SkinData skinData;

    public async void Initialize(SkinData data)
    {
        skinData = data;
        nameText.text = data.name;
        priceText.text = $"{data.price} Coins";

        var sprite = await SkinManager.Instance.DownloadSkinPreview(data.imageUrl);
        previewImage.sprite = sprite;

        buyButton.onClick.AddListener(() => PurchaseManager.Instance.TryPurchaseSkin(skinData));
    }
}