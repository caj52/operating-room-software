using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ScreenshotCamera : MonoBehaviour
{
    private static ScreenshotCamera _instance;
    private readonly float _scrollSensitivity = 0.01f;
    private readonly float _dragSensitivity = 30f;

    private CinemachineVirtualCamera _virtualCamera;
    [SerializeField] private CinemachineTargetGroup _targetGroup;
    List<MeshRenderer> _disabledMeshRenderers = new();
    List<Collider> _disabledColliders = new();
    public static bool IsActive { get; private set; }

    CinemachineOrbitalTransposer _transposer;
    CinemachineGroupComposer _composer;

    private void Awake()
    {
        _instance = this;
        _virtualCamera = GetComponent<CinemachineVirtualCamera>();
        _virtualCamera.Priority = 0;
        _transposer = _virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
        _composer = _virtualCamera.GetCinemachineComponent<CinemachineGroupComposer>();
    }

    private void Update()
    {
        //testing
        //if (Input.GetKeyUp(KeyCode.Space)) 
        //{ 
        //    if (!IsActive)
        //        EnableCamera();
        //    else
        //        DisableCamera();
        //}

        if (IsActive)
        {
            if (Input.GetMouseButton(0))
            {
                _transposer.m_XAxis.Value += InputHandler.MouseDeltaScreenPercentage.x * _dragSensitivity;
            }

            if (Input.mouseScrollDelta.y != 0)
            {
                _composer.m_GroupFramingSize = Mathf.Clamp(_composer.m_GroupFramingSize + Input.mouseScrollDelta.y * _scrollSensitivity, 0.1f, 3f);
            }
        }        
    }

    public void EnableCamera()
    {
        _virtualCamera.Priority = 999;

        Selectable parentSelectable = Selectable.SelectedSelectables[0].transform.root.GetComponent<Selectable>();

        List<MeshRenderer> allowedMeshRenderers = parentSelectable.gameObject.GetComponentsInChildren<MeshRenderer>().ToList();
        List<Collider> allowedColliders = parentSelectable.gameObject.GetComponentsInChildren<Collider>().ToList();
        List<Transform> activeChildTransforms = parentSelectable.gameObject.GetComponentsInChildren<Transform>().ToList();

        var targets = new List<CinemachineTargetGroup.Target>();

        activeChildTransforms.ForEach(item =>
        {
            targets.Add(new CinemachineTargetGroup.Target
            {
                target = item,
                weight = 1
            });
        });

        _targetGroup.m_Targets = targets.ToArray();

        _disabledMeshRenderers.Clear();
        _disabledColliders.Clear();

        GetAllObjectsInScene().ForEach(o =>
        {
            if (o.TryGetComponent<MeshRenderer>(out var meshRenderer) && !allowedMeshRenderers.Contains(meshRenderer)) 
            {
                if (meshRenderer.enabled) 
                {
                    _disabledMeshRenderers.Add(meshRenderer);
                    meshRenderer.enabled = false;
                }
            }

            if (o.TryGetComponent<Collider>(out var collider) && !allowedColliders.Contains(collider))
            {
                if (collider.gameObject.GetComponent<DeselectSelectableOnClick>() == null && collider.enabled)
                {
                    _disabledColliders.Add(collider);
                    collider.enabled = false;
                }
            }
        });

        IsActive = true;
        CameraManager.CameraChanged?.Invoke();
    }

    public void DisableCamera()
    {
        _virtualCamera.Priority = 0;
        _disabledMeshRenderers.ForEach(mr =>
        {
            mr.enabled = true;
        });
        _disabledColliders.ForEach(collider => collider.enabled = true);
        _disabledMeshRenderers.Clear();
        _disabledColliders.Clear();
        IsActive = false;
        CameraManager.CameraChanged?.Invoke();
    }

    private List<GameObject> GetAllObjectsInScene()
    {
        List<GameObject> objectsInScene = new List<GameObject>();

        foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)) as GameObject[])
        {
            if (go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave)
                continue;

            //if (!EditorUtility.IsPersistent(go.transform.root.gameObject))
            //    continue;

            if (!go.activeInHierarchy) continue;

            objectsInScene.Add(go);
        }

        return objectsInScene;
    }
}
