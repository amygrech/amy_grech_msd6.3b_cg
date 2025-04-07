using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Parallax : MonoBehaviour
{
    [Header("References")]        
    public RawImage rawImage;        
    [Header("Parameters")]        
    public Vector2 speed = new Vector2(1, 0);        
    public Vector2 size = new Vector2(256, 256);

    private void Awake()
    {
        if (rawImage == null)
        {
            rawImage = GetComponent<RawImage>();
        }
    }

    private void Reset()
    {
        rawImage = GetComponent<RawImage>();
    }

    private void Update()
    {
        Vector2 calculatedSizeBasedOnScreen = new Vector2(rawImage.rectTransform.rect.width / size.x,rawImage.rectTransform.rect.height / size.y);            
        rawImage.uvRect = new Rect(rawImage.uvRect.position + (speed * Time.deltaTime),calculatedSizeBasedOnScreen);
    }
}


