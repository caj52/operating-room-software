using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_MaterialPallete : MonoBehaviour
{
    public GameObject materialPrefab;
    [field: SerializeField] public List<UI_MaterialPalleteSwatch> pallete { get; private set; }

    public void LoadPalleteOptions(Material[] materials, int element)
    {
        ClearPalleteOptions();

        foreach(Material material in materials)
        {
            UI_MaterialPalleteSwatch swatch = Instantiate(materialPrefab, this.gameObject.transform).GetComponent<UI_MaterialPalleteSwatch>();
            swatch.AssignMaterialToSwatch(material, element);
            pallete.Add(swatch);
        }
    }

    public void ClearPalleteOptions()
    {
        foreach(UI_MaterialPalleteSwatch swatch in pallete)
        {
          // swatch.gameObject.SetActive(false);
            Destroy(swatch.gameObject);
        }

        pallete.Clear();
        pallete.TrimExcess();
    }
}
