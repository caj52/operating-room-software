using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_ButtonOpenSelectablesInSceneMenu : MonoBehaviour
{
    private void Awake()
    {
        Selectable.ActiveSelectablesInSceneChanged.AddListener(UpdateVisibility);
        UpdateVisibility();
    }

    private void OnDestroy()
    {
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(UpdateVisibility);
    }

    private void UpdateVisibility()
    {
        gameObject.SetActive(Selectable.ActiveSelectables.Count > 0);
    }

    public void OpenSelectablesInSceneMenu()
    {
        UI_Menu_SelectablesInScene.Open();
    }
}
