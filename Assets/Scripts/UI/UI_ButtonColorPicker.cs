using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_ButtonColorPicker : MonoBehaviour
{
    public UnityEvent<Color> ColorPickerEvent; // Assigned in scene to decide what color buttons affect which objects
    [SerializeField] Texture2D colorChart; // the color map to display 
    [SerializeField] GameObject chart; // reference to the rect transform object

    [SerializeField] RectTransform cursor;
    [SerializeField] Image button;
    [SerializeField] Image cursorColor;

    /// <summary>
    /// Selects the color at the current mouse position and translates it to the current anchor position of the virtual cursor for a corresponding Color result
    /// </summary>
    public void PickColor(BaseEventData data)
    {
        PointerEventData pointer = data as PointerEventData; // Unsure why can't use this directly, but trying breaks everything
        cursor.position = pointer.position; // places a fake cursor where they clicked to see clearly the current selection
        Color pickedColor = colorChart.GetPixel( // gets the pixel's color at the position of selection
            (int)(cursor.anchoredPosition.x * (colorChart.width / chart.GetComponent<RectTransform>().rect.width)), // use anchored, set bottom left anchor. 0,0 is bottom left of a texture
            (int)(cursor.anchoredPosition.y * (colorChart.height / chart.GetComponent<RectTransform>().rect.height)));
        button.color = pickedColor; // set the top level UI to display the color
        cursorColor.color = pickedColor; // set the visual cursor to display the color
        ColorPickerEvent.Invoke(pickedColor); // inform any functions set to listen to react
    }
}
