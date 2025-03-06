using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(FullScreenMenu))]
public class UI_PdfExportOptions : MonoBehaviour
{
    public static UI_PdfExportOptions Instance { get; private set; }

    [field: SerializeField]
    private TMP_InputField InputfieldTitle { get; set; }

    [field: SerializeField]
    private GameObject InputFieldAssemblyNameTemplate { get; set; }

    [field: SerializeField]
    private TMP_InputField InputfieldSubTitle { get; set; }

    [field: SerializeField]
    private TMP_InputField InputField_PdfData_Table
    { get; set; }

    [field: SerializeField]
    private TMP_InputField InputField_PdfData_Key
    { get; set; }

    [field: SerializeField]
    private TMP_InputField InputField_PdfData_Value
    { get; set; }

    [field: SerializeField]
    private GameObject Template_PdfData { get; set; }

    private Selectable _selectable;
    List<AssemblyData> _assemblyDatas = new();
    private List<GameObject> _instantiatedFieldAssemblyInputs = new();
    private List<GameObject> _instantiatedPdfDatas = new();

    public static List<AdditionalPdfData> GetAdditionalData()
    {
        List<AdditionalPdfData> datas = new();
        Instance._instantiatedPdfDatas.ForEach(x =>
        {
            var texts = x.GetComponentsInChildren
                <TextMeshProUGUI>();

            string table = texts[0].text;
            string key = texts[1].text;
            string value = texts[2].text;

            var existing = datas.FirstOrDefault(x => x.Table == table);

            if (existing == null)
            {
                existing = new() { Table = table };
                datas.Add(existing);
            }

            existing.Data.Add(key, value);
        });

        return datas;
    }

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(false);
        InputFieldAssemblyNameTemplate.SetActive(false);
        Template_PdfData.SetActive(false);
    }

    public void AddNewPdfData()
    {
        AddNewPdfData(
            InputField_PdfData_Table.text,
            InputField_PdfData_Key.text,
            InputField_PdfData_Value.text);
    }

    public void AddNewPdfData(string table, string key,
    string value)
    {
        Template_PdfData.SetActive(true);

        var newObj = Instantiate(Template_PdfData,
            Template_PdfData.transform.parent);

        var texts = newObj.GetComponentsInChildren
            <TextMeshProUGUI>();

        texts[0].text = table;
        texts[1].text = key;
        texts[2].text = value;

        newObj.GetComponentInChildren<Button>()
            .onClick.AddListener(() =>
            {
                _instantiatedPdfDatas.Remove(newObj);
                Destroy(newObj);
            });

        _instantiatedPdfDatas.Add(newObj);

        Template_PdfData.SetActive(false);
    }

    public void ExportPdf()
    {
        for (int i = 0; i < _assemblyDatas.Count; i++)
        {
            _assemblyDatas[i].Title = _instantiatedFieldAssemblyInputs[i]
                .GetComponentInChildren<TMP_InputField>().text;
        }

        _selectable.ExportElevationPdf(
            InputfieldTitle.text, 
            InputfieldSubTitle.text, 
            _assemblyDatas);

        gameObject.SetActive(false);
    }

    public static void Open(Selectable selectable)
    {
        Instance._selectable = selectable;
        if (Instance._selectable.TryGetArmAssemblyRoot(out GameObject rootObj))
        {
            Instance._assemblyDatas.Clear();

            var selectables = rootObj.GetComponentsInChildren<Selectable>().ToList();
            var orderedSelectables = selectables
                .OrderBy(x => x.transform.GetParentCount())
                .ToList();

            // orderedSelectables[0] should be the mount
            var mountData = orderedSelectables[0].GetMetadata();

            List<List<Selectable>> orderedSelectablesByMount = new();

            var allAttachmentPoints = orderedSelectables[0]
                .RelatedSelectables[0].GetComponentsInChildren<AttachmentPoint>();

            allAttachmentPoints = allAttachmentPoints
            .Where(x => 
            {
                if (x.MoveUpOnAttach && x.transform.childCount > 1)
                {
                    // This is an Arm Segment 1 multi-arm attach
                    return true;
                }

                return x == allAttachmentPoints[0] || 
                    x.transform.parent == allAttachmentPoints[0].transform.parent;
            }).ToArray();

            for (int i = 0; i < allAttachmentPoints.Length; i++)
            {
                var parentAttachmentPoint = allAttachmentPoints[i];
                List<Selectable> children = orderedSelectables
                    .Where(x =>
                    {
                        var parent = x.transform;
                        AttachmentPoint attach = null;

                        while (attach != parentAttachmentPoint)
                        {
                            parent = parent.parent;
                            if (parent == null) return false;
                            attach = parent.GetComponent<AttachmentPoint>();

                            if (attach != null && 
                            attach != parentAttachmentPoint && 
                            allAttachmentPoints.Contains(attach))
                            {
                                // part of a multi-arm mount, ignore
                                return false;
                            }
                        }
                        return true;
                    }).ToList();
                orderedSelectablesByMount.Add(children);
            }

            while (Instance._instantiatedFieldAssemblyInputs.Count < 
            orderedSelectablesByMount.Count)
            {
                var newObj = Instantiate(Instance.InputFieldAssemblyNameTemplate, 
                    Instance.InputFieldAssemblyNameTemplate.transform.parent);

                Instance._instantiatedFieldAssemblyInputs.Add(newObj);
            }

            Instance._instantiatedFieldAssemblyInputs.ForEach(x => x.SetActive(false));

            for (int i = 0; i < orderedSelectablesByMount.Count; i++)
            {
                var input = Instance._instantiatedFieldAssemblyInputs[i];
                input.SetActive(true);

                
                var reversed = new List<Selectable>(orderedSelectablesByMount[i]);
                reversed.Reverse();
                var titleObject = reversed.FirstOrDefault(x =>
                {
                    // last component with a z-rotation facing down is probably the head
                    bool hasGizmo = x.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z);
                    var angle = Vector3.Angle(x.transform.forward, Vector3.down);
                    return hasGizmo && angle < 5;
                });

                string title = $"Table {i + 1} Title";
                if (titleObject != default)
                {
                    titleObject = titleObject.RelatedSelectables[0];
                    string subTitle = titleObject.GetMetadata().Name;
                    title += $" ({subTitle})";

                    input.GetComponentInChildren<TMP_InputField>()
                        .text = subTitle;
                }
                input.GetComponentInChildren<TextMeshProUGUI>().text = title;
                Instance._assemblyDatas.Add(new AssemblyData() 
                { 
                    OrderedSelectables = orderedSelectablesByMount[i],
                });
            }
        }
        else
        {
            throw new System.Exception("Something went wrong. Couldn't export PDF");
        }
        Instance.gameObject.SetActive(true);
    }
}

public class AssemblyData
{
    public string Title { get; set; }
    public List<Selectable> OrderedSelectables { get; set; }
}

public class AdditionalPdfData
{
    public string Table { get; set; }
    public Dictionary<string, string> Data = new();
}