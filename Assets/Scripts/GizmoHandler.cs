using RTG;
using SplenSoft.UnityUtilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Color = UnityEngine.Color;

//[RequireComponent(typeof(Selectable))]
public class GizmoHandler : MonoBehaviour
{
    [field: SerializeField, HideInInspector] 
    public ObjectTransformGizmo _translateGizmo { get; private set; }

    [field: SerializeField, HideInInspector] 
    public ObjectTransformGizmo _rotateGizmo { get; private set; }

    public ObjectTransformGizmo _scaleGizmo;
    private ObjectTransformGizmo _universalGizmo;
    private bool _gizmosInitialized;
    public static bool GizmoBeingUsed { get; private set; }
    public bool GizmoUsedLastFrame { get; private set; }
    public bool IsBeingUsed { get; private set; }
    private Vector3 _positionBeforeStartDrag;
    private Vector3 _localScaleBeforeStartDrag;
    public Vector3 CurrentScaleDrag { get; private set; }
    public UnityEvent GizmoDragEnded { get; } = new UnityEvent();
    public UnityEvent GizmoDragPostUpdate { get; } = new UnityEvent();
    private Vector3 _lastCircleIntersectPoint;
    //private static readonly Color _colorTransparent = new(0, 0, 0, 0);
    public bool IsDestroyed { get; private set; }

    private bool RotateGizmoEnabled() => GizmoSelector.CurrentGizmoMode ==
        GizmoMode.Rotate && _selectable.IsSelected;

    private bool TranslateEnabled() => GizmoSelector.CurrentGizmoMode ==
        GizmoMode.Translate && _selectable.IsSelected;

    private bool ScaleEnabled() => GizmoSelector.CurrentGizmoMode ==
        GizmoMode.Scale && _selectable.IsSelected;

    [SerializeField, ReadOnly] private bool _canUseAnyTranslation;
    [SerializeField, ReadOnly] private bool _canUseTranslateX;
    [SerializeField, ReadOnly] private bool _canUseTranslateY;
    [SerializeField, ReadOnly] private bool _canUseTranslateZ;

    [SerializeField, ReadOnly] private bool _canUseAnyRotation;
    [SerializeField, ReadOnly] private bool _canUseRotationX;
    [SerializeField, ReadOnly] private bool _canUseRotationY;
    [SerializeField, ReadOnly] private bool _canUseRotationZ;

    [SerializeField, ReadOnly] private bool _canUseAnyScale;
    [SerializeField, ReadOnly] private bool _canUseScaleX;
    [SerializeField, ReadOnly] private bool _canUseScaleY;
    [SerializeField, ReadOnly] private bool _canUseScaleZ;

    // Public read-only properties for articulation
    public bool CanUseAnyTranslation => _canUseAnyTranslation;
    public bool CanUseTranslateX => _canUseTranslateX;
    public bool CanUseTranslateY => _canUseTranslateY;
    public bool CanUseTranslateZ => _canUseTranslateZ;

    public bool CanUseAnyRotation => _canUseAnyRotation;
    public bool CanUseRotationX => _canUseRotationX;
    public bool CanUseRotationY => _canUseRotationY;
    public bool CanUseRotationZ => _canUseRotationZ;

    public bool CanUseAnyScale => _canUseAnyScale;
    public bool CanUseScaleX => _canUseScaleX;
    public bool CanUseScaleY => _canUseScaleY;
    public bool CanUseScaleZ => _canUseScaleZ;

    private Selectable _selectable;

    private void Awake()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        _selectable = GetComponent<Selectable>();
        _selectable.Deselected.AddListener(EnableGizmo);
        GizmoSelector.GizmoModeChanged += GizmoModeChanged;
        UI_ToggleSnapping.SnappingToggled.AddListener(EnableGizmo);
        CameraManager.CameraChanged.AddListener(EnableGizmo);
    }

    private void OnDestroy()
    {
        IsDestroyed = true;

        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            enabled = false;
            return;
        }

        if (_selectable != null)
        {
            _selectable.Deselected.RemoveListener(EnableGizmo);
        }
        GizmoSelector.GizmoModeChanged -= GizmoModeChanged;
        UI_ToggleSnapping.SnappingToggled.RemoveListener(EnableGizmo);
        CameraManager.CameraChanged.RemoveListener(EnableGizmo);
    }

     
    private void OnEnable()
    {
        ArticualtionToolScript.uiChanged += OnExternalUIChange;
    }

    private void OnDisable()
    {
        ArticualtionToolScript.uiChanged -= OnExternalUIChange;
    }


    // 3) Called whenever the UI moves the object
    private void OnExternalUIChange()
    {
        // only if we?re actually showing a gizmo for this object
        if (!_selectable.IsSelected) return;
        // reuse our existing logic (plus the new scale line)
        UpdatePositionAndRotation();
    }


private IEnumerator Start()
    {
        yield return new WaitUntil(() => _selectable.Started);
        RefreshGizmoCapabilities();
    }

    /// <summary>
    /// Re-reads gizmo permissions from Selectable after deferred room-load init.
    /// </summary>
    public void RefreshGizmoCapabilities()
    {
        if (_selectable == null)
            _selectable = GetComponent<Selectable>();

        _canUseTranslateX = _selectable.IsGizmoSettingAllowed(GizmoType.Move, Axis.X);
        _canUseTranslateY = _selectable.IsGizmoSettingAllowed(GizmoType.Move, Axis.Y);
        _canUseTranslateZ = _selectable.IsGizmoSettingAllowed(GizmoType.Move, Axis.Z);
        _canUseAnyTranslation = _canUseTranslateX || _canUseTranslateY || _canUseTranslateZ;

        _canUseRotationX = _selectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.X);
        _canUseRotationY = _selectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Y);
        _canUseRotationZ = _selectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z);
        _canUseAnyRotation = _canUseRotationX || _canUseRotationY || _canUseRotationZ;

        _canUseScaleX = _selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.X);
        _canUseScaleY = _selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.Y);
        _canUseScaleZ = _selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z);
        _canUseAnyScale = _canUseScaleX || _canUseScaleY || _canUseScaleZ;

        if (_gizmosInitialized)
            EnableGizmo();
        else
            enabled = AnyEnabled();
    }

    private void Update()
    {
        UpdatePositionAndRotation();
    }

    private void GizmoModeChanged(object sender = null, EventArgs e = null)
    {
        EnableGizmo();
    }

    public void SelectableSelected()
    {
        CreateGizmos();
        if (_selectable.Started)
            RefreshGizmoCapabilities();
        EnableGizmo();
    }

    public void SelectableDeselected()
    {
        EnableGizmo();
    }

    private bool AnyEnabled()
    {
        return RotateGizmoEnabled() || TranslateEnabled() || ScaleEnabled();
    }

    private void UpdatePositionAndRotation()
    {
        if (!_selectable.IsSelected)
            return;

        _translateGizmo.Gizmo.Transform.Position3D = transform.position;
        _translateGizmo.Gizmo.Transform.LocalRotation3D = transform.localRotation;
        _translateGizmo.Gizmo.Transform.Rotation3D = transform.rotation;

        _rotateGizmo.Gizmo.Transform.Position3D = transform.position;
        _rotateGizmo.Gizmo.Transform.Rotation3D = transform.rotation;

        _scaleGizmo.Gizmo.Transform.LocalPosition3D = transform.position;
        _scaleGizmo.Gizmo.Transform.Rotation3D = transform.rotation;
    }

    private void UpdateScaleGizmo()
    {
        _scaleGizmo.Gizmo.ScaleGizmo.SetSnapEnabled(UI_ToggleSnapping.SnappingEnabled);

        // Caching, ScaleEnabled is expensive
        bool scaleEnabled = ScaleEnabled();
        bool xEnabled = scaleEnabled && _canUseScaleX;
        _scaleGizmo.Gizmo.ScaleGizmo._pstvXSlider.SetVisible(xEnabled);
        _scaleGizmo.Gizmo.ScaleGizmo._pstvXSlider.Set3DCapVisible(xEnabled);

        bool yEnabled = scaleEnabled && _canUseScaleX;
        _scaleGizmo.Gizmo.ScaleGizmo._pstvYSlider.SetVisible(yEnabled);
        _scaleGizmo.Gizmo.ScaleGizmo._pstvYSlider.Set3DCapVisible(yEnabled);

        bool zEnabled = scaleEnabled && _canUseScaleZ;
        _scaleGizmo.Gizmo.ScaleGizmo._pstvZSlider.SetVisible(zEnabled);
        _scaleGizmo.Gizmo.ScaleGizmo._pstvZSlider.Set3DCapVisible(zEnabled);

        _scaleGizmo.Gizmo.ScaleGizmo._xySlider.SetVisible(xEnabled && yEnabled);
        _scaleGizmo.Gizmo.ScaleGizmo._xySlider.SetBorderVisible(xEnabled && yEnabled);

        _scaleGizmo.Gizmo.ScaleGizmo._yzSlider.SetVisible(zEnabled && yEnabled);
        _scaleGizmo.Gizmo.ScaleGizmo._yzSlider.SetBorderVisible(xEnabled && yEnabled);

        _scaleGizmo.Gizmo.ScaleGizmo._zxSlider.SetVisible(xEnabled && zEnabled);
        _scaleGizmo.Gizmo.ScaleGizmo._zxSlider.SetBorderVisible(xEnabled && zEnabled);
    }

    private void UpdateRotationGizmo()
    {
        // Caching, RotateGizmoEnabled is expensive
        bool rotationEnabled = RotateGizmoEnabled();

        _rotateGizmo.Gizmo.RotationGizmo
            .SetSnapEnabled(UI_ToggleSnapping.SnappingEnabled);

        _rotateGizmo.Gizmo.RotationGizmo._xSlider
            .SetBorderVisible(_canUseRotationX && rotationEnabled);

        if (CameraManager.ActiveCamera == null) return;

        var currentCameraType = CameraManager.ActiveCamera
            .GetComponent<OperatingRoomCamera>().CameraType;

        bool allowVertical = currentCameraType !=
            OperatingRoomCameraType.OrthoCeiling;

        bool allowRotationY = rotationEnabled &&
            allowVertical && _canUseRotationY;

        _rotateGizmo.Gizmo.RotationGizmo._ySlider
            .SetBorderVisible(allowRotationY);

        bool allowHorizontal = currentCameraType !=
            OperatingRoomCameraType.OrthoSide;

        bool allowRotationZ = rotationEnabled &&
            allowHorizontal && _canUseRotationZ;

        _rotateGizmo.Gizmo.RotationGizmo._zSlider
            .SetBorderVisible(allowRotationZ);
    }

    private void UpdateTranslationGizmo()
    {
        _translateGizmo.Gizmo.MoveGizmo.SetSnapEnabled(UI_ToggleSnapping.SnappingEnabled);

        bool xMovementAllowed = _translateGizmo.Gizmo.IsEnabled &&
            _canUseTranslateX;

        bool yMovementAllowed = _translateGizmo.Gizmo.IsEnabled &&
            _canUseTranslateY;

        bool zMovementAllowed = _translateGizmo.Gizmo.IsEnabled &&
            _canUseTranslateZ;

        _translateGizmo.Gizmo.MoveGizmo._pXSlider.SetVisible(xMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._pYSlider.SetVisible(yMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._pZSlider.SetVisible(zMovementAllowed);

        _translateGizmo.Gizmo.MoveGizmo._pXSlider.Set3DCapVisible(xMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._pYSlider.Set3DCapVisible(yMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._pZSlider.Set3DCapVisible(zMovementAllowed);

        _translateGizmo.Gizmo.MoveGizmo._xySlider.SetVisible(xMovementAllowed && yMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._xySlider.SetBorderVisible(xMovementAllowed && yMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._yzSlider.SetVisible(yMovementAllowed && zMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._yzSlider.SetBorderVisible(xMovementAllowed && yMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._zxSlider.SetVisible(zMovementAllowed && xMovementAllowed);
        _translateGizmo.Gizmo.MoveGizmo._zxSlider.SetBorderVisible(zMovementAllowed && xMovementAllowed);
    }

    private void EnableGizmo()
    {
        if (!_gizmosInitialized) return;

        _translateGizmo.Gizmo.SetEnabled(TranslateEnabled());
        _rotateGizmo.Gizmo.SetEnabled(RotateGizmoEnabled());
        _scaleGizmo.Gizmo.SetEnabled(ScaleEnabled());

        UpdatePositionAndRotation();

        if (_canUseAnyTranslation)
            UpdateTranslationGizmo();

        if (_canUseAnyRotation)
            UpdateRotationGizmo();

        if (_canUseAnyScale)
            UpdateScaleGizmo();

        enabled = AnyEnabled();
    }

    private void CreateGizmos()
    {
        if (_gizmosInitialized) return;

        _translateGizmo = RTGizmosEngine.Get.CreateObjectMoveGizmo();

        // if (Selectable.SelectedSelectable.AllowInverseControl)
        //     _translateGizmo.Gizmo.MoveGizmo.Set2DModeEnabled(true);

        if (_selectable.AllowInverseControl)
        {
            _translateGizmo.SetTargetObject(_selectable.GetComponent<CCDIK>().Target.gameObject);
        }
        else
        {
            _translateGizmo.SetTargetObject(gameObject);
        }

        //_translateGizmo.Gizmo.MoveGizmo.SetVertexSnapTargetObjects(new List<GameObject> { gameObject });
        _translateGizmo.SetTransformSpace(GizmoSpace.Local);
        _translateGizmo.Gizmo.PostDragUpdate += OnGizmoPostDragUpdate;
        _translateGizmo.Gizmo.PostDragBegin += OnGizmoPostDragBegin;
        _translateGizmo.Gizmo.PostDragEnd += OnGizmoPostDragEnd;
        _translateGizmo.Gizmo.PreDragUpdate += OnGizmoPreDragUpdate;
        _translateGizmo.Gizmo.PreDragBegin += OnGizmoPreDragBegin;
        _translateGizmo.SetTransformPivot(GizmoObjectTransformPivot.ObjectMeshPivot);
        UpdateTranslationGizmo();

        _rotateGizmo = RTGizmosEngine.Get.CreateObjectRotationGizmo();
        _rotateGizmo.SetTargetObject(gameObject);
        _rotateGizmo.SetTransformSpace(GizmoSpace.Local);
        _rotateGizmo.Gizmo.PostDragUpdate += OnGizmoPostDragUpdate;
        _rotateGizmo.Gizmo.PostDragBegin += OnGizmoPostDragBegin;
        _rotateGizmo.Gizmo.PostDragEnd += OnGizmoPostDragEnd;
        _rotateGizmo.SetTransformPivot(GizmoObjectTransformPivot.ObjectMeshPivot);
        UpdateRotationGizmo();

        _scaleGizmo = RTGizmosEngine.Get.CreateObjectScaleGizmo();
        //_scaleGizmo.SetTargetObject(gameObject);
        _rotateGizmo.SetTransformSpace(GizmoSpace.Local);
        _scaleGizmo.Gizmo.PostDragEnd += OnGizmoPostDragEnd;
        _scaleGizmo.Gizmo.PostDragBegin += OnGizmoPostDragBegin;
        _scaleGizmo.Gizmo.PreDragUpdate += OnGizmoPreDragUpdate;
        _scaleGizmo.Gizmo.PostDragUpdate += OnGizmoPostDragUpdate;
        _scaleGizmo.Gizmo.PreDragBegin += OnGizmoPreDragBegin;
        _scaleGizmo.SetTransformPivot(GizmoObjectTransformPivot.ObjectMeshPivot);
        UpdateScaleGizmo();

        _gizmosInitialized = true;
    }

    private void OnGizmoPreDragBegin(Gizmo gizmo, int handleId)
    {
        _translateGizmo.Gizmo.Transform.LocalRotation3D = transform.localRotation;
        _localScaleBeforeStartDrag = _selectable.UseLossyScale ? transform.lossyScale : transform.localScale;
        _positionBeforeStartDrag = transform.position;
        CurrentScaleDrag = _selectable.UseLossyScale ? transform.lossyScale : transform.localScale;
        //_lastCircleIntersectPoint = default;
        IsBeingUsed = true;

        if (TryGetComponent(out CCDIK ik))
        {
            ik.enabled = true;
        }
    }

    private void OnGizmoPreDragUpdate(Gizmo gizmo, int handleId)
    {
        //_positionBeforeStartDrag = transform.localPosition;
    }

    private async void OnGizmoPostDragEnd(Gizmo gizmo, int handleId)
    {
        if (TryGetComponent(out KeepRelativePosition k))
        {
            k.SelectablePositionChanged();
        }

        GizmoBeingUsed = false;
        IsBeingUsed = false;
        GizmoDragEnded?.Invoke();
        ArticualtionToolScript.gizmoChanged?.Invoke();
        await Task.Yield();
        if (!Application.isPlaying)
            throw new AppQuitInTaskException();

        GizmoUsedLastFrame = false;

        _translateGizmo.Gizmo.Transform
            .Position3D = transform.position;

        _translateGizmo.Gizmo.Transform
            .LocalRotation3D = transform.localRotation;

        if (TryGetComponent(out CCDIK ik))
        {
            ik.enabled = false;
        }
    }

    private void OnGizmoPostDragBegin(Gizmo gizmo, int handleId)
    {
        GizmoBeingUsed = true;
    }

    // Find the points where the two circles intersect.
    private int FindCircleCircleIntersections(
        float cx0, float cy0, float radius0,
        float cx1, float cy1, float radius1,
        out Vector2 intersection1, out Vector2 intersection2)
    {
        // Find the distance between the centers.
        float dx = cx0 - cx1;
        float dy = cy0 - cy1;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // See how many solutions there are.
        if (dist > radius0 + radius1)
        {
            // No solutions, the circles are too far apart.
            intersection1 = new Vector2(float.NaN, float.NaN);
            intersection2 = new Vector2(float.NaN, float.NaN);
            return 0;
        }
        else if (dist < Math.Abs(radius0 - radius1))
        {
            // No solutions, one circle contains the other.
            intersection1 = new Vector2(float.NaN, float.NaN);
            intersection2 = new Vector2(float.NaN, float.NaN);
            return 0;
        }
        else if ((dist == 0) && (radius0 == radius1))
        {
            // No solutions, the circles coincide.
            intersection1 = new Vector2(float.NaN, float.NaN);
            intersection2 = new Vector2(float.NaN, float.NaN);
            return 0;
        }
        else
        {
            // Find a and h.
            double a = (radius0 * radius0 - 
                radius1 * radius1 + dist * dist) / (2 * dist);
            double h = Math.Sqrt(radius0 * radius0 - a * a);

            // Find P2.
            double cx2 = cx0 + a * (cx1 - cx0) / dist;
            double cy2 = cy0 + a * (cy1 - cy0) / dist;

            // Get the points P3.
            intersection1 = new Vector2(
                (float)(cx2 + h * (cy1 - cy0) / dist),
                (float)(cy2 - h * (cx1 - cx0) / dist));
            intersection2 = new Vector2(
                (float)(cx2 - h * (cy1 - cy0) / dist),
                (float)(cy2 + h * (cx1 - cx0) / dist));

            // See if we have 1 or 2 solutions.
            if (dist == radius0 + radius1) return 1;
            return 2;
        }
    }

    private void OnGizmoPostDragUpdate(Gizmo gizmo, int handleId)
    {
        GizmoUsedLastFrame = true;

        if (gizmo.ObjectTransformGizmo == _translateGizmo && _selectable.ExeedsMaxTranslation(out Vector3 totalExcess))
        {
            HandleTranslationGizmo(gizmo, totalExcess);
        }

        if (gizmo.ObjectTransformGizmo == _rotateGizmo && _selectable.ExceedsMaxRotation(out totalExcess))
        {
            HandleRotationGizmo(gizmo, totalExcess);
        }

        if (gizmo.ObjectTransformGizmo == _scaleGizmo)
        {
            HandleScaleGizmo(gizmo);
        }

        GizmoDragPostUpdate?.Invoke();

        ArticualtionToolScript.gizmoChanged?.Invoke();
    }

    private void HandleTranslationGizmo(Gizmo gizmo, Vector3 totalExcess)
    {
        if (_selectable.AllowInverseControl)
        {
            _translateGizmo.Gizmo.Transform.Position3D = transform.position;
            return;
        }

        transform.localPosition -= totalExcess;
        Transform parent = transform.parent;

        //do vertical component first
        if (gizmo.RelativeDragOffset.y != 0)
        {
            HandleVerticalComponent(gizmo, parent);
        }

        //try a two-bone triangle movement
        HandleTwoBoneTriangleMovement(gizmo, parent);

        _translateGizmo.Gizmo.Transform.Position3D = transform.position;
    }

    private void HandleVerticalComponent(Gizmo gizmo, Transform parent)
    {
        // Debug.Log($"Inside the vertical if with offset {gizmo.RelativeDragOffset.y}");
        Selectable verticalComponent = null;

        while (parent != null && verticalComponent == null && _selectable.AllowInverseControl)
        {
            var parentSelectable = parent.GetComponent<Selectable>();
            if (parentSelectable != null && parentSelectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Y))
            {
                verticalComponent = parentSelectable;
                break;
            }
            parent = parent.parent;
        }

        if (verticalComponent != null)
        {
            Vector3 desiredPosition = new Vector3(_positionBeforeStartDrag.x, _positionBeforeStartDrag.y + gizmo.TotalDragOffset.y, _positionBeforeStartDrag.z);
            Vector3 currentPos = new Vector3(_positionBeforeStartDrag.x, transform.position.y, _positionBeforeStartDrag.z);
            float angle = Vector3.Angle(desiredPosition, currentPos);
            float distance = Vector3.Distance(desiredPosition, currentPos);

            verticalComponent.transform.Rotate(0, angle, 0);
            // Debug.Log($"Found Vertical Component {angle}");
            currentPos = new Vector3(_positionBeforeStartDrag.x, transform.position.y, _positionBeforeStartDrag.z);
            float distance2 = Vector3.Distance(desiredPosition, currentPos);
            if (distance2 > distance)
            { // we went the wrong way, flip the angle
                verticalComponent.transform.Rotate(0, -angle * 2f, 0);
            }

            if (verticalComponent.ExceedsMaxRotation(out Vector3 totalExcess1))
            {
                verticalComponent.transform.localRotation *= Quaternion.Euler(-totalExcess1.x, -totalExcess1.y, -totalExcess1.z);
            }
        }
    }

    private void HandleTwoBoneTriangleMovement(Gizmo gizmo, Transform parent)
    {
        Transform closestBone = null;
        Transform farthestBone = null;
        parent = transform.parent;
        while (parent != null && (closestBone == null || farthestBone == null))
        {
            var parentSelectable = parent.GetComponent<Selectable>();
            if (parentSelectable != null && parentSelectable.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z))
            {
                if (closestBone == null)
                {
                    closestBone = parent;
                }
                else
                {
                    farthestBone = parent;
                    break;
                }
            }
            parent = parent.parent;
        }

        if (closestBone != null && farthestBone != null && _selectable.AllowInverseControl)
        {
            Vector3 farthestBoneXZ = new Vector3(farthestBone.transform.position.x, 0, farthestBone.transform.position.z);
            Vector3 closestBoneXZ = new Vector3(closestBone.transform.position.x, 0, closestBone.transform.position.z);
            Vector3 thisTransformXZ = new Vector3(transform.position.x + gizmo.RelativeDragOffset.x, 0, transform.position.z + gizmo.RelativeDragOffset.z);
            //Debug.Log(thisTransformXZ);
            float circle1Radius = Vector3.Distance(farthestBoneXZ, closestBoneXZ);
            float circle2Radius = Vector3.Distance(closestBoneXZ, new Vector3(transform.position.x, 0, transform.position.z));
            int intersects = FindCircleCircleIntersections(farthestBoneXZ.x, farthestBoneXZ.z, circle1Radius, thisTransformXZ.x, thisTransformXZ.z, circle2Radius, out Vector2 intersection1, out Vector2 intersection2);

            if (intersects > 0)
            {

                Vector3 intersect1 = new Vector3(intersection1.x, 0, intersection1.y);
                Vector3 intersect = intersect1;
                if (intersects > 1)
                {
                    Vector3 intersect2 = new Vector3(intersection2.x, 0, intersection2.y);
                    float distance1, distance2;
                    if (_lastCircleIntersectPoint == default)
                    {
                        // use intersect closest to destination
                        distance1 = Vector3.Distance(intersect1, thisTransformXZ);
                        distance2 = Vector3.Distance(intersect2, thisTransformXZ);
                    }
                    else
                    {
                        // get point closest to last intersect
                        distance1 = Vector3.Distance(intersect1, _lastCircleIntersectPoint);
                        distance2 = Vector3.Distance(intersect2, _lastCircleIntersectPoint);
                    }

                    intersect = distance1 < distance2 ? intersect1 : intersect2;
                    _lastCircleIntersectPoint = intersect;
                }

                //Quaternion originalFacing = _translateGizmo.Gizmo.Transform.Rotation3D;
                float angleBetween = -Vector3.SignedAngle(farthestBone.right, intersect - farthestBoneXZ, Vector3.up);
                farthestBone.transform.Rotate(new Vector3(0, 0, angleBetween));
                closestBoneXZ = new Vector3(closestBone.transform.position.x, 0, closestBone.transform.position.z);
                angleBetween = -Vector3.SignedAngle(closestBone.right, thisTransformXZ - closestBoneXZ, Vector3.up);
                closestBone.transform.Rotate(new Vector3(0, 0, angleBetween));
                //transform.rotation = originalFacing;
                //_translateGizmo.Gizmo.Transform.Rotation3D = originalFacing;
            }
        }
    }

    private void HandleRotationGizmo(Gizmo gizmo, Vector3 totalExcess)
    {
        transform.localRotation *= Quaternion.Euler(-totalExcess.x, -totalExcess.y, -totalExcess.z);
        _rotateGizmo.Gizmo.Transform.Rotation3D = transform.rotation;
    }

    private void HandleScaleGizmo(Gizmo gizmo)
    {
        CurrentScaleDrag = new Vector3(_localScaleBeforeStartDrag.x * gizmo.TotalDragScale.x, _localScaleBeforeStartDrag.y * gizmo.TotalDragScale.y, _localScaleBeforeStartDrag.z * gizmo.TotalDragScale.z);

        float xScale = _localScaleBeforeStartDrag.x * (_selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.X) ? gizmo.TotalDragScale.x : 1);
        float yScale = _localScaleBeforeStartDrag.y * (_selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.Y) ? gizmo.TotalDragScale.y : 1);
        float zScale = CalculateZScale(gizmo);

        if (UI_ToggleSnapping.SnappingEnabled)
        {
            ApplySnapping(ref xScale, ref yScale, ref zScale);
        }

        if (_selectable.UseLossyScale && _selectable.TryGetGizmoSetting(GizmoType.Scale, Axis.Z, out GizmoSetting gizmoSetting))
        {
            float oldScaleZ = _selectable.transform.localScale.z;

            if (_selectable.transform.lossyScale.z > gizmoSetting.GetMaxValue())
            {
                _selectable.transform.localScale = new Vector3(1, 1, zScale);

                if (_selectable.transform.lossyScale.z > gizmoSetting.GetMaxValue())
                {
                    _selectable.transform.localScale = new Vector3(1, 1, oldScaleZ);
                    return;
                }

                return;
            }

            yScale = 1;
            xScale = 1;
        }

        _selectable.transform.localScale = new Vector3(xScale, yScale, zScale);

        if (_selectable.ParentSelectable != null)
        {
            if (_selectable.ParentSelectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z))
            {
                _selectable.ParentSelectable.StoreChildScales();
            }
        }

        //if (TryGetComponent(out ScaleGroup group))
        //{
        //    ScaleGroupManager.OnZScaleChanged?.Invoke(group.id, _selectable.transform.lossyScale.z);
        //}
    }

    private float CalculateZScale(Gizmo gizmo)
    {
        float zScale = _localScaleBeforeStartDrag.z;
        if (_selectable.ScaleLevels.Count == 0)
        {
            if (_selectable.IsGizmoSettingAllowed(GizmoType.Scale, Axis.Z))
            {
                if (zScale == 0) zScale = 0.1f;
                zScale *= gizmo.TotalDragScale.z;

                if (_selectable.TryGetGizmoSetting(GizmoType.Scale, Axis.Z, out GizmoSetting gizmoSetting) && !gizmoSetting.Unrestricted)
                {

                    zScale = Mathf.Clamp(zScale, gizmoSetting.GetMinValue(), gizmoSetting.GetMaxValue());
                }
            }
        }
        else
        {
            zScale = _selectable.transform.localScale.z;
        }

        return zScale;
    }

    private void ApplySnapping(ref float xScale, ref float yScale, ref float zScale)
    {
        if (xScale != _localScaleBeforeStartDrag.x)
        {
            xScale = Selectable.RoundToNearestHalfInch(xScale);
        }

        if (yScale != _localScaleBeforeStartDrag.y)
        {
            yScale = Selectable.RoundToNearestHalfInch(yScale);
        }

        if (_selectable.ScaleLevels.Count == 0 && zScale != _localScaleBeforeStartDrag.z)
        {
            zScale = Selectable.RoundToNearestHalfInch(zScale);
        }
    }

    // Additional methods for handling specific parts of the gizmo handling logic would go here.
}

public enum GizmoMode
{
    Translate,
    Rotate,
    Scale,
    Universal
}
