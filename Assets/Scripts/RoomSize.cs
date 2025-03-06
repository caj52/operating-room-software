using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Singleton class
/// </summary>
public class RoomSize : MonoBehaviour
{
    public static RoomSize Instance { get; set; }

    public static UnityEvent<RoomDimension> RoomSizeChanged { get; } = new();

    private readonly float _minimumSizeInFeet = 6f;

    [field: SerializeField, HideInInspector] 
    public RoomDimension CurrentDimensions { get; private set; }

    [field: SerializeField] private TMP_InputField InputFieldWidth { get; set; }
    [field: SerializeField] private TMP_InputField InputFieldHeight { get; set; }
    [field: SerializeField] private TMP_InputField InputFieldDepth { get; set; }

    /// <summary>
    /// The bounds that represents the entire room
    /// </summary>
    public static Bounds Bounds { get; private set; }

    private void Awake()
    {
        Instance = this;

        InputFieldWidth.onEndEdit
            .AddListener(text => EnforceDimensionSize(InputFieldWidth, text));

        InputFieldHeight.onEndEdit
            .AddListener(text => EnforceDimensionSize(InputFieldHeight, text));

        InputFieldDepth.onEndEdit
            .AddListener(text => EnforceDimensionSize(InputFieldDepth, text));

        RoomSizeChanged.AddListener(OnRoomSizeChanged);
        RoomSizeChanged.AddListener(UpdateBounds);
    }

    private void OnDestroy()
    {
        RoomSizeChanged.RemoveListener(OnRoomSizeChanged);
        RoomSizeChanged.RemoveListener(UpdateBounds);
    }

    private static async void UpdateBounds(RoomDimension dimension = default)
    {
        await Task.Yield();

        if (!Application.isPlaying)
            return;

        var bounds = new Bounds(Vector3.zero, Vector3.zero);

        RoomBoundary.Instances.ForEach(x =>
        {
            bounds.Encapsulate(x.MeshRenderer.bounds);
        });

        Bounds = bounds;
    }

    public static void SetDimensions(RoomDimension roomDimension)
    {
        RoomSizeChanged?.Invoke(roomDimension);
    }

    private void OnRoomSizeChanged(RoomDimension dim)
    {
        CurrentDimensions = dim;
    }

    private void EnforceDimensionSize(TMP_InputField inputField, string text)
    {
        if (IsDimensionTooSmall(text))
            inputField.SetTextWithoutNotify(_minimumSizeInFeet.ToString());
    }

    private bool IsDimensionTooSmall(string text)
    {
        if (float.TryParse(text, out float result))
        {
            if (result < _minimumSizeInFeet)
            {
                Debug.LogError($"Room dimensions must be a minimum of {_minimumSizeInFeet} feet");
                return true;
            }
        }
        return false;
    }

    public void OnButtonConfirm()
    {
        RoomSizeChanged?.Invoke(new RoomDimension(
            float.Parse(Instance.InputFieldWidth.text),
            float.Parse(Instance.InputFieldHeight.text),
            float.Parse(Instance.InputFieldDepth.text)
            ));
        gameObject.SetActive(false);
    }
}

[Serializable]
public struct RoomDimension
{
    /// <summary>
    /// Parameters are in feet
    /// </summary>
    public RoomDimension(float w, float h, float d)
    {
        Width = w;
        Height = h;
        Depth = d;
    }

    /// <summary>
    /// Measured in feet
    /// </summary>
    public float Width;
    /// <summary>
    /// Measured in feet
    /// </summary>
    public float Height;
    /// <summary>
    /// Measured in feet
    /// </summary>
    public float Depth;
}