using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoorClearanceLineScaler : MonoBehaviour
{
    private float _originalLocalScaleY;

    private void Awake()
    {
        _originalLocalScaleY = transform.localScale.y;
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 scale = transform.localScale;
        scale.y = transform.root.localScale.x * _originalLocalScaleY;
        transform.localScale = scale;
    }
}
