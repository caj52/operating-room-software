using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Button))]
public class UI_ButtonTextController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Text Color Settings")]
    public Color normalColor = Color.white;
    public Color hoverColor = Color.yellow;
    public Color selectedColor = Color.green;

    [Header("Text Size Settings")]
    public float normalTextSize = 14f;
    public float hoverTextSize = 16f;

    private TextMeshProUGUI buttonText;
    private bool isSelected = false;

    // Static reference to track the currently selected button
    private static UI_ButtonTextController currentlySelectedButton;

    private void Awake()
    {
        buttonText = GetComponentInChildren<TextMeshProUGUI>();

        if (buttonText == null)
        {
            Debug.LogError("No TextMeshProUGUI component found on the button.");
            enabled = false;
            return;
        }

        // Set initial text properties
        buttonText.color = normalColor;
        buttonText.fontSize = Mathf.RoundToInt(normalTextSize);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isSelected)
        {
            buttonText.color = hoverColor;
            buttonText.fontSize = Mathf.RoundToInt(hoverTextSize);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isSelected)
        {
            buttonText.color = normalColor;
            buttonText.fontSize = Mathf.RoundToInt(normalTextSize);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Deselect the previously selected button
        if (currentlySelectedButton != null && currentlySelectedButton != this)
        {
            currentlySelectedButton.Deselect();
        }

        // Select the new button
        isSelected = true;
        buttonText.color = selectedColor;
        buttonText.fontSize = Mathf.RoundToInt(hoverTextSize);

        // Update the static reference
        currentlySelectedButton = this;
    }

    private void Deselect()
    {
        isSelected = false;
        buttonText.color = normalColor;
        buttonText.fontSize = Mathf.RoundToInt(normalTextSize);
    }

    private void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
