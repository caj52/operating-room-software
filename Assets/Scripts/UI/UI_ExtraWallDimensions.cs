using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UI_ExtraWallDimensions : MonoBehaviour
{
    [field: SerializeField] private TextMeshProUGUI TextHeight { get; set; }
    [field: SerializeField] private TextMeshProUGUI TextWidth { get; set; }
    [field: SerializeField] private TextMeshProUGUI TextDepth { get; set; }

    private string _lastHeightText = string.Empty;
    private string _lastWidthText = string.Empty;
    private string _lastDepthText = string.Empty;

    private void Awake()
    {
        ExtraWall.ExtraWallSelectionChanged.AddListener(() =>
        {
            gameObject.SetActive(ExtraWall.SelectedExtraWall != null);
        });

        gameObject.SetActive(false);
    }

    private string GetDimensionFeetInches(float dimensionInMeters)
    {
        float heightFeet = dimensionInMeters.ToFeet();
        float heightFeetRounded = Mathf.Floor(heightFeet);
        float heightInches = (heightFeet - heightFeetRounded) * 12f;
        return $"{heightFeetRounded}' {heightInches:0.0}\"";
    }

    private void Update()
    {
        if (ExtraWall.SelectedExtraWall != null)
        {
            var scale = ExtraWall.SelectedExtraWall.transform.localScale;
            string heightText = $"H: {GetDimensionFeetInches(scale.z)}";
            string widthText = $"W: {GetDimensionFeetInches(scale.x)}";
            string depthText = $"D: {GetDimensionFeetInches(scale.y)}";

            if (heightText != _lastHeightText)
            {
                _lastHeightText = heightText;
                TextHeight.text = heightText;
            }
            if (widthText != _lastWidthText)
            {
                _lastWidthText = widthText;
                TextWidth.text = widthText;
            }
            if (depthText != _lastDepthText)
            {
                _lastDepthText = depthText;
                TextDepth.text = depthText;
            }
        }
    }
}
