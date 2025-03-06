using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UI_OnScreen_ObjectEditor : MonoBehaviour
{
    [field: SerializeField]
    private CinemachineVirtualCamera VirtualCamera { get; set; }

    private GameObject _cameraLookAt;

    private void Awake()
    {
        _cameraLookAt = new GameObject();
        VirtualCamera.LookAt = _cameraLookAt.transform;

        ObjectMenu.LastOpenedSelectableChanged.AddListener(UpdateObjects);
        Selectable.ActiveSelectablesInSceneChanged.AddListener(UpdateObjects);
    }

    private void Start()
    {
        UpdateObjects();
    }

    private void OnDestroy()
    {
        ObjectMenu.LastOpenedSelectableChanged.RemoveListener(UpdateObjects);
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(UpdateObjects);
    }

    private void UpdateObjects()
    {
        //bool buttonsActive = Selectable.ActiveSelectables.Count > 0;

        if (ObjectMenu.LastOpenedSelectable != null && 
        !ObjectMenu.LastOpenedSelectable.IsDestroyed) 
        {
            _cameraLookAt.transform.position = 
                ObjectMenu.LastOpenedSelectable.GetBounds().center;
        }
    }
}