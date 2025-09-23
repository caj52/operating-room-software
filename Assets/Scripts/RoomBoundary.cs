using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class RoomBoundary : MonoBehaviour
{
    public UnityEvent SizeSet { get; } = new();
    public UnityEvent VisibilityStatusChanged { get; } = new();
    private static readonly float _mouseMoveSensitivityX = 10f;
    private static readonly float _mouseMoveSensitivityY = 10f;
    private static readonly float _scrollSensitivity = 0.5f;
    public static List<RoomBoundary> Instances { get; private set; } = new();
    public static readonly float DefaultWallThickness = 0.375f.ToMeters(); //4.5 inches
    [field: SerializeField] public RoomBoundaryType RoomBoundaryType { get; private set; }
    [field: SerializeField] private CinemachineVirtualCamera VirtualCamera { get; set; }

    private bool VirtualCameraActive => (CinemachineVirtualCamera)
        CinemachineCore.Instance.GetActiveBrain(0).ActiveVirtualCamera == VirtualCamera;

    public MeshRenderer MeshRenderer { get; private set; }
    public Collider Collider { get; private set; }

    [field: SerializeField]
    public List<GameObject> AdditionalObjectsToHide 
    { get; private set; }

    private CinemachineTransposer _transposer;
    private static Dictionary<RoomBoundaryType, RoomBoundary> RoomBoundariesByType { get; set; } = new();

    public float Height { get; private set; }
    public float Width { get; private set; }
    public float Depth { get; private set; }

    private void Awake()
    {
        Instances.Add(this);
        RoomBoundariesByType[RoomBoundaryType] = this;

        MeshRenderer = GetComponentInChildren<MeshRenderer>();
        Collider = GetComponentInChildren<Collider>();

        if (VirtualCamera != null)
        {
            CameraManager.Register(VirtualCamera);
            _transposer = VirtualCamera.GetCinemachineComponent<CinemachineTransposer>();
        }

        RoomSize.RoomSizeChanged.AddListener(SetSize);
    }

    private void OnDestroy()
    {
        Instances.Remove(this);
        RoomSize.RoomSizeChanged.RemoveListener(SetSize);
        CameraManager.Unregister(VirtualCamera);
    }

    void SetSize(RoomDimension dimension)
    {
        Height = dimension.Height.ToMeters();
        Width = dimension.Width.ToMeters();
        Depth = dimension.Depth.ToMeters();

        switch (RoomBoundaryType)
        {
            case RoomBoundaryType.Ceiling:
                transform.localScale = new Vector3(Width, DefaultWallThickness, Depth);
                transform.position = new Vector3(0, Height + (transform.localScale.y / 2), 0);
                break;
            case RoomBoundaryType.Floor:
                transform.localScale = new Vector3(Width, DefaultWallThickness, Depth);
                transform.position = new Vector3(0, 0 - (transform.localScale.y / 2), 0);
                break;
            case RoomBoundaryType.WallSouth:
                transform.localScale = new Vector3(Width, DefaultWallThickness, Height);
                transform.position = new Vector3(0, 0, 0 - (Depth / 2f) - (DefaultWallThickness / 2));
                break;
            case RoomBoundaryType.WallWest:
                transform.localScale = new Vector3(DefaultWallThickness, Depth, Height);
                transform.position = new Vector3(0 - (Width / 2f) - (DefaultWallThickness / 2f), 0, 0);
                break;
            case RoomBoundaryType.WallEast:
                transform.localScale = new Vector3(DefaultWallThickness, Depth, Height);
                transform.position = new Vector3(0 + (Width / 2f) + (DefaultWallThickness / 2f), 0, 0);
                break;
            case RoomBoundaryType.WallNorth:
                transform.localScale = new Vector3(Width, DefaultWallThickness, Height);
                transform.position = new Vector3(0, 0, 0 + (Depth / 2f) + (DefaultWallThickness / 2));
                break;
        }

        SizeSet?.Invoke();
    }

    public void OnCameraLive()
    {
        EnableAllMeshRenderersAndColliders();
        ToggleMeshRendererAndCollider(false);

        if (RoomBoundaryType == RoomBoundaryType.Ceiling || 
            RoomBoundaryType == RoomBoundaryType.Floor)
        {
            float dim = Mathf.Max(transform.localScale.x, transform.localScale.z);
            VirtualCamera.m_Lens.OrthographicSize = (dim / 2f) + (dim / 10f);
        }
        else
        {
            VirtualCamera.m_Lens.OrthographicSize = (transform.localScale.y / 2f) 
                + (transform.localScale.y / 10f);
        }
    }

    private void Update()
    {
        if (VirtualCamera != null && VirtualCameraActive)
        {
            HandleCameraMovement();
        }
    }

    private void HandleCameraMovement()
    {
        if (GizmoHandler.GizmoBeingUsed || FullScreenMenu.IsOpen) return;
        float scroll = -GetScrollWheel() * _scrollSensitivity;
        bool move = Input.GetMouseButton(0);
        Vector2 mouseMovement = new Vector2(move ? InputHandler.MouseDeltaScreenPercentage.x * _mouseMoveSensitivityX : 0, move ? InputHandler.MouseDeltaScreenPercentage.y * _mouseMoveSensitivityY : 0);

        switch (RoomBoundaryType)
        {
            case RoomBoundaryType.Ceiling:
                _transposer.m_FollowOffset.x -= mouseMovement.x;
                _transposer.m_FollowOffset.z -= mouseMovement.y;
                break;
            case RoomBoundaryType.WallEast:
                _transposer.m_FollowOffset.y -= mouseMovement.y;
                _transposer.m_FollowOffset.z -= mouseMovement.x;
                break;
            case RoomBoundaryType.WallSouth:
                _transposer.m_FollowOffset.x -= mouseMovement.x;
                _transposer.m_FollowOffset.y -= mouseMovement.y;
                break;
        }
        if (!EventSystem.current.IsPointerOverGameObject())
        {
            VirtualCamera.m_Lens.OrthographicSize = Mathf.Max(1f, VirtualCamera.m_Lens.OrthographicSize + scroll);
        }
    }

    private float GetScrollWheel()
    {
        return Input.mouseScrollDelta.y;
    }

    public static void EnableAllMeshRenderersAndColliders()
    {
        Instances.ForEach(item => item.ToggleMeshRendererAndCollider(true));
    }

    public void SetColor(Color c)
    {
        MeshRenderer.material.color = c;
    }

    /// <summary>
    /// Toggles the walls between opaque and clear
    /// </summary>
    /// <param name="toggle">True = Opaque, False = Clear</param>
    public static void ToggleAllWallOpaque(bool toggle)
    {
        Instances.ForEach(item => item.ToggleWallOpaque(toggle));
    }

    private void ToggleWallOpaque(bool toggle)
    {
        if (RoomBoundaryType == RoomBoundaryType.Floor || 
            RoomBoundaryType == RoomBoundaryType.Ceiling) 
            return;

        Color c = MeshRenderer.material.color;
        c.a = toggle ? 1 : 0;
        MeshRenderer.material.color = c;

        HandleAdditionalObjectVisibility(toggle);
    }

    private void OnMouseUpAsButton()
    {
        if (InputHandler.IsPointerOverUIElement()) 
            return;

        Selectable.DeselectAll();
    }

    private void ToggleMeshRendererAndCollider(bool toggle)
    {
        bool oldStatus = MeshRenderer.enabled;
        MeshRenderer.enabled = toggle;
        Collider.enabled = toggle;

        if (toggle != oldStatus)
        {
            VisibilityStatusChanged?.Invoke();
        }

        HandleAdditionalObjectVisibility(toggle);
    }

    private void HandleAdditionalObjectVisibility(bool toggle)
    {
        AdditionalObjectsToHide.ForEach(x =>
        {
            x.GetComponentsInChildren<MeshRenderer>()
                .ToList()
                .ForEach(y => y.enabled = toggle);

            x.GetComponentsInChildren<Collider>()
                .ToList()
                .ForEach(y => y.enabled = toggle);
        });
    }

    public RoomBoundary GetRoomBoundaryNonStatic(RoomBoundaryType roomBoundaryType)
    {
        return RoomBoundariesByType[roomBoundaryType];
    }
    public static RoomBoundary GetRoomBoundary(RoomBoundaryType roomBoundaryType)
    {
        return RoomBoundariesByType[roomBoundaryType];
    }

    public static void SetCeilingsAlpha(float alpha)
    {
        // Clamp alpha between 0 (fully transparent) and 1 (opaque)
        alpha = Mathf.Clamp01(alpha);
        foreach (var ceiling in Instances.Where(i => i.RoomBoundaryType == RoomBoundaryType.Ceiling))
        {
            if (ceiling.MeshRenderer != null && ceiling.MeshRenderer.material != null)
            {
                var mat = ceiling.MeshRenderer.material;
                Color c = mat.color;
                c.a = alpha;
                mat.color = c;

                // Attempt to ensure material is in a transparent rendering mode if using Standard shader
                // (safe no-op for custom shaders)
                var shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                if (shaderName.Contains("Standard") && alpha < 0.999f)
                {
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }
        }
    }
}

public enum RoomBoundaryType
{
    Ceiling,
    Floor,
    WallSouth,
    WallNorth,
    WallEast,
    WallWest
}