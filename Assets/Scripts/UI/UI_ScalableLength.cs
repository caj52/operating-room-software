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
    private string _lastLengthText = string.Empty;

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
        _isActive = Selectable.SelectedSelectables.Count > 0 &&
            Selectable.SelectedSelectables.Sum(x => x.ScaleLevels.Count) > 0;

        gameObject.SetActive(_isActive);
    }

    private void Update()
    {
        if (_isActive)
        {
            var selectable = Selectable.SelectedSelectables.First(x => x.ScaleLevels.Count > 0);
            var scale = selectable.CurrentPreviewScaleLevel.Size;
            string text = $"{scale * 1000f} mm";
            if (text != _lastLengthText)
            {
                _lastLengthText = text;
                TextLength.text = text;
            }
        }
    }
}
