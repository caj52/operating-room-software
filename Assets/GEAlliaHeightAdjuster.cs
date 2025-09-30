using UnityEngine;

public class GEAlliaHeightAdjuster : MonoBehaviour
{
    [Tooltip("Child transform that should align to the ceiling underside when placed")]
    public Transform cielingAttachObject;

    [Header("Optional: shafts/pipes to stretch toward the ceiling attach point")]
    public Transform object1toScale;
    public Transform object2toScale;
    

    private Selectable _selectable;

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();
    }

    private void OnEnable()
    {
        if (_selectable != null)
        {
            _selectable.OnPlaced.AddListener(HandlePlaced);
        }
        RoomSize.RoomSizeChanged.AddListener(HandleRoomSizeChanged);
    }

    private void OnDisable()
    {
        if (_selectable != null)
        {
            _selectable.OnPlaced.RemoveListener(HandlePlaced);
        }
        RoomSize.RoomSizeChanged.RemoveListener(HandleRoomSizeChanged);
    }

    private void HandlePlaced()
    {
        AlignAttachToCeiling();
        ScaleObjectsToAttach();
    }

    private void HandleRoomSizeChanged(RoomDimension _)
    {
        AlignAttachToCeiling();
        ScaleObjectsToAttach();
    }

    /// <summary>
    /// Moves only the specified child so that its attach point sits flush with the
    /// underside of the room ceiling. Does not move the whole machine/root.
    /// </summary>
    public void AlignAttachToCeiling()
    {
        if (cielingAttachObject == null)
        {
            Debug.LogWarning("GEAlliaHeightAdjuster: cielingAttachObject is not assigned.");
            return;
        }

        var ceiling = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling);
        if (ceiling == null)
        {
            Debug.LogWarning("GEAlliaHeightAdjuster: Could not find RoomBoundary of type Ceiling.");
            return;
        }

        // Ceiling underside (visible side) is at position.y - (scale.y/2)
        float ceilingUndersideY = ceiling.transform.position.y - (ceiling.transform.localScale.y / 2f);

        // Compute how much to move ONLY the attach object so it aligns to the ceiling underside
        float currentAttachY = cielingAttachObject.position.y;
        float deltaY = ceilingUndersideY - currentAttachY;

        cielingAttachObject.position += new Vector3(0f, deltaY, 0f);
    }

    /// <summary>
    /// Scales object1toScale and object2toScale along their local Z so their length reaches
    /// the ceiling attach object from their pivot. Assumes pivot is at the base of the part.
    /// </summary>
    public void ScaleObjectsToAttach()
    {
        if (cielingAttachObject == null) return;

        if (object1toScale != null)
            ScaleAlongLocalZToTarget(object1toScale, cielingAttachObject.position);

        if (object2toScale != null)
            ScaleAlongLocalZToTarget(object2toScale, cielingAttachObject.position);
    }

    private void ScaleAlongLocalZToTarget(Transform obj, Vector3 targetWorldPos)
    {
        if (obj == null) return;

        // Distance along the object's local +Z (forward) from its pivot to the target
        Vector3 toTarget = targetWorldPos - obj.position;
        float distAlongZ = Vector3.Dot(toTarget, obj.forward);
        float targetLength = Mathf.Max(0.0f, distAlongZ);

        // Determine the mesh's original length along local Z (in local space units)
        float originalLocalLengthZ = 1f;
        if (obj.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
        {
            // mesh bounds are in local space
            originalLocalLengthZ = Mathf.Max(0.0001f, mf.sharedMesh.bounds.size.z);
        }

        // Figure out parent's combined scale along Z so we can compute the needed local scale
        float parentScaleZ = obj.lossyScale.z / Mathf.Max(0.0001f, obj.localScale.z);
        float newLocalScaleZ = targetLength / Mathf.Max(0.0001f, originalLocalLengthZ * parentScaleZ);

        // Clamp to sane range
        newLocalScaleZ = Mathf.Clamp(newLocalScaleZ, 0.001f, 1000f);

        Vector3 ls = obj.localScale;
        ls.z = newLocalScaleZ;
        obj.localScale = ls;
    }
}
