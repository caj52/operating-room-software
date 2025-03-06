using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UI_ExtraWallDimensions : MonoBehaviour
{
    [field: SerializeField] private TextMeshProUGUI TextHeight { get; set; }
    [field: SerializeField] private TextMeshProUGUI TextWidth { get; set; }
    [field: SerializeField] private TextMeshProUGUI TextDepth { get; set; }

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
            TextHeight.text = $"H: {GetDimensionFeetInches(scale.z)}";
            TextWidth.text = $"W: {GetDimensionFeetInches(scale.x)}";
            TextDepth.text = $"D: {GetDimensionFeetInches(scale.y)}";
        }
    }
}
