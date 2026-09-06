using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class UI_ScalableLength : MonoBehaviour
{
    [field: SerializeField] 
    private TextMeshProUGUI TextLength { get; set; }

    private bool _isActive;

    private void Awake()
    {
        Selectable.SelectionChanged += SelectableChanged;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= SelectableChanged;
    }

    private void SelectableChanged()
    {
        _isActive = Selectable.SelectedSelectables.Any(x =>
            x != null && x.ScaleLevels != null && x.ScaleLevels.Count > 0);
        gameObject.SetActive(_isActive);
    }

    private void Update()
    {
        if (!_isActive)
            return;

        var selectable = Selectable.SelectedSelectables.FirstOrDefault(x =>
            x != null && x.ScaleLevels != null && x.ScaleLevels.Count > 0);
        if (selectable == null)
        {
            _isActive = false;
            gameObject.SetActive(false);
            return;
        }

        float scale = selectable.CurrentPreviewScaleLevel?.Size
                      ?? selectable.CurrentScaleLevel?.Size
                      ?? 0f;
        if (TextLength != null)
            TextLength.text = $"{scale * 1000f} mm";
    }
}
