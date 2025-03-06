using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_ButtonMaterialPallete : MonoBehaviour
{
    Button _button;

    /// <summary>
    /// internal reference to the material palette object
    /// </summary>
    MaterialPalette _palette;

    public UI_MaterialPallete ui_pallete;
    public TMP_Text label;
    private int _elementIncrement;

    private void Awake()
    {
        _button = GetComponent<Button>();
        Selectable.SelectionChanged += UpdateSelectedPallete;
        _button.onClick.AddListener(DisplayPallete);
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateSelectedPallete;
        _button.onClick.RemoveListener(DisplayPallete);
    }

    private void UpdateSelectedPallete()
    {
        bool objectActive;
        _palette = null;
        ui_pallete.ClearPalleteOptions();

        if (Selectable.SelectedSelectables.Count > 0)
        {
            objectActive = Selectable.SelectedSelectables[0]
                .gameObject.TryGetComponent(out MaterialPalette m);

            _palette = m;
            _elementIncrement = 0;
            label.text = "Show Material Palette";
        }
        else 
        {
            objectActive = false;
        } 

        gameObject.SetActive(objectActive);
    }

    private void DisplayPallete()
    {
        _elementIncrement++;
        if (_elementIncrement > _palette.elements.Count())
        {
            ui_pallete.ClearPalleteOptions();
            label.text = "Show Material Palette";
            _elementIncrement = 0;
            return;
        }
        else if(_elementIncrement < _palette.elements.Count())
        {
            label.text = "Next Material Region";
        }
        else
        {
            label.text = "Hide Material Palette";
        }

        ui_pallete.LoadPalleteOptions(_palette.elements[_elementIncrement - 1].materials, _elementIncrement - 1);
    }
}
