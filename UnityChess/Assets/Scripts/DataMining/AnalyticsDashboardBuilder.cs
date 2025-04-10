using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// This class helps set up the Analytics Dashboard UI programmatically.
/// You can use this to create the dashboard at runtime if it doesn't exist in your scene.
/// </summary>
public class AnalyticsDashboardBuilder : MonoBehaviour
{
    [Header("Dashboard Prefab Generation")]
    [SerializeField] private bool createOnStart = false;
    [SerializeField] public Canvas targetCanvas;
    
    [Header("Dashboard Design")]
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
    [SerializeField] private Color headerColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color buttonColor = new Color(0.2f, 0.4f, 0.8f, 1f);
    [SerializeField] private Font uiFont;
    
    private void Start()
    {
        if (createOnStart)
        {
            if (targetCanvas == null)
            {
                // Try to find Canvas in scene
                targetCanvas = FindObjectOfType<Canvas>();
                if (targetCanvas == null)
                {
                    Debug.LogError("No Canvas found. Cannot create Analytics Dashboard UI.");
                    return;
                }
            }
            
            CreateAnalyticsDashboard();
        }
    }
    
    /// <summary>
    /// Creates an analytics dashboard UI on the target canvas.
    /// </summary>
    public GameObject CreateAnalyticsDashboard()
    {
        // Create the main panel
        GameObject dashboardPanel = CreateUIPanel("AnalyticsDashboard", targetCanvas.transform);
        RectTransform dashboardRect = dashboardPanel.GetComponent<RectTransform>();
        dashboardRect.anchorMin = new Vector2(0.2f, 0.1f);
        dashboardRect.anchorMax = new Vector2(0.8f, 0.9f);
        dashboardRect.offsetMin = Vector2.zero;
        dashboardRect.offsetMax = Vector2.zero;
        
        // Create header
        GameObject headerPanel = CreateUIPanel("HeaderPanel", dashboardPanel.transform);
        RectTransform headerRect = headerPanel.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.sizeDelta = new Vector2(0, 60);
        headerPanel.GetComponent<Image>().color = headerColor;
        
        // Create header title
        Text headerText = CreateUIText("HeaderText", headerPanel.transform, "Chess Analytics Dashboard");
        RectTransform headerTextRect = headerText.GetComponent<RectTransform>();
        headerTextRect.anchorMin = new Vector2(0, 0);
        headerTextRect.anchorMax = new Vector2(1, 1);
        headerTextRect.offsetMin = new Vector2(20, 0);
        headerTextRect.offsetMax = new Vector2(-20, 0);
        headerText.alignment = TextAnchor.MiddleCenter;
        headerText.fontSize = 24;
        
        // Create close button
        Button closeButton = CreateUIButton("CloseButton", headerPanel.transform, "X");
        RectTransform closeButtonRect = closeButton.GetComponent<RectTransform>();
        closeButtonRect.anchorMin = new Vector2(1, 0.5f);
        closeButtonRect.anchorMax = new Vector2(1, 0.5f);
        closeButtonRect.pivot = new Vector2(1, 0.5f);
        closeButtonRect.anchoredPosition = new Vector2(-10, 0);
        closeButtonRect.sizeDelta = new Vector2(40, 40);
        closeButton.onClick.AddListener(() => dashboardPanel.SetActive(false));
        
        // Create content panel with scroll view
        GameObject contentPanel = CreateScrollView("ContentPanel", dashboardPanel.transform);
        RectTransform contentRect = contentPanel.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 0);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.offsetMin = new Vector2(0, 0);
        contentRect.offsetMax = new Vector2(0, -60);
        
        // Get scroll rect content
        ScrollRect scrollRect = contentPanel.GetComponent<ScrollRect>();
        Transform scrollContent = scrollRect.content;
        
        // Create sections
        CreateStatsSection(scrollContent);
        CreateGameSaveLoadSection(scrollContent);
        CreateDLCStatsSection(scrollContent);
        CreateGameResultsSection(scrollContent);
        
        // Assign components and scripts
        dashboardPanel.AddComponent<AnalyticsDashboardUI>();
        
        // Set references in the AnalyticsDashboardUI script
        AnalyticsDashboardUI dashboardUI = dashboardPanel.GetComponent<AnalyticsDashboardUI>();
        dashboardUI.dashboardPanel = dashboardPanel;
        dashboardUI.closeButton = closeButton;
        dashboardUI.headerText = headerText;
        
        // Initially hide the dashboard
        dashboardPanel.SetActive(false);
        
        return dashboardPanel;
    }
    
    private void CreateStatsSection(Transform parent)
    {
        GameObject section = CreateSection("StatsSection", parent, "Game Statistics");
        
        // Create stats items
        Text totalGamesText = CreateUIText("TotalGamesText", section.transform, "Total Games: 0");
        Text totalMovesText = CreateUIText("TotalMovesText", section.transform, "Total Moves: 0");
        Text avgMovesText = CreateUIText("AvgMovesText", section.transform, "Avg. Moves per Game: 0");
        
        // Position the stats
        RectTransform rect = totalGamesText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(20, -50);
        rect.offsetMax = new Vector2(-20, -20);
        
        rect = totalMovesText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(20, -80);
        rect.offsetMax = new Vector2(-20, -50);
        
        rect = avgMovesText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(20, -110);
        rect.offsetMax = new Vector2(-20, -80);
    }
    
    private void CreateGameSaveLoadSection(Transform parent)
    {
        GameObject section = CreateSection("GameStateSection", parent, "Game State Management");
        
        // Create input field for game ID
        InputField gameIdInput = CreateInputField("GameIdInput", section.transform, "Enter Game ID");
        RectTransform inputRect = gameIdInput.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0, 1);
        inputRect.anchorMax = new Vector2(1, 1);
        inputRect.pivot = new Vector2(0, 1);
        inputRect.offsetMin = new Vector2(20, -80);
        inputRect.offsetMax = new Vector2(-20, -20);
        
        // Create buttons
        Button saveButton = CreateUIButton("SaveButton", section.transform, "Save Game");
        RectTransform saveRect = saveButton.GetComponent<RectTransform>();
        saveRect.anchorMin = new Vector2(0, 1);
        saveRect.anchorMax = new Vector2(0.48f, 1);
        saveRect.pivot = new Vector2(0, 1);
        saveRect.offsetMin = new Vector2(20, -130);
        saveRect.offsetMax = new Vector2(-5, -90);
        
        Button loadButton = CreateUIButton("LoadButton", section.transform, "Load Game");
        RectTransform loadRect = loadButton.GetComponent<RectTransform>();
        loadRect.anchorMin = new Vector2(0.52f, 1);
        loadRect.anchorMax = new Vector2(1, 1);
        loadRect.pivot = new Vector2(0, 1);
        loadRect.offsetMin = new Vector2(5, -130);
        loadRect.offsetMax = new Vector2(-20, -90);
        
        // Status text
        Text statusText = CreateUIText("StatusText", section.transform, "No game state operations performed");
        RectTransform statusRect = statusText.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0, 1);
        statusRect.anchorMax = new Vector2(1, 1);
        statusRect.pivot = new Vector2(0, 1);
        statusRect.offsetMin = new Vector2(20, -180);
        statusRect.offsetMax = new Vector2(-20, -140);
    }
    
    private void CreateDLCStatsSection(Transform parent)
    {
        GameObject section = CreateSection("DLCStatsSection", parent, "DLC Purchase Statistics");
        
        // Create stats display
        Text popularDLCText = CreateUIText("PopularDLCText", section.transform, 
            "Popular DLC Items:\n" +
            "No data available");
        RectTransform rect = popularDLCText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(20, -120);
        rect.offsetMax = new Vector2(-20, -20);
    }
    
    private void CreateGameResultsSection(Transform parent)
    {
        GameObject section = CreateSection("GameResultsSection", parent, "Game Results");
        
        // Create stats display
        Text resultsText = CreateUIText("ResultsText", section.transform, 
            "Game Results:\n" +
            "No data available");
        RectTransform rect = resultsText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(20, -120);
        rect.offsetMax = new Vector2(-20, -20);
    }
    
    private GameObject CreateSection(string name, Transform parent, string title)
    {
        // Create section container
        GameObject section = CreateUIPanel(name, parent);
        RectTransform sectionRect = section.GetComponent<RectTransform>();
        
        // Calculate position based on existing sections
        float yOffset = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i) != section.transform)
            {
                RectTransform siblingRect = parent.GetChild(i).GetComponent<RectTransform>();
                yOffset += siblingRect.sizeDelta.y + 20; // Add spacing
            }
        }
        
        // Set size and position
        sectionRect.pivot = new Vector2(0.5f, 1);
        sectionRect.anchorMin = new Vector2(0, 1);
        sectionRect.anchorMax = new Vector2(1, 1);
        sectionRect.sizeDelta = new Vector2(0, 200); // Default height
        sectionRect.anchoredPosition = new Vector2(0, -yOffset);
        
        // Create section header
        Text headerText = CreateUIText(name + "Header", section.transform, title);
        RectTransform headerRect = headerText.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.offsetMin = new Vector2(10, -30);
        headerRect.offsetMax = new Vector2(-10, 0);
        headerText.fontSize = 18;
        headerText.fontStyle = FontStyle.Bold;
        
        // Add separator line
        GameObject separator = new GameObject("Separator");
        separator.transform.SetParent(section.transform, false);
        separator.AddComponent<RectTransform>();
        separator.AddComponent<Image>();
        RectTransform separatorRect = separator.GetComponent<RectTransform>();
        separatorRect.anchorMin = new Vector2(0, 1);
        separatorRect.anchorMax = new Vector2(1, 1);
        separatorRect.pivot = new Vector2(0.5f, 1);
        separatorRect.offsetMin = new Vector2(10, -32);
        separatorRect.offsetMax = new Vector2(-10, -30);
        separator.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        
        return section;
    }
    
    private GameObject CreateUIPanel(string name, Transform parent)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        
        // Add components
        panel.AddComponent<RectTransform>();
        Image image = panel.AddComponent<Image>();
        image.color = backgroundColor;
        
        return panel;
    }
    
    private Text CreateUIText(string name, Transform parent, string content)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        
        // Add components
        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        Text text = textObject.AddComponent<Text>();
        
        // Configure text
        text.text = content;
        text.color = textColor;
        text.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 16;
        
        return text;
    }
    
    private Button CreateUIButton(string name, Transform parent, string label)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        
        // Add components
        RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
        Image image = buttonObject.AddComponent<Image>();
        Button button = buttonObject.AddComponent<Button>();
        
        // Configure button
        image.color = buttonColor;
        button.targetGraphic = image;
        
        // Add text
        Text text = CreateUIText(name + "Text", buttonObject.transform, label);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.alignment = TextAnchor.MiddleCenter;
        
        return button;
    }
    
    private InputField CreateInputField(string name, Transform parent, string placeholder)
    {
        GameObject inputObject = new GameObject(name);
        inputObject.transform.SetParent(parent, false);
        
        // Add components
        RectTransform rectTransform = inputObject.AddComponent<RectTransform>();
        Image image = inputObject.AddComponent<Image>();
        InputField inputField = inputObject.AddComponent<InputField>();
        
        // Create text components
        GameObject textArea = new GameObject("Text");
        textArea.transform.SetParent(inputObject.transform, false);
        Text textComponent = textArea.AddComponent<Text>();
        textComponent.supportRichText = false;
        textComponent.color = Color.black;
        textComponent.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        textComponent.fontSize = 16;
        RectTransform textRect = textArea.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 6);
        textRect.offsetMax = new Vector2(-10, -6);
        
        GameObject placeholderArea = new GameObject("Placeholder");
        placeholderArea.transform.SetParent(inputObject.transform, false);
        Text placeholderComponent = placeholderArea.AddComponent<Text>();
        placeholderComponent.supportRichText = false;
        placeholderComponent.fontStyle = FontStyle.Italic;
        placeholderComponent.color = new Color(0.5f, 0.5f, 0.5f);
        placeholderComponent.text = placeholder;
        placeholderComponent.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
        placeholderComponent.fontSize = 16;
        RectTransform placeholderRect = placeholderArea.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10, 6);
        placeholderRect.offsetMax = new Vector2(-10, -6);
        
        // Configure input field
        inputField.textComponent = textComponent;
        inputField.placeholder = placeholderComponent;
        image.color = Color.white;
        
        return inputField;
    }
    
    private GameObject CreateScrollView(string name, Transform parent)
    {
        GameObject scrollView = new GameObject(name);
        scrollView.transform.SetParent(parent, false);
        
        // Add rect transform
        RectTransform scrollRect = scrollView.AddComponent<RectTransform>();
        
        // Add scroll rect component
        ScrollRect scrollRectComponent = scrollView.AddComponent<ScrollRect>();
        
        // Create viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollView.transform, false);
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1, 1, 1, 0.01f); // Almost transparent
        
        Mask viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;
        
        // Create content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 1000); // Default height
        
        // Set up scroll rect
        scrollRectComponent.viewport = viewportRect;
        scrollRectComponent.content = contentRect;
        scrollRectComponent.horizontal = false;
        scrollRectComponent.vertical = true;
        scrollRectComponent.scrollSensitivity = 30;
        scrollRectComponent.movementType = ScrollRect.MovementType.Elastic;
        scrollRectComponent.elasticity = 0.1f;
        scrollRectComponent.inertia = true;
        scrollRectComponent.decelerationRate = 0.135f;
        
        return scrollView;
    }
}