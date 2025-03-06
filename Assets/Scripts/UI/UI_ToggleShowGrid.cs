using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RTG;
using System.Linq;

public class UI_ToggleShowGrid : MonoBehaviour
{
    [field: SerializeField] private GameObject HeightSlider { get; set; }

    public void ToggleShowGrid(bool isOn)
    {
        var color = RTSceneGrid.Get.LookAndFeel.LineColor;
        color.a = isOn ? 1 : 0;
        RTSceneGrid.Get.LookAndFeel.LineColor = color;
        HeightSlider.SetActive(isOn);
        //RTSceneGrid.Get.LookAndFeel.
    }

    public void SetGridHeight(float percentage)
    {
        float wallHeight = RoomBoundary.Instances.Where(item => item.RoomBoundaryType == RoomBoundaryType.WallWest).ToList()[0].transform.localScale.y;
        RTSceneGrid.Get.Settings.YOffset = Mathf.Clamp(wallHeight * percentage, 0.01f, wallHeight - 0.01f);
    }
}