using RTG;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections.Generic;
using Unity.VisualScripting;

public class GetHierarchyObjects : MonoBehaviour
{
    public static GetHierarchyObjects instance;
    public Transform SelectedRootObject;
    public Transform targetObjectMainParent;
    public Transform targetObject;
    public List<Slider> _sliderList = new List<Slider>();
    public List<Transform> allChildren = new List<Transform>();
    public bool ObjectHaveNotAttechedPoint = false;
    [Header("UI Panel")]
    public GameObject TransformPanel;

    public Slider positionXSlider, positionYSlider, positionZSlider;
    public Slider rotationXSlider, rotationYSlider, rotationZSlider;
    public Slider scaleXSlider, scaleYSlider, scaleZSlider;

    [Header("Object Ref")]
    public Transform xMove;
    public Transform yMove;
    public Transform zMove;

    public Transform xRotate;
    public Transform yRotate;
    public Transform zRotate;

    public Transform xScale;
    public Transform yScale;
    public Transform zScale;

    [SerializeField]
    private bool listenersAdded = false;

    private float _minValue, _maxValue;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        toggleSliderPanelAndClearList(false);
        AddSliderIntoList();
        LockedSlider();
    }

    void AddSliderIntoList()
    {
        _sliderList.Add(positionXSlider);
        _sliderList.Add(positionYSlider);
        _sliderList.Add(positionZSlider);

        _sliderList.Add(rotationXSlider);
        _sliderList.Add(rotationYSlider);
        _sliderList.Add(rotationZSlider);

        _sliderList.Add(scaleXSlider);
        _sliderList.Add(scaleYSlider);
        _sliderList.Add(scaleZSlider);
    }
    #region UIController

    private void AddSliderListeners()
    {
        if (listenersAdded) return;

        AddSliderListener(positionXSlider,
            () => xMove?.GetComponent<GizmoHandler>()?.CanUseTranslateX ?? false,
            value => UpdateTransformPosition(value, Axis.X));

        AddSliderListener(positionYSlider,
            () => yMove?.GetComponent<GizmoHandler>()?.CanUseTranslateY ?? false,
            value => UpdateTransformPosition(value, Axis.Y));

        AddSliderListener(positionZSlider,
            () => zMove?.GetComponent<GizmoHandler>()?.CanUseTranslateZ ?? false,
            value => UpdateTransformPosition(value, Axis.Z));

        AddSliderListener(rotationXSlider,
            () => xRotate?.GetComponent<GizmoHandler>()?.CanUseRotationX ?? false,
            value => UpdateTransformRotation(value, Axis.X));

        AddSliderListener(rotationYSlider,
            () => yRotate?.GetComponent<GizmoHandler>()?.CanUseRotationY ?? false,
            value => UpdateTransformRotation(value, Axis.Y));

        AddSliderListener(rotationZSlider,
            () => zRotate?.GetComponent<GizmoHandler>()?.CanUseRotationZ ?? false,
            value => UpdateTransformRotation(value, Axis.Z));

        AddSliderListener(scaleXSlider,
            () => xScale?.GetComponent<GizmoHandler>()?.CanUseScaleX ?? false,
            value => UpdateTransformScale(value, Axis.X));

        AddSliderListener(scaleYSlider,
            () => yScale?.GetComponent<GizmoHandler>()?.CanUseScaleY ?? false,
            value => UpdateTransformScale(value, Axis.Y));

        AddSliderListener(scaleZSlider,
            () => zScale?.GetComponent<GizmoHandler>()?.CanUseScaleZ ?? false,
            value => UpdateTransformScale(value, Axis.Z));

        listenersAdded = true;
    }

    private void AddSliderListener(Slider slider, System.Func<bool> condition, UnityEngine.Events.UnityAction<float> action)
    {
        if (slider == null)
        {
            Debug.LogError("Slider is null, cannot add listener.");
            return;
        }

        if (condition.Invoke())
        {
            slider.onValueChanged.AddListener(action);
        }
        else
        {
            slider.value = 0;
            slider.onValueChanged.RemoveAllListeners();
            Debug.LogWarning($"Condition not met for {slider.name}, listener not added.");
        }
    }

    private void UpdateTransformPosition(float value, Axis axis)
    {
        Transform selectedObject = null;

        if (axis == Axis.X) { selectedObject = xRotate; }
        if (axis == Axis.Y) { selectedObject = yRotate; }
        if (axis == Axis.Z) { selectedObject = zRotate; }

        Debug.Log("UpdateTransformRotation 1");

        if (selectedObject == null)
        {
            Debug.LogWarning("UpdateTransformRotation: Selected object is null for axis " + axis);
            return;
        }

        Vector3 position = selectedObject.position;
        switch (axis)
        {
            case Axis.X: position.x = value; break;
            case Axis.Y: position.y = value; break;
            case Axis.Z: position.z = value; break;
        }
        // selectedObject.position = position;

    }

    private void UpdateTransformRotation(float value, Axis axis)
    {
        Transform selectedObject = null;
        if (axis == Axis.X) { selectedObject = xRotate; }
        if (axis == Axis.Y) { selectedObject = yRotate; }
        if (axis == Axis.Z) { selectedObject = zRotate; }

        Debug.Log("UpdateTransformRotation 1");

        if (selectedObject == null)
        {
            Debug.LogWarning("UpdateTransformRotation: Selected object is null for axis " + axis);
            return;
        }

        Debug.Log("UpdateTransformRotation 2 " + selectedObject.name);
        Vector3 _rotation;
        if (selectedObject.name.Equals(SelectedRootObject.name))
        {
            _rotation = selectedObject.rotation.eulerAngles; // Get world rotation
        }
        else
        {
           _rotation = selectedObject.localEulerAngles;
        }
        switch (axis)
        {
            case Axis.X: _rotation.x = value; break;
            case Axis.Y: _rotation.y = value; break;
            case Axis.Z: _rotation.z = value; break;
        }
        if (selectedObject.name.Equals(SelectedRootObject.name))
        {
            Debug.Log($"Updated world rotation for {selectedObject.name}: {_rotation}");
            selectedObject.rotation = Quaternion.Euler(_rotation); 
        }
        else
        {
            Debug.Log($"Updated rotation for {selectedObject.name}: {_rotation}");
            selectedObject.localEulerAngles = _rotation;
        }

        
    }

    private void UpdateTransformScale(float value, Axis axis)
    {
        Transform selectedObject = null;
        if (axis.Equals("X")) { selectedObject = xScale; }
        if (axis.Equals("Y")) { selectedObject = yScale; }
        if (axis.Equals("Z")) { selectedObject = zScale; }
        if (selectedObject == null) return;

        Vector3 scale = selectedObject.localScale;
        switch (axis)
        {
            case Axis.X: scale.x = value; break;
            case Axis.Y: scale.y = value; break;
            case Axis.Z: scale.z = value; break;
        }
        selectedObject.localScale = scale;

        // CheckForMaxTranslation();
    }

    public Quaternion rot;
    private void UpdateSlidersFromTransform()
    {
        // Ensure values are clamped within slider range
        if (xMove != null) { positionXSlider.value = Mathf.Clamp(xMove.localPosition.x, positionXSlider.minValue, positionXSlider.maxValue); }
        if (yMove != null) { positionYSlider.value = Mathf.Clamp(yMove.localPosition.y, positionYSlider.minValue, positionYSlider.maxValue); } // Fixed: Uses correct slider
        if (zMove != null) { positionZSlider.value = Mathf.Clamp(zMove.localPosition.z, positionZSlider.minValue, positionZSlider.maxValue); } // Fixed

        // Handle rotation properly (converting Euler angles)
        if (xRotate != null) { rotationXSlider.value = Mathf.Clamp(NormalizeEulerAnglez(xRotate.localEulerAngles.x), rotationXSlider.minValue, rotationXSlider.maxValue); }
        if (yRotate != null) { rotationYSlider.value = Mathf.Clamp(NormalizeEulerAngley(yRotate.localEulerAngles.y), rotationYSlider.minValue, rotationYSlider.maxValue); }
        if (zRotate != null) { rotationZSlider.value = Mathf.Clamp(NormalizeEulerAnglez(zRotate.localEulerAngles.z), rotationZSlider.minValue, rotationZSlider.maxValue); }





        // Scale values clamped only 
        if (xScale != null) { scaleXSlider.value = Mathf.Clamp(xScale.localScale.x, scaleXSlider.minValue, scaleXSlider.maxValue); }
        if (yScale != null) { scaleYSlider.value = Mathf.Clamp(yScale.localScale.y, scaleYSlider.minValue, scaleYSlider.maxValue); }
        if (zScale != null) { scaleZSlider.value = Mathf.Clamp(zScale.localScale.z, scaleZSlider.minValue, scaleZSlider.maxValue); }
    }

    private float GetNormalizedRotation(float zRotation)
    {
        // Convert the rotation value to always be between 0 and 360
        zRotation = (zRotation + 360) % 360;
        return zRotation;
    }

    // Normalize rotation to -180 to 180 range
    private float NormalizeEulerAngley(float angle)
    {
        return (angle > 180) ? angle - 360 : angle;
    }
    private float NormalizeEulerAnglez(float angle)
    {
        return (angle < 0) ? angle + 360 : angle;
    }

    public void RemoveAllListeners()
    {
        positionXSlider.onValueChanged.RemoveAllListeners();
        positionYSlider.onValueChanged.RemoveAllListeners();
        positionZSlider.onValueChanged.RemoveAllListeners();

        rotationXSlider.onValueChanged.RemoveAllListeners();
        rotationYSlider.onValueChanged.RemoveAllListeners();
        rotationZSlider.onValueChanged.RemoveAllListeners();

        scaleXSlider.onValueChanged.RemoveAllListeners();
        scaleYSlider.onValueChanged.RemoveAllListeners();
        scaleZSlider.onValueChanged.RemoveAllListeners();
        listenersAdded = false;
    }
    #endregion

    private void Update()
    {
        AddSliderListeners();
        UpdateSlidersFromTransform();
    }

    #region ObjectSelectedOrDesSelect
    public void ObjectDesSelect()
    {
        //toggleSliderPanelAndClearList(false);
    }

    public void SelectObject(Transform obj)
    {
        Debug.Log("SelectObject 1");
        toggleSliderPanelAndClearList(false);
        if (obj.root.name.Contains("Room") || obj.root.name.Equals("Room"))
        {
            Debug.Log("SelectObject 2");
            return;
        }
        else
        {
            Debug.Log("SelectObject 3");
            toggleSliderPanelAndClearList(true);
            targetObject = obj;

            if (targetObject != null)
            {
                SelectedRootObject = targetObject.root;
                GetAllParents(targetObject);
                if (ObjectHaveNotAttechedPoint == false)
                {
                    if (SelectedRootObject != null)
                    {
                        if (obj.name.Equals(SelectedRootObject.name))
                        {
                            if (SelectedRootObject.GetComponent<GizmoHandler>() != null)
                            {
                                allChildren.Add(SelectedRootObject);
                            }
                        }
                    }
                    if (targetObjectMainParent != null)
                    {
                        if (targetObjectMainParent.GetComponent<GizmoHandler>() != null)
                        {
                            allChildren.Add(targetObjectMainParent);
                        }
                        GetAllChildren(targetObjectMainParent);
                        RemoveDuplicates(allChildren);
                    }
                }
                AssignTransformReferences(allChildren);
                Debug.Log("Unique Children: " + string.Join(", ", allChildren));
                Debug.Log(obj.name, gameObject);
                EventManager.OnCompareProximatryAlertWithOR_Table.Invoke(obj.gameObject, 1, 1.5f);
            }
        }
    }

    void GetAllChildren(Transform parent)
    {
        Debug.Log("Attached Point GameObject found in object or any of its children!");
       
        foreach (Transform child in parent)
        {
            if (child.GetComponent<GizmoHandler>() != null)
            {
                allChildren.Add(child);
                // AddGizmoData(child);
            }

            // Recursively check all children
            GetAllChildren(child);
        }
       
    }

    public Transform attachedPoint;
    void GetAllParents(Transform child)
    {
        // Find "Attached Point" in the child and its hierarchy
        attachedPoint = FindAttachedPoint(child);

        if (attachedPoint != null)
        {
            Debug.Log("Attached Point GameObject found in parent object");
            ObjectHaveNotAttechedPoint = false;
            Transform parent = child.parent;
            while (parent != null)
            {
                if (parent.name == "AttachPoint")
                {
                    targetObjectMainParent = parent;
                    break; // Stop searching once AttachPoint is found
                }

                if (parent.name.Equals(SelectedRootObject.name))
                {
                    targetObjectMainParent = parent;
                }

                parent = parent.parent; // Move up the hierarchy
            }
        }
        else
        {
            Debug.Log("Attached Point GameObject not found in object or any of its children!");
            ObjectHaveNotAttechedPoint = true;
            ifObjectDontHaveAttachedPoint(child);
        }
    }

    // Helper function: Recursively find "Attached Point" in children
    Transform FindAttachedPoint(Transform parent)
    {
        
        attachedPoint = parent.Find("AttachPoint");
        if (attachedPoint != null) return attachedPoint;

        foreach (Transform child in parent)
        {
            attachedPoint = FindAttachedPoint(child);
            if (attachedPoint != null) return attachedPoint;
        }

        return null;
    }

    void ifObjectDontHaveAttachedPoint(Transform obj)
    {
        Debug.Log("Attached Point GameObject not found in object or any of its children!");
        targetObjectMainParent = obj;
        if (obj.GetComponent<GizmoHandler>() != null)
        {
            allChildren.Add(obj);
        }
    }

    //rremove repeating object form final list
    void RemoveDuplicates(List<Transform> list)
    {
        HashSet<Transform> seenObjects = new HashSet<Transform>();
        list.RemoveAll(obj => !seenObjects.Add(obj));
    }

    private void AssignTransformReferences(List<Transform> objects)
    {
        foreach (Transform obj in objects)
        {
            GizmoHandler gizmo = obj.GetComponent<GizmoHandler>();
            if (gizmo == null) continue;

            // Position
            if (xMove == null && gizmo.CanUseTranslateX) { xMove = obj; SetSliderRangesFromGizmoData(obj, positionXSlider); CheckSliderCondition(positionXSlider, true); }
            if (yMove == null && gizmo.CanUseTranslateY) { yMove = obj; SetSliderRangesFromGizmoData(obj, positionYSlider); CheckSliderCondition(positionYSlider, true); }
            if (zMove == null && gizmo.CanUseTranslateZ) { zMove = obj; SetSliderRangesFromGizmoData(obj, positionZSlider); CheckSliderCondition(positionZSlider, true); }

            // Rotation
            if (xRotate == null && gizmo.CanUseRotationX) { xRotate = obj; SetSliderRangesFromGizmoData(obj, rotationXSlider); CheckSliderCondition(rotationXSlider, true); }
            if (yRotate == null && gizmo.CanUseRotationY) { yRotate = obj; SetSliderRangesFromGizmoData(obj, rotationYSlider); CheckSliderCondition(rotationYSlider, true); }
            if (zRotate == null && gizmo.CanUseRotationZ) { zRotate = obj; SetSliderRangesFromGizmoData(obj, rotationZSlider); CheckSliderCondition(rotationZSlider, true); }

            // Scale
            if (xScale == null && gizmo.CanUseScaleX) { xScale = obj; SetSliderRangesFromGizmoData(obj, scaleXSlider); CheckSliderCondition(scaleXSlider, true); }
            if (yScale == null && gizmo.CanUseScaleY) { yScale = obj; SetSliderRangesFromGizmoData(obj, scaleYSlider); CheckSliderCondition(scaleYSlider, true); }
            if (zScale == null && gizmo.CanUseScaleZ) { zScale = obj; SetSliderRangesFromGizmoData(obj, scaleZSlider); CheckSliderCondition(scaleZSlider, true); }

            // If all references are assigned, break early
            if (xMove != null && yMove != null && zMove != null &&
                xRotate != null && yRotate != null && zRotate != null &&
                xScale != null && yScale != null && zScale != null)
            {
                break;
            }
        }

        LockedSlider();
    }

    void LockedSlider()
    {
        if (xMove == null) { CheckSliderCondition(positionXSlider, false); }
        if (yMove == null) { CheckSliderCondition(positionYSlider, false); }
        if (zMove == null) { CheckSliderCondition(positionZSlider, false); }

        if (xRotate == null) { CheckSliderCondition(rotationXSlider, false); }
        if (yRotate == null) { CheckSliderCondition(rotationYSlider, false); }
        if (zRotate == null) { CheckSliderCondition(rotationZSlider, false); }

        if (xScale == null) { CheckSliderCondition(scaleXSlider, false); }
        if (yScale == null) { CheckSliderCondition(scaleYSlider, false); }
        if (zScale == null) { CheckSliderCondition(scaleZSlider, false); }
    }
    void CheckSliderCondition(Slider slider, bool _bool)
    {
        if (!_bool)
        {
            slider.value = 0;
        }
        slider.interactable = _bool;
    }
    void SetSliderRangesFromGizmoData(Transform obj, Slider slider)
    {
        if (slider == null)
        {
            Debug.LogError("SetSliderRangesFromGizmoData: Slider is null!");
            return; // Prevent null reference errors
        }

        Debug.Log("SetSliderRangesFromGizmoData: Called for object -> " + obj.name);

        Selectable selectable = obj.GetComponent<Selectable>();

        if (selectable == null)
        {
            Debug.LogError("SetSliderRangesFromGizmoData: No Selectable component found on object -> " + obj.name);
            return;
        }

        if (selectable.GizmoSettingsList == null || selectable.GizmoSettingsList.Count == 0)
        {
            Debug.LogWarning("SetSliderRangesFromGizmoData: GizmoSettingsList is null or empty for object -> " + obj.name);
            return; // Prevent errors if the list is null or empty
        }

        Debug.Log("SetSliderRangesFromGizmoData: Processing GizmoSettingsList with count -> " + selectable.GizmoSettingsList.Count);

        // Default min/max values
        float minValue = 0f;
        float maxValue = 1f;
        bool foundSetting = false;

        for (int i = 0; i < selectable.GizmoSettingsList.Count; i++) // Fixed: `i < Count`
        {
            var settings = selectable.GizmoSettingsList[i];
            Debug.Log($"Checking GizmoSettings {i}: Axis -> {settings.Axis}, GizmoType -> {settings.GizmoType}");

            if (slider == rotationXSlider && settings.Axis == Axis.X && settings.GizmoType == GizmoType.Rotate)
            {
                minValue = settings.MinValue;
                maxValue = settings.MaxValue;
                foundSetting = true;
                Debug.Log($"Matched rotationXSlider: minValue = {minValue}, maxValue = {maxValue}");
                break; // Stop looping once found
            }
            else if (slider == rotationYSlider && settings.Axis == Axis.Y && settings.GizmoType == GizmoType.Rotate)
            {
                minValue = settings.MinValue;
                maxValue = settings.MaxValue;
                foundSetting = true;
                Debug.Log($"Matched rotationYSlider: minValue = {minValue}, maxValue = {maxValue}");
                break;
            }
            else if (slider == rotationZSlider && settings.Axis == Axis.Z && settings.GizmoType == GizmoType.Rotate)
            {
                minValue = settings.MinValue;
                maxValue = settings.MaxValue;
                foundSetting = true;
                Debug.Log($"Matched rotationZSlider: minValue = {minValue}, maxValue = {maxValue}");
                break;
            }
        }

        if (!foundSetting)
        {
            Debug.LogWarning("SetSliderRangesFromGizmoData: No matching GizmoSettings found for slider -> " + slider.name);
        }

        if (minValue == 0 && maxValue == 0)
        {
            minValue = 0; maxValue = 360;
        }
        // Apply min/max to slider
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        Debug.Log($"SetSliderRangesFromGizmoData: Slider {slider.name} minValue set to {slider.minValue}, maxValue set to {slider.maxValue}");
    }

    private void SetSliderRangesFromGizmoData(Transform obj, Slider _slider, float min, float max)
    {
        Debug.Log("SetSliderRangesFromGizmoData != null >> 1 ");
        if (_slider != null)
        {
            if (min == 0 && max == 0)
            {
                _slider.minValue = 0;
                _slider.maxValue = 360;

            }
            else
            {
                _slider.minValue = min;
                _slider.maxValue = max;
            }

            UpdateSliderValue(_slider, obj);
        }
    }

    private void UpdateSliderValue(Slider slider, Transform obj)
    {
        if (obj == null || slider == null)
        {
            Debug.LogWarning("Object or Slider is null. Cannot update slider value.");
            return;
        }

        GizmoHandler gizmoHandler = obj.GetComponent<GizmoHandler>();
        if (gizmoHandler == null)
        {
            Debug.LogWarning($"GizmoHandler not found on {obj.name}");
            return;
        }

        if (gizmoHandler.CanUseTranslateX) slider.value = obj.position.x;
        else if (gizmoHandler.CanUseTranslateY) slider.value = obj.position.y;
        else if (gizmoHandler.CanUseTranslateZ) slider.value = obj.position.z;
        else if (gizmoHandler.CanUseRotationX) slider.value = obj.localEulerAngles.x;
        else if (gizmoHandler.CanUseRotationY) slider.value = obj.localEulerAngles.y;
        else if (gizmoHandler.CanUseRotationZ) slider.value = obj.localEulerAngles.z;
        else if (gizmoHandler.CanUseScaleX) slider.value = obj.localScale.x;
        else if (gizmoHandler.CanUseScaleY) slider.value = obj.localScale.y;
        else if (gizmoHandler.CanUseScaleZ) slider.value = obj.localScale.z;
        else
        {
            Debug.LogWarning($"No valid transformation type found for {obj.name}");
        }
    }

    //toggle UI panel with respect to object select or not
    public void toggleSliderPanelAndClearList(bool _bool)
    {
        Debug.Log("toggleSliderPanelAndClearList  >>  1");
        allChildren.Clear();
        RemoveAllListeners();
        ResetAllSliderSetting();
        ObjectHaveNotAttechedPoint = false;
        TransformPanel.gameObject.SetActive(_bool);
        if (_bool == false)
        {
            RemoveGameObjectRef();
        }
    }

    //set all object null when deselect object
    void RemoveGameObjectRef()
    {
        _minValue = 0f;
        _maxValue = 0f;
        targetObject = null;
        targetObjectMainParent = null;
        SelectedRootObject = null;

        xMove = null;
        yMove = null;
        zMove = null;

        xRotate = null;
        yRotate = null;
        zRotate = null;

        xScale = null;
        yScale = null;
        zScale = null;

    }

    void ResetAllSliderSetting()
    {
        foreach (Slider slider in _sliderList)
        {
            slider.minValue = 0;
            slider.maxValue = 0;
        }
    }
    #endregion
}
