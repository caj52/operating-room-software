using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class KeepRelativePosition : MonoBehaviour
{
    [field: SerializeField] 
    public Transform VirtualParent { get; private set; }

    [field: SerializeField] 
    private bool HideIfSurfaceIsHidden { get; set; }

    /// <summary>
    /// Populated when an object is loaded by the 
    /// <see cref="ConfigurationManager"/>
    /// </summary>
    public string ParentName { get; set; }

    private Vector3 _relativePosition;
    private RoomBoundary _roomBoundary;
    private InGameLight _light;
    private Transform _originalLightParent;
    private bool _subscribedRoomSizeChange;
    private bool _subscribedVisibilityChanged;
    private bool _isDestroyed;

    #region Monobehaviour
    private void Awake()
    {
        RecalculateRelativePosition();

        ConfigurationManager.OnRoomLoadComplete
            .AddListener(TryGetParent);

        SubscribeRoomSizeChange();
        SubscribeVisibility();

        _light = GetComponentInChildren<InGameLight>();

        if (_light != null) 
        {
            _originalLightParent = _light.transform.parent;
        }
    }

    private void OnDestroy()
    {
        _isDestroyed = true;

        ConfigurationManager.OnRoomLoadComplete
            .RemoveListener(TryGetParent);

        if (_subscribedRoomSizeChange)
        {
            RoomSize.RoomSizeChanged.RemoveListener(RoomSizeChanged);
        }

        if (_subscribedVisibilityChanged)
        {
            _roomBoundary.VisibilityStatusChanged
                .RemoveListener(CheckHideStatus);

            UI_ToggleShowCeilingObjects
                .CeilingObjectVisibilityToggled
                .RemoveListener(CheckHideStatus);

            CameraManager.CameraChanged.RemoveListener(CheckHideStatus);
        }
    }
    #endregion

    private void SubscribeRoomSizeChange()
    {
        if (_subscribedRoomSizeChange) 
            return;

        _subscribedRoomSizeChange = true;
        ConfigurationManager.OnRoomLoadComplete.AddListener(TryGetParent);
        RoomSize.RoomSizeChanged.AddListener(RoomSizeChanged);
    }

    private void SubscribeVisibility()
    {
        if (VirtualParent == null || _subscribedVisibilityChanged) 
            return;

        _roomBoundary = VirtualParent.GetComponent<RoomBoundary>();

        if (_roomBoundary != null && HideIfSurfaceIsHidden)
        {
            _roomBoundary.VisibilityStatusChanged
                .AddListener(CheckHideStatus);

            UI_ToggleShowCeilingObjects
                .CeilingObjectVisibilityToggled
                .AddListener(CheckHideStatus);

            CameraManager.CameraChanged.AddListener(CheckHideStatus);

            _subscribedVisibilityChanged = true;
            CheckHideStatus();
        }
    }

    private void TryGetParent()
    {
        if (string.IsNullOrEmpty(ParentName)) 
            return;

        var rootObj = GameObject.Find(ParentName);

        if (rootObj == null) 
            return;

        VirtualParent = rootObj.transform;
        SeatOnCeilingUnderside();
        RecalculateRelativePosition();
        SubscribeVisibility();
    }

    /// <summary>
    /// Ceiling-bound roots sit on the live underside (same Y the seed-light path uses).
    /// Saves store an absolute Y, so a 9'-6" authored cover in a 10' room would otherwise
    /// bake a 152 mm gap into <see cref="_relativePosition"/> and follow the ceiling forever.
    /// </summary>
    void SeatOnCeilingUnderside()
    {
        var rb = VirtualParent != null
            ? VirtualParent.GetComponent<RoomBoundary>()
            : null;
        if (rb == null || rb.RoomBoundaryType != RoomBoundaryType.Ceiling)
            return;

        float underside = ElevationRoomFrame.ComputeCeilingUndersideY(
            ElevationRoomFrame.ComputeFloorTopY());
        Vector3 p = transform.position;
        if (Mathf.Abs(p.y - underside) < 0.001f)
            return;

        p.y = underside;
        transform.position = p;
    }

    private void RecalculateRelativePosition()
    {
        if (VirtualParent != null) 
        {
            _relativePosition = transform.position - VirtualParent.position;
        }
    }

    public void VirtualParentChanged(Transform virtualParent)
    {
        VirtualParent = virtualParent;
        SubscribeVisibility();
    }

    public void CheckHideStatus()
    {
        if (_roomBoundary == null || !HideIfSurfaceIsHidden)
            return;

        bool enabled;
        if (_roomBoundary.RoomBoundaryType == RoomBoundaryType.Ceiling)
        {
            // Show Ceiling Objects only applies in ortho ceiling view (where the ceiling
            // mesh is hidden). In free look / other cams, keep fixtures visible.
            bool ceilingCamActive = false;
            if (CameraManager.ActiveCamera != null)
            {
                var orCam = CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>();
                ceilingCamActive = orCam != null
                    && orCam.CameraType == OperatingRoomCameraType.OrthoCeiling;
            }

            enabled = !ceilingCamActive || UI_ToggleShowCeilingObjects.ShowCeilingObjects;
        }
        else
        {
            enabled = _roomBoundary.MeshRenderer != null && _roomBoundary.MeshRenderer.enabled;
        }

        if (_light != null)
            _light.transform.parent = enabled ? _originalLightParent : null;

        gameObject.SetActive(enabled);
    }

    public void SelectablePositionChanged()
    {
        RecalculateRelativePosition();
    }

    private void GoToRelativePosition()
    {
        transform.position = VirtualParent != null ? VirtualParent.position + _relativePosition : _relativePosition;
        RecalculateRelativePosition();
    }

    private async void RoomSizeChanged(RoomDimension dimension)
    {
        if (VirtualParent == null) 
            return;

        await Task.Yield();

        if (!Application.isPlaying) 
            throw new Exception("App quit during async");

        if (_isDestroyed) 
            return;

        GoToRelativePosition();
    }
}
