using System.Collections;
using UnityEngine;

public class DuplexWatcher : MonoBehaviour
{
    public string UIObjectName;
    public int redDuplextCount;
    private void OnEnable()
    {
        UI_ButtonDeleteObject.OnButtonAction += UpdateActiveState;
    }

    IEnumerator UpdateSelectablePrice()
    {

        yield return null;

        Selectable[] selectables = this.GetComponentsInChildren<Selectable>();
        SelectablePrice selectablePrice = this.GetComponentInChildren<SelectablePrice>();

        string boomObjectExcelName = "";
        redDuplextCount = 0;

        for (int i = 0; i < selectables.Length; i++)
        {
            if (selectables[i].MetaData.Name == "HV Power Outlet")
            {

                redDuplextCount++;
                Debug.Log("Duplex Count: "+selectables[i].gameObject.name +" "+redDuplextCount, selectables[i].gameObject);
            }
        }

        if (redDuplextCount <= 1)
        {
            if (selectablePrice)
                Destroy(selectablePrice);

        }

        else
        {
            if (redDuplextCount == 2)
            {
                Debug.Log("Red Duplex found");
                boomObjectExcelName = "Electrical (2 Duplex)";
                if (selectablePrice == null)
                {

                    AddSelectablePrice(selectables, boomObjectExcelName);
                }
                else if (selectablePrice.pricingObjectName != "Electrical (2 Duplex)")
                {
                    Destroy(selectablePrice);
                    AddSelectablePrice(selectables, boomObjectExcelName);
                }

            }
            else if (redDuplextCount == 3)
            {
                Debug.Log("Red Duplex found");
                boomObjectExcelName = "Electrical (3 Duplex)";

                if (selectablePrice == null)
                {

                    AddSelectablePrice(selectables, boomObjectExcelName);
                }
                else if (selectablePrice.pricingObjectName != "Electrical (2 Duplex)")
                {
                    Destroy(selectablePrice);
                    AddSelectablePrice(selectables, boomObjectExcelName);
                }
            }
        }
    }

    private void UpdateActiveState()
    {
        StartCoroutine(UpdateSelectablePrice());
    }

    void AddSelectablePrice(Selectable[] selectables, string boomObjectExcelName)
    {
        for (int i = 0; i < selectables.Length; i++)
        {
            if (selectables[i].MetaData.Name == "HV Power Outlet")
            {
                //selectables[i].AddComponent<SelectablePrice>();
                string excelFileName = DataFilePaths.sheetNameBoomIndividual;
                FindAnyObjectByType<ObjectMenu>(FindObjectsInactive.Include).AddSelectablePrice(selectables[i].gameObject, true, boomObjectExcelName, UIObjectName, excelFileName);
                break;
            }
        }
    }

    private void OnDisable()
    {
        UI_ButtonDeleteObject.OnButtonAction -= UpdateActiveState;
    }
}
