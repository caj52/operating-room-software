using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static Selectable;

public class ArticualtionToolScript : MonoBehaviour
{
    public static ArticualtionToolScript instance;
    public GameObject SelectedObject;
    public Transform ARPanel;
    public TextMeshProUGUI headingText;
    public Slider xSlider, ySlider, zSlider;
    public GizmoHandler gizmoHandler;
    public static UnityAction gizmoChanged;
    public List<Selectable> selectables;
    public DuplicateRoom rooms;
    private void Awake()
    {
        instance = this;
    }
    private void Start()
    {
        rooms = FindObjectOfType<DuplicateRoom>();
        Selectable.SelectionChanged += TargetObjectChanged;
        gizmoChanged += GizmoChanged;
    }

    private void OnEnable()
    {
        headingText.text = GizmoSelector.CurrentGizmoMode.ToString();
        xSlider.onValueChanged.AddListener(OnXSliderValueChanged);
        ySlider.onValueChanged.AddListener(OnYSliderValueChanged);
        zSlider.onValueChanged.AddListener(OnZSliderValueChanged);

    }



    public void Update()
    {
        UpdateSlidersFromTransform();
    }

    private void UpdateSlidersFromTransform()
    {
     /*   if (GizmoSelector.CurrentGizmoMode == GizmoMode.Translate)
        {
            xSlider.value = SelectedObject.transform.position.x;
            ySlider.value = SelectedObject.transform.position.z;
            zSlider.value = SelectedObject.transform.position.y;
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Rotate)
        {
            xSlider.value = SelectedObject.transform.rotation.x;
            ySlider.value = SelectedObject.transform.rotation.y;
            zSlider.value = SelectedObject.transform.rotation.z;
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
        {
            xSlider.value = SelectedObject.transform.localScale.x;
            ySlider.value = SelectedObject.transform.localScale.y;
            zSlider.value = SelectedObject.transform.localScale.z;
        }*/
    }

    private void GizmoChanged()
    {
        headingText.text = GizmoSelector.CurrentGizmoMode.ToString();
        SetSlidersForMultipleObjects();
    }

    private void TargetObjectChanged()
    {
        selectables = Selectable.SelectedSelectables;

        if (selectables.Count > 0)
        {
            // Set the primary selected object (for compatibility with existing code)
            SelectedObject = selectables[0].gameObject;
            gizmoHandler = SelectedObject.GetComponent<GizmoHandler>();

            // Set sliders based on all selected objects
            SetSlidersForMultipleObjects();
        }
        else
        {
            // No objects selected, hide all sliders
            DisableAllSliders();
        }
    }

    void SetSlidersForMultipleObjects()
    {
        // Default behavior - all axes disabled
        bool useX = false;
        bool useY = false;
        bool useZ = false;
        bool anyValidObjectFound = false;

        if (selectables.Count > 0)
        {
            if (GizmoSelector.CurrentGizmoMode == GizmoMode.Rotate)
            {
                // Check all selected objects and enable an axis if ANY object can use it
                foreach (var selectable in selectables)
                {
                    GizmoHandler handler = selectable.GetComponent<GizmoHandler>();
                    if (handler != null)
                    {
                        // Check if this object can use any rotation
                        if (handler.CanUseAnyRotation)
                        {
                            anyValidObjectFound = true;
                            // Enable rotation slider if any object allows rotation on that axis
                            if (handler.CanUseRotationX) useX = true;
                            if (handler.CanUseRotationY) useY = true;
                            if (handler.CanUseRotationZ) useZ = true;
                        }
                    }
                }

                // Set sliders' ranges for each axis if we need to show them
                if (anyValidObjectFound)
                {
                    if (useX) SetSliderRangesForAllObjectsForRotation(Axis.X, xSlider);
                    if (useY) SetSliderRangesForAllObjectsForRotation(Axis.Y, ySlider);
                    if (useZ) SetSliderRangesForAllObjectsForRotation(Axis.Z, zSlider);
                }
            }
            else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Translate)
            {
                foreach (var selectable in selectables)
                {
                    GizmoHandler handler = selectable.GetComponent<GizmoHandler>();
                    if (handler != null && handler.CanUseAnyTranslation)
                    {
                        anyValidObjectFound = true;
                        if (handler.CanUseTranslateX) useX = true;
                        if (handler.CanUseTranslateY) useY = true;
                        if (handler.CanUseTranslateZ) useZ = true;
                    }
                }
                if (anyValidObjectFound)
                {
                    if (useX) SetSliderRangesForAllObjectsForTranslation(Axis.X, xSlider);
                    if (useY) SetSliderRangesForAllObjectsForTranslation(Axis.Y, ySlider);
                    if (useZ) SetSliderRangesForAllObjectsForTranslation(Axis.Z, zSlider);
                }
            }
            else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
            {
                foreach (var selectable in selectables)
                {
                    GizmoHandler handler = selectable.GetComponent<GizmoHandler>();
                    if (handler != null && handler.CanUseAnyScale)
                    {
                        anyValidObjectFound = true;
                        if (handler.CanUseScaleX) useX = true;
                        if (handler.CanUseScaleY) useY = true;
                        if (handler.CanUseScaleZ) useZ = true;
                    }
                }

                if (anyValidObjectFound)
                {
                    if (useX) SetSliderRangesForAllObjectsForScale(Axis.X, xSlider);
                    if (useY) SetSliderRangesForAllObjectsForScale(Axis.Y, ySlider);
                    if (useZ) SetSliderRangesForAllObjectsForScale(Axis.Z, zSlider);
                }
            }
        }

        // Update UI to show active sliders
        if (anyValidObjectFound)
        {
            SetAxisSliders(useX, useY, useZ);
        }
        else
        {
            // If no valid objects are found, hide all sliders
            DisableAllSliders();
        }
    }

    void SetSliderRangesForAllObjectsForRotation(Axis axis, Slider slider)
    {
        if (slider == null)
        {
            Debug.LogError("SetSliderRangesForAllObjects: Slider is null!");
            return;
        }

        float minValue = 0f;
        float maxValue = 360f; // Default rotation range

        bool foundSetting = false;

        foreach (var selectable in selectables)
        {
            if (selectable.GizmoSettingsList == null || selectable.GizmoSettingsList.Count == 0)
            {
                continue;
            }

            GizmoHandler handler = selectable.GetComponent<GizmoHandler>();
            bool canUseAxis = false;

            // Only consider objects that can use this axis
            switch (axis)
            {
                case Axis.X: canUseAxis = handler != null && handler.CanUseRotationX; break;
                case Axis.Y: canUseAxis = handler != null && handler.CanUseRotationY; break;
                case Axis.Z: canUseAxis = handler != null && handler.CanUseRotationZ; break;
            }

            if (!canUseAxis) continue;

            foreach (var settings in selectable.GizmoSettingsList)
            {
                if (settings.Axis == axis && settings.GizmoType == GizmoType.Rotate)
                {
                    // Only update if this is the first valid setting OR has a wider range
                    if (!foundSetting ||
                        settings.MinValue < minValue ||
                        settings.MaxValue > maxValue)
                    {
                        // If first setting found, set values directly
                        if (!foundSetting)
                        {
                            minValue = settings.MinValue;
                            maxValue = settings.MaxValue;
                        }
                        else
                        {
                            // Otherwise, expand range if needed
                            minValue = Mathf.Min(minValue, settings.MinValue);
                            maxValue = Mathf.Max(maxValue, settings.MaxValue);
                        }
                        foundSetting = true;
                    }
                }
            }
        }

        // Apply min/max to slider
        if (minValue == 0 && maxValue == 0)
        {
            minValue = 0;
            maxValue = 360;
        }

        slider.minValue = minValue;
        slider.maxValue = maxValue;

        // Reset the slider value to the current rotation of the primary selected object
        if (SelectedObject != null)
        {
            Vector3 rotation = SelectedObject.transform.localEulerAngles;
            switch (axis)
            {
                case Axis.X: slider.value = rotation.x; break;
                case Axis.Y: slider.value = rotation.y; break;
                case Axis.Z: slider.value = rotation.z; break;
            }
        }
    }

    void SetSliderRangesForAllObjectsForTranslation(Axis axis, Slider slider)
    {
        if (slider == null)
        {
            Debug.LogError("SetSliderRangesForAllObjects: Slider is null!");
            return;
        }

        float minValue = 0f;
        float maxValue = 360f; // Default rotation range

        bool foundSetting = false;

        foreach (var selectable in selectables)
        {
            if (selectable.GizmoSettingsList == null || selectable.GizmoSettingsList.Count == 0)
            {
                continue;
            }

            GizmoHandler handler = selectable.GetComponent<GizmoHandler>();
            bool canUseAxis = false;

            // Only consider objects that can use this axis
            switch (axis)
            {
                case Axis.X: canUseAxis = handler != null && handler.CanUseTranslateX; break;
                case Axis.Y: canUseAxis = handler != null && handler.CanUseTranslateY; break;
                case Axis.Z: canUseAxis = handler != null && handler.CanUseTranslateZ; break;
            }

            if (!canUseAxis) continue;

            foreach (var settings in selectable.GizmoSettingsList)
            {
                if (settings.Axis == axis && settings.GizmoType == GizmoType.Move)
                {
                    RoomBoundary[] roomBoundaries = rooms.currentRoom.GetComponentsInChildren<RoomBoundary>();

                    var wallSouth = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallSouth);
                    var wallNorth = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallNorth);
                    var wallEast = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallEast);
                    var wallWest = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.WallWest);
                    var ceiling = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.Ceiling);
                    var floor = roomBoundaries.FirstOrDefault(rb => rb.RoomBoundaryType == RoomBoundaryType.Floor);

                    if (axis == Axis.X)
                    {
                        minValue = Mathf.Min(wallWest.transform.position.x, wallEast.transform.position.x);
                        maxValue = Mathf.Max(wallWest.transform.position.x, wallEast.transform.position.x);
                    }
                    else if (axis == Axis.Y)
                    {
                        minValue = Mathf.Min(wallSouth.transform.position.z, wallNorth.transform.position.z);
                        maxValue = Mathf.Max(wallSouth.transform.position.z, wallNorth.transform.position.z);
                    }
                    else if (axis == Axis.Z)
                    {
                        minValue = Mathf.Min(floor.transform.position.y, ceiling.transform.position.y);
                        maxValue = Mathf.Max(floor.transform.position.y, ceiling.transform.position.y);

                    }

                    foundSetting = true;
                }
            }
        
        
    }

        // Apply min/max to slider
        if (minValue == 0 && maxValue == 0)
        {
            minValue = 0;
            maxValue = 360;
        }

        slider.minValue = minValue;
        slider.maxValue = maxValue;

    }

    void SetSliderRangesForAllObjectsForScale(Axis axis, Slider slider)
    {
        if (slider == null)
        {
            Debug.LogError("SetSliderRangesForAllObjects: Slider is null!");
            return;
        }

        float minValue = float.MaxValue;
        float maxValue = float.MinValue;
        Selectable firstValidSelectable = null;
        bool foundSetting = false;

        foreach (var selectable in selectables)
        {
            if (selectable?.ScaleLevels == null || selectable.ScaleLevels.Count == 0)
                continue;

            var handler = selectable.GetComponent<GizmoHandler>();
            bool canUseAxis = handler != null && axis switch
            {
                Axis.X => handler.CanUseScaleX,
                Axis.Y => handler.CanUseScaleY,
                Axis.Z => handler.CanUseScaleZ,
                _ => false
            };

            if (!canUseAxis)
                continue;

            if (selectable.GizmoSettingsList.Any(settings => settings.Axis == axis && settings.GizmoType == GizmoType.Scale))
            {
                float localMin = selectable.ScaleLevels.Min(s => s.Size);
                float localMax = selectable.ScaleLevels.Max(s => s.Size);

                minValue = Mathf.Min(minValue, localMin);
                maxValue = Mathf.Max(maxValue, localMax);
                firstValidSelectable = selectable;
                foundSetting = true;
            }
        }

        if (foundSetting)
        {
            slider.minValue = minValue;
            slider.maxValue = maxValue;
        }
        else
        {
            slider.minValue = 0f;
            slider.maxValue = 1f; // fallback defaults
            Debug.LogWarning("No valid scale settings found for selected objects.");
        }

        if (firstValidSelectable?.ScaleLevels != null)
        {
            if (SelectedObject != null)
            {
                Vector3 scale = SelectedObject.transform.localScale;
                slider.value = axis switch
                {
                    Axis.X => scale.x,
                    Axis.Y => scale.y,
                    Axis.Z => firstValidSelectable.CurrentScaleLevel.Size,
                    _ => minValue,
                };
            }
        }
    }

    void DisableAllSliders()
    {
        xSlider.transform.parent.gameObject.SetActive(false);
        ySlider.transform.parent.gameObject.SetActive(false);
        zSlider.transform.parent.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    void SetAxisSliders(bool useX, bool useY, bool useZ)
    {
        xSlider.transform.parent.gameObject.SetActive(useX);
        ySlider.transform.parent.gameObject.SetActive(useY);
        zSlider.transform.parent.gameObject.SetActive(useZ);

        // Remove the fallback behavior that shows all sliders if none are enabled
        // This was causing the issue where objects with all rotations disabled showed all sliders
    }

    private void OnZSliderValueChanged(float value)
    {
        if (GizmoSelector.CurrentGizmoMode == GizmoMode.Translate)
        {
            UpdateTransformPositiom(value,Axis.Y);
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Rotate)
        {
            UpdateTransformRotation(value, Axis.Z);
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
        {
            UpdateTransformScale(value, Axis.Z);

        }


    }

    private void OnYSliderValueChanged(float value)
    {
        if (GizmoSelector.CurrentGizmoMode == GizmoMode.Translate)
        {
            UpdateTransformPositiom(value, Axis.Z);
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Rotate)
        {
            UpdateTransformRotation(value, Axis.Y);
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
        {

        }
    }

    private void OnXSliderValueChanged(float value)
    {
        if (GizmoSelector.CurrentGizmoMode == GizmoMode.Translate)
        {
            UpdateTransformPositiom(value, Axis.X);
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Rotate)
        {
            UpdateTransformRotation(value, Axis.X);
        }
        else if (GizmoSelector.CurrentGizmoMode == GizmoMode.Scale)
        {
        }
    }

    private void UpdateTransformPositiom(float value, Axis axis)
    {
        if (selectables.Count == 0)
        {
            Debug.LogWarning("UpdateTransformRotation: No objects selected");
            return;
        }

        foreach (var selectable in selectables)
        {
            GameObject obj = selectable.gameObject;
            GizmoHandler handler = obj.GetComponent<GizmoHandler>();
            if (obj.TryGetComponent(out CCDIK ik))
            {
                ik.enabled = true;
            }
            // Get current rotation as Quaternion

            if (ik != null)
            {
                var target = ik.Target;
                if (target != null)
                {
                    Vector3 targetPos = target.localPosition;

                    switch (axis)
                    {
                        case Axis.X: targetPos.x = value; break;
                        case Axis.Y: targetPos.y = value; break;
                        case Axis.Z: targetPos.z = value; break;
                    }

                    target.localPosition = targetPos;
                    return;
                }
            }

            Vector3 currentPos = obj.transform.localPosition;

            // Update only the relevant axis
            switch (axis)
            {
                case Axis.X: currentPos.x = value; break;
                case Axis.Y: currentPos.y = value; break;
                case Axis.Z: currentPos.z = value; break;
            }

     
            // Apply updated rotation as Quaternion
            obj.transform.localPosition = currentPos;
          
        }

    }


    private void UpdateTransformRotation(float value, Axis axis)
    {

        if (selectables.Count == 0)
        {
            Debug.LogWarning("UpdateTransformRotation: No objects selected");
            return;
        }

        foreach (var selectable in selectables)
        {
            GameObject obj = selectable.gameObject;
            GizmoHandler handler = obj.GetComponent<GizmoHandler>();

            bool canRotate = false;
            switch (axis)
            {
                case Axis.X: canRotate = handler != null && handler.CanUseRotationX; break;
                case Axis.Y: canRotate = handler != null && handler.CanUseRotationY; break;
                case Axis.Z: canRotate = handler != null && handler.CanUseRotationZ; break;
            }

            if (!canRotate) continue;

            // Get current rotation as Quaternion
            Quaternion currentRotation = obj.transform.localRotation;
            Vector3 currentEuler = currentRotation.eulerAngles;

            // Update only the relevant axis
            switch (axis)
            {
                case Axis.X: currentEuler.x = value; break;
                case Axis.Y: currentEuler.y = value; break;
                case Axis.Z: currentEuler.z = value; break;
            }

            // Apply updated rotation as Quaternion
            obj.transform.localRotation = Quaternion.Euler(currentEuler);
        }
    }


    private void UpdateTransformScale(float value, Axis axis)
    {
        if (selectables.Count == 0)
        {
            Debug.LogWarning("UpdateTransformRotation: No objects selected");
            return;
        }

        Debug.LogError("UpdateTransformScale"+value);
        foreach (var selectable in selectables)
        {
            GameObject obj = selectable.gameObject;
            GizmoHandler handler = obj.GetComponent<GizmoHandler>();

            bool canScale = false;
            switch (axis)
            {
                case Axis.X: canScale = handler != null && handler.CanUseScaleX; break;
                case Axis.Y: canScale = handler != null && handler.CanUseScaleY; break;
                case Axis.Z: canScale = handler != null && handler.CanUseScaleZ; break;
            }

            if (!canScale) continue;

            // Get current rotation as Quaternion
            if (selectable.ScaleLevels.Count == 0) return;
            //get closest scale in list
          
            ScaleLevel closest = selectable.ScaleLevels.OrderBy(item => Mathf.Abs(item.Size - value)).First();
            Debug.LogError(closest.Size);
            selectable.SetScaleLevel(closest, false);
           
        }
    }
}