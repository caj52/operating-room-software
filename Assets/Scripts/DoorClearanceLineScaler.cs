using UnityEngine;

public class DoorClearanceLineScaler : MonoBehaviour
{
    private float _originalLocalScaleY;
    private Transform _root;
    private float _lastRootScaleX;

    private void Awake()
    {
        _originalLocalScaleY = transform.localScale.y;
        _root = transform.root;
        _lastRootScaleX = _root.localScale.x;
        ApplyScale();
    }

    private void Update()
    {
        float rootScaleX = _root.localScale.x;
        if (Mathf.Approximately(rootScaleX, _lastRootScaleX))
            return;

        _lastRootScaleX = rootScaleX;
        ApplyScale();
    }

    private void ApplyScale()
    {
        Vector3 scale = transform.localScale;
        scale.y = _root.localScale.x * _originalLocalScaleY;
        transform.localScale = scale;
    }
}
