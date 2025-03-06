using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Parts of this class should eventually be refactored to reduce memory allocations (too many Destroy/Instantiate loops)
/// </summary>
public class UI_Menu_SelectablesInScene : MonoBehaviour
{
    private static UI_Menu_SelectablesInScene Instance { get; set; }

    [field: SerializeField] private GameObject ItemTemplate { get; set; }
    List<GameObject> InstantiatedItems = new List<GameObject>();

    private void Awake()
    {
        Instance = this;
        gameObject.SetActive(false);
    }

    public static void Open()
    {
        Instance.gameObject.SetActive(true);
        Instance.InstantiatedItems.ForEach(item => Destroy(item));
        Instance.InstantiatedItems.Clear();
        Instance.ItemTemplate.SetActive(true);
        foreach (var item in Selectable.ActiveSelectables)
        {
            if (item.RelatedSelectables.Count == 0 || 
            item.RelatedSelectables[0] != item) 
                continue;

            int parents = 0;
            Transform parent = item.transform.parent;
            while (parent != null) 
            {
                parents++;
                parent = parent.parent;
            }

            var newItem = Instantiate(Instance.ItemTemplate, Instance.ItemTemplate.transform.parent);
            string title = "";
            for (int i = 0; i < parents; i++)
            {
                title += " ";
            }
            var metaData = item.GetMetadata();
            title += metaData.Name;
            if (metaData.IsSubSelectable) 
            {
                title += !string.IsNullOrWhiteSpace(metaData.SubPartName) ? 
                    $" ({metaData.SubPartName})" : $" (Unnamed Subpart)";
            }
            newItem.GetComponentInChildren<TextMeshProUGUI>().text = title;
            newItem.GetComponentInChildren<Button>().onClick.AddListener(() =>
            {
                Selectable.DeselectAll();
                item.Select();
                Instance.gameObject.SetActive(false);
            });
            Instance.InstantiatedItems.Add(newItem);
        }
        Instance.ItemTemplate.SetActive(false);
    }
}
