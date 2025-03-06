using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeselectSelectableOnClick : MonoBehaviour
{
    private float _timeMouseDown;

    private void OnMouseDown()
    {
        _timeMouseDown = Time.time;
    }

    private void OnMouseUpAsButton()
    {
        if (InputHandler.IsPointerOverUIElement()) return;

        if (Time.time - _timeMouseDown < 0.2f)
            Selectable.DeselectAll();
    }
}
