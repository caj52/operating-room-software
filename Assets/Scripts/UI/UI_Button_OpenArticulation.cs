using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_Button_OpenArticulation : MonoBehaviour
{
    private Button button;
    // Start is called before the first frame update
    void Start()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(() => OnButtonClicked());
    }

    private void OnButtonClicked()
    {
        var Selectible = Selectable.SelectedSelectables;
        if (Selectible.Count>0)
        {
            GetHierarchyObjects.instance.SelectObject(Selectable.SelectedSelectables[0].gameObject.transform);
        }
         
    }
}
