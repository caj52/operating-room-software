using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraMovementControllerObjectEditor : MonoBehaviour
{
    private Bounds _currentBounds = new Bounds();
    private CinemachineVirtualCamera _virtualCamera;
    private const float _mouseScrollSensitivity = 0.1f;

    private void Awake()
    {
        _virtualCamera = GetComponent<CinemachineVirtualCamera>();
        ObjectMenu.LastOpenedSelectableChanged.AddListener(UpdateBounds);
    }

    private void OnDestroy()
    {
        ObjectMenu.LastOpenedSelectableChanged.RemoveListener(UpdateBounds);
    }

    private void Update()
    {
        if (FullScreenMenu.IsOpen) return;

        if (!InputHandler.MouseWasDownOverUI && Input.GetMouseButton(0))
        {
            //float distance = Vector3.Distance(_currentBounds.center, _currentLookPos);
            //var point1 = Camera.main.ScreenToWorldPoint(Vector3.zero);
            //var point2 = Camera.main.ScreenToWorldPoint(InputHandler.MouseDeltaPixels);
            //Vector3 dif = point2 - point1;
            //Vector3 newPos = transform.position + dif;
            //float newDis = Vector3.Distance(_currentBounds.center, newPos);
            //float disDif = newDis - distance;
            //newPos += (_currentBounds.center - newPos).normalized * disDif;

            //newPos.y = Mathf.Clamp(newPos.y, _currentBounds.min.y, _currentBounds.max.y);



            //Debug.Log($"Moving camera {InputHandler.MouseDeltaPixels} pixels to {newPos}");
            //_virtualCamera.ForceCameraPosition(newPos, transform.rotation);
            if (ObjectMenu.LastOpenedSelectable != null &&
            !ObjectMenu.LastOpenedSelectable.IsDestroyed)
            {
                ObjectMenu.LastOpenedSelectable
                    .transform
                    .RotateAround(
                        _currentBounds.center, 
                        Vector3.up, 
                        InputHandler.MouseDeltaScreenPercentage.x * Mathf.Rad2Deg);

                ObjectMenu.LastOpenedSelectable
                .transform
                .RotateAround(
                    _currentBounds.center,
                    transform.right,
                    InputHandler.MouseDeltaScreenPercentage.y * Mathf.Rad2Deg);
            }
        }

        if (Input.mouseScrollDelta.y != 0)
        {
            _virtualCamera.m_Lens.OrthographicSize = Mathf.Clamp(
                _virtualCamera.m_Lens.OrthographicSize - (Input.mouseScrollDelta.y * _mouseScrollSensitivity), 
                0.1f, 
                _currentBounds.size.magnitude);
        }
    }

    private void UpdateBounds()
    {
        if (ObjectMenu.LastOpenedSelectable != null &&
        !ObjectMenu.LastOpenedSelectable.IsDestroyed)
        {
            _currentBounds =
                ObjectMenu.LastOpenedSelectable.GetBounds();

            _virtualCamera.m_Lens.OrthographicSize = _currentBounds.size.magnitude;
            Vector3 pos = _currentBounds.center + (Vector3.right * (_currentBounds.size.magnitude + 1));
            Debug.Log($"Setting camera position to {pos}");
            _virtualCamera.ForceCameraPosition(pos, transform.rotation);
        }
    }
}
