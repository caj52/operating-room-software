using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RecordHirarcheySelectables : MonoBehaviour
{
    [SerializeField]
    public List<AttachedSelectables> attachedSelectables= new List<AttachedSelectables>();

    public List<AttachedSelectables> attachedSelectables2 = new List<AttachedSelectables>();

    public GameObject tandemMount1;
    public GameObject tandemMount2;

    public void AddAttachedSelectables(Selectable selectable, string objectName)
    {
        if(objectName == "Tandem Mount")//Tandem Mount has two attachment points so it have specific condition.
        {
            if (selectable.AttachmentPointDatas.Count == 2)
            {
                tandemMount1 = selectable.AttachmentPointDatas[0].AttachmentPoint.gameObject;
                tandemMount2 = selectable.AttachmentPointDatas[1].AttachmentPoint.gameObject;
                //selectable.gameObject.AddComponent<SelectablePrice>();
                //var selectablePrice = selectable.gameObject.AddComponent<SelectablePrice>();
                //selectablePrice.pricingObjectName = objectName;
                //string boomExcelFileName = DataFilePaths.ExcelFileBoomPricingSheet;
                //Debug.Log("Boom Object found");
                //selectablePrice.GetPricingDataFromExcel(boomExcelFileName);
            }
        }
        else
        {
            if (tandemMount1 == null && tandemMount2 == null)//normal Object
            {
                AttachedSelectables attachedSelectable = new AttachedSelectables();
                attachedSelectable.gameObject = selectable.gameObject;
                attachedSelectable.btName = objectName;
                attachedSelectables.Add(attachedSelectable);

                Debug.LogError("==" + objectName);

                // Define length options
                List<float> standardTopArmLengths = new List<float> { 0.6f, 0.8f, 1f, 1.2f };
                List<float> xlTopArmLengths = new List<float> { 0.6f, 0.8f, 1f, 1.2f, 1.4f, 1.6f };
                List<float> standardBottomArmLengths = new List<float> { 1.0f }; // Articulating default
                List<float> fixedBottomArmLengths = new List<float> { 0.6f, 0.8f, 1f, 1.2f, 1.4f, 1.6f };

                // Utility to apply scale
                void ApplyToArm(string armKeyword, List<float> scales)
                {
                    var arm = attachedSelectables.Where(a => a.gameObject.name.Contains(armKeyword)).FirstOrDefault();
                    if (arm == null) return;

                    var selectables = arm.gameObject.GetComponentsInChildren<Selectable>().Where(x => x.ScaleLevels.Count > 0);
                    var target = selectables.FirstOrDefault();
                    if (target != null)
                    {
                        Debug.LogError("Applying scale to " + armKeyword + ": " + target.name);
                        ApplyScaleFilter(scales, target);
                    }
                }

                if (objectName.Contains("Fixed"))
                {
                    ApplyToArm("TopArm", objectName.Contains("XL") ? xlTopArmLengths : standardTopArmLengths);
                    ApplyToArm("BottomArm", fixedBottomArmLengths);
                }
                else if (objectName.Contains("Powered") || objectName.Contains("Spring"))
                {
                    ApplyToArm("TopArm", objectName.Contains("XL") ? xlTopArmLengths : standardTopArmLengths);
                    ApplyToArm("BottomArm", standardBottomArmLengths); // Articulating 1000mm
                }

            }
            else
            {
                GameObject selectableGo = selectable.gameObject;
                bool isFirstTandemChild = selectableGo.transform.IsChildOf(tandemMount1.transform);
                if (isFirstTandemChild)
                {
                    AttachedSelectables attachedSelectable = new AttachedSelectables();
                    attachedSelectable.gameObject = selectable.gameObject;
                    attachedSelectable.btName = objectName;
                    attachedSelectables.Add(attachedSelectable);
                }
                else
                {
                    bool isSecondTandemChild = selectableGo.transform.IsChildOf(tandemMount2.transform);
                    if (isSecondTandemChild)
                    {
                        AttachedSelectables attachedSelectable = new AttachedSelectables();
                        attachedSelectable.gameObject = selectable.gameObject;
                        attachedSelectable.btName = objectName;
                        attachedSelectables2.Add(attachedSelectable);
                    }
                }
                 
            }
        }
    }



    private void ApplyScaleFilter(List<float> allowedScales, Selectable selectable)
    {
        try
        {
            if (selectable == null)
            {
                Debug.LogWarning("Cannot apply scale filter to null selectable", this);
                return;
            }

            if (selectable.ScaleLevels == null)
            {
                Debug.LogWarning($"ScaleLevels is null on {selectable.name}", this);
                return;
            }

            if (selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"ScaleLevels is empty on {selectable.name}", this);
                return;
            }

            // Log before filtering
            Debug.Log($"Before filtering: {selectable.name} has {selectable.ScaleLevels.Count} scale levels: {string.Join(", ", selectable.ScaleLevels.Select(l => l.Size))}", this);

            // Create a new filtered list to avoid modifying during enumeration
            var filteredScales = selectable.ScaleLevels
                .Where(level => level != null && allowedScales.Contains(level.Size))
                .ToList();

            // Assign the filtered list
            selectable.ScaleLevels = filteredScales;

            // Log after filtering
            Debug.Log($"After filtering: {selectable.name} has {selectable.ScaleLevels.Count} scale levels: {string.Join(", ", selectable.ScaleLevels.Select(l => l.Size))}", this);
            Debug.Log($"Applied scale filter to {selectable.name} - Allowed scales: {string.Join(", ", allowedScales)}", this);

            if (selectable.ScaleLevels.Count == 0)
            {
                Debug.LogWarning($"WARNING: Filtering resulted in zero scale levels for {selectable.name}!", this);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            Debug.LogError($"Error applying scale filter to {selectable?.name}: {e.Message}", this);
        }
    }

}

[Serializable]
public class AttachedSelectables
{
    public GameObject gameObject;
    public string btName;
    ExcelKeys[] excelKeysAll = new ExcelKeys[6];
    public void ExcelCompatibleName()
    {


        ExcelKeys excelKeys1 = new ExcelKeys("Boom Bottom Arm - Powered", "Powered Boom");
        ExcelKeys excelKeys2 = new ExcelKeys("Boom Bottom Arm - Spring", "Spring Boom");
        ExcelKeys excelKeys3 = new ExcelKeys("Boom Bottom Arm - Fixed", "Fixed Boom");
        ExcelKeys excelKeys4 = new ExcelKeys("Boom Drop Tube", "Ceiling Flange");
        ExcelKeys excelKeys5 = new ExcelKeys("Boom Top Arm(XL)", "XL Top");
        ExcelKeys excelKeys6 = new ExcelKeys("Boom Column Tube", "Ceiling Flange");

        excelKeysAll[0] = excelKeys1;
        excelKeysAll[1] = excelKeys2;
        excelKeysAll[2] = excelKeys3;
        excelKeysAll[3] = excelKeys4;
        excelKeysAll[4] = excelKeys5;
        excelKeysAll[5] = excelKeys6;
        

        //ExcelKeys excelKeys7 = new ExcelKeys("Boom Top Arm(XL)", "Bottom Arm");

        //Boom Drop Tube
        //Boom Column Tube



    }

    public string GetExcelName()
    {
        for (int i = 0; i < excelKeysAll.Length; i++)
        {
            if (excelKeysAll[i].uiBtnName == btName)
            {
                return excelKeysAll[i].excelKey;
            }
            
        }
        Debug.Log("Excel Key not found");
        return null;
    }

}

public class ExcelKeys 
{
    public string excelKey;
    public string uiBtnName;

    public ExcelKeys(string uiBtnName, string excelKey)
    {
        this.excelKey = excelKey;
        this.uiBtnName = uiBtnName;
    }
}