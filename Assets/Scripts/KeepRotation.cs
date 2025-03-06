using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeepRotation : MonoBehaviour
{
    private Quaternion _originalRotation;

    private void Start()
    {
        _originalRotation = transform.rotation;
    }

    private void LateUpdate()
    {
        //if (transform.rotation != _originalRotation) 
        //{ 
        //    transform.rotation = _originalRotation;
        //}
    }
}
