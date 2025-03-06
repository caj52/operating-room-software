using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class UI_ButtonExportObj : MonoBehaviour
{
    //private void Awake()
    //{
    //    Selectable.SelectionChanged += UpdateActiveState;

    //    //gameObject.SetActive(false);
    //}

    //private void OnDestroy()
    //{
    //    Selectable.SelectionChanged -= UpdateActiveState;
    //}

    //private void UpdateActiveState()
    //{
    //    gameObject.SetActive(Selectable.SelectedSelectables.Count > 0 && Selectable.SelectedSelectables[0].IsArmAssembly);
    //}

    public void ExportObj()
    {
        //if (Selectable.SelectedSelectables.Count > 0)
        //{
        //    if (Selectable.SelectedSelectables[0].TryGetArmAssemblyRoot(out GameObject obj))
        //    {
        //        ObjExporter.DoExport(true, obj, Get);
        //    }
        //    else
        //    {

        //        ObjExporter.DoExport(true, Selectable.SelectedSelectables[0].gameObject);
        //    }
        //}
        //else
        //{
        //    //UI_DialogPrompt.Open(
        //    //    "Would you like to include the walls and objects on the walls?", 
        //    //    new ButtonAction(
        //    //        "Yes", 
        //    //        () => ObjExporter.DoExport(true, Selectable.ActiveSelectables)),
        //    //    new ButtonAction(
        //    //        "No", 
        //    //        () => ObjExporter.DoExport(true, Selectable.ActiveSelectables, true)));
        //}
        if (string.IsNullOrEmpty(FullRoomSave.GetRoomPath()))
        {
            UI_DialogPrompt.Open(
             $"Please Export Room First",
             new ButtonAction("OK"));
          
        }
        else {
            UI_ObjExportOptions.Open();
        }
    }
}
