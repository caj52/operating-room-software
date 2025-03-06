using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UI_ButtonDeleteObject : MonoBehaviour
{
    private void Awake()
    {
        Selectable.SelectionChanged += UpdateActiveState;

        gameObject.SetActive(false);
    }

    private void UpdateActiveState()
    {
        if (!Application.isPlaying) return;
        if (this == null || gameObject == null) return;

        bool active = Selectable.SelectedSelectables.Count > 0;

        if (active && !Selectable.SelectedSelectables
        .Any(x => x.IsDestructible)) 
            return;

        gameObject.SetActive(active);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateActiveState;
    }

    public void DeleteSelectedSelectable()
    {
        UI_DialogPrompt.Open("Are you sure you want to delete this object?",
            new ButtonAction
            {
                ButtonText = "Yes",
                Action = () =>
                    {
                        var selectables = Selectable.SelectedSelectables;
                        Destroy(selectables[0].gameObject);
                        UI_DialogPrompt.Close();
                    },
            },
            new ButtonAction
            {
                ButtonText = "Cancel"
            }
        );
    }
}