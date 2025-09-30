using System;
using UnityEngine;

public class NitrogenRegulatotPositionSetter : MonoBehaviour
{
    [Header("Attachment context")]
    [SerializeField] private AttachmentPoint attachmentPoint;

    [Header("Parent name to match (optional)")]
    [SerializeField] private string railPlateParentName = "NitrogenRegulatorAtachPoint";
    [SerializeField] private bool caseInsensitiveMatch = true;

    [Header("Position to apply")] 
    [Tooltip("If true, applies to localPosition; otherwise to world position")]
    [SerializeField] private bool useLocalPosition = true;

    [Tooltip("Position when this object is on a rail plate (or parent name matches)")]
    [SerializeField] private Vector3 positionWhenChildOfRailPlate = new Vector3(0, 0.25f, 0.23f);

    [Tooltip("Position when this object is NOT on a rail plate")]
    [SerializeField] private Vector3 positionOtherwise = new Vector3(0, 0, 0);

    [Header("Rail detection keywords (name contains any)")]
    [SerializeField] private string[] railNameKeywords = new[] { "Standard_Rail_v1", "Rear_Rail", "SHP_Rails", "Rail" };

    private void Awake()
    {
        // Auto-resolve attachment point if not explicitly assigned
        if (attachmentPoint == null)
        {
            attachmentPoint = GetComponentInParent<AttachmentPoint>();
        }
    }

    private void OnEnable()
    {
        if (attachmentPoint != null)
        {
            attachmentPoint.StatusUpdated?.AddListener(OnAttachmentStatusUpdated);
        }
        ApplyPosition();
    }

    private void OnDisable()
    {
        if (attachmentPoint != null)
        {
            attachmentPoint.StatusUpdated?.RemoveListener(OnAttachmentStatusUpdated);
        }
    }

    private void Start()
    {
        ApplyPosition();
    }

    private void OnAttachmentStatusUpdated(bool _)
    {
        ApplyPosition();
    }

    private void OnTransformParentChanged()
    {
        // Re-resolve attachment point if our hierarchy moved
        if (attachmentPoint == null)
        {
            attachmentPoint = GetComponentInParent<AttachmentPoint>();
        }
        ApplyPosition();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (attachmentPoint == null)
                attachmentPoint = GetComponentInParent<AttachmentPoint>();
            ApplyPosition();
        }
    }
#endif

    private void ApplyPosition()
    {
        bool onRailPlate = IsOnRailPlate();
        var target = onRailPlate ? positionWhenChildOfRailPlate : positionOtherwise;

        if (useLocalPosition)
            transform.localPosition = target;
        else
            transform.position = target;
    }

    private bool IsOnRailPlate()
    {
        // 1) Any ancestor-name match (safe traversal)
        Transform current = transform.parent;
        while (current != null)
        {
            if (NamesEqual(current.name, railPlateParentName, caseInsensitiveMatch))
            {
                return true;
            }
            current = current.parent;
        }

        // 2) Check attachment point's attached selectables for any "rail" keyword
        if (attachmentPoint != null && attachmentPoint.AttachedSelectable != null)
        {
            foreach (var sel in attachmentPoint.AttachedSelectable)
            {
                if (sel == null) continue;
                string n = sel.name ?? string.Empty;
                foreach (var kw in railNameKeywords)
                {
                    if (!string.IsNullOrEmpty(kw) && n.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
        }

        return false;
    }

    private static bool NamesEqual(string a, string b, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        a = a.Trim();
        b = b.Trim();
        return string.Compare(a, b, ignoreCase) == 0;
    }
}
