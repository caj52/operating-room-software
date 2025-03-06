using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

internal class UI_ObjectEditor : MonoBehaviour
{
    #region Fields/Properties

    public static UI_ObjectEditor Instance { get; private set; }

    [field: SerializeField]
    private TextMeshProUGUI Title { get; set; }

    [field: SerializeField]
    private GameObject AttachmentPointBox { get; set; }
 
    [field: SerializeField]
    private TMP_InputField InputField_ObjectName { get; set; }

    [field: SerializeField]
    private Toggle Toggle_IsEnabled { get; set; }

    [field: SerializeField]
    private TMP_InputField InputField_NewKeyword { get; set; }

    [field: SerializeField]
    private TMP_InputField InputField_NewCategory { get; set; }

    [field: SerializeField]
    private TMP_InputField
    InputField_NewCategoryAttachmentPoint { get; set; }

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
    private GameObject Template_Keyword { get; set; }

    [field: SerializeField]
    private GameObject Template_Category { get; set; }

    [field: SerializeField]
    private GameObject Template_CategoryAttachmentPoint 
    { get; set; }

    [field: SerializeField]
    private GameObject Template_CompatibleObject { get; set; }

    [field: SerializeField]
    private GameObject Template_PdfData { get; set; }

    [field: SerializeField]
    private Button ButtonAddPdfData { get; set; }

    private UnityEventManager _eventManager = new();
    private SelectableMetaData _activeMetaData;
    private List<GameObject> _instantiatedKeywords = new();
    private List<GameObject> _instantiatedCategories = new();
    private List<GameObject> _instantiatedCategoriesAttachmentPoint = new();
    private List<GameObject> _instantiatedPdfDatas = new();

    private List<GameObject> 
    _instantiatedAssetBundleNamesAttachmentPoint = new();

    #endregion

    #region Methods

    #region Monobehaviour

    private void Awake()
    {
        _eventManager.RegisterEvents
            ((ObjectMenu.LastOpenedSelectableChanged, UpdateState),
            (Selectable.ActiveSelectablesInSceneChanged, UpdateState),
            (AttachmentPoint.SelectedAttachmentPointChanged, UpdateState));

        _eventManager.RegisterEvents
            ((InputField_PdfData_Table.onValueChanged, UpdateAddPdfDataButton), 
            (InputField_PdfData_Key.onValueChanged, UpdateAddPdfDataButton), 
            (InputField_PdfData_Value.onValueChanged, UpdateAddPdfDataButton));

        _eventManager.AddListeners();

        Template_Keyword.SetActive(false);
        Template_Category.SetActive(false);
        Template_CategoryAttachmentPoint.SetActive(false);
        Template_CompatibleObject.SetActive(false);
        Template_PdfData.SetActive(false);

        gameObject.SetActive(false);

        Instance = this;
    }

    private void OnDestroy()
    {
        _eventManager.RemoveListeners();
    }

    #endregion

    private void UpdateAddPdfDataButton(string text = null)
    {
        bool interactable = 
            !string.IsNullOrWhiteSpace(InputField_PdfData_Table.text) && 
            !string.IsNullOrWhiteSpace(InputField_PdfData_Key.text) && 
            !string.IsNullOrWhiteSpace(InputField_PdfData_Value.text);

        ButtonAddPdfData.interactable = interactable;
    }

    public static async void AddCompatibleObject(string assetBundleName)
    {
        var task = Instance.AddNewAttachmentPointObject(assetBundleName);
        await task;

        if (!Application.isPlaying)
            throw new Exception($"App quit while in task");

        Instance.OpenObjectMenuForAttachmentPointObject();
    }

    private void UpdateState()
    {
        Selectable selectable = ObjectMenu.LastOpenedSelectable;

        bool active = selectable != null && 
            !selectable.IsDestroyed && 
            Selectable.ActiveSelectables.Count > 0;

        bool wasActive = gameObject.activeSelf;

        if ((active && !gameObject.activeSelf) || 
        (!active && gameObject.activeSelf))
            gameObject.SetActive(active);

        AttachmentPointBox.SetActive
            (AttachmentPoint.SelectedAttachmentPoint != null);

        if (active && wasActive)
        {
            RefreshValues();
        }

        if (!active || wasActive) return;

        GetMetaData();
    }

    private async void GetMetaData()
    {
        Selectable selectable = ObjectMenu.LastOpenedSelectable;

        string assetBundleName = ObjectMenu
                .LastOpenedSelectableData.AssetBundleName;

        var task = Database.GetMetaData
            (assetBundleName, selectable.MetaData);

        await task;

        if (!Application.isPlaying)
            throw new Exception($"App quit during async task");

        if (task.Result.ResultType != Database.MetaDataOpertaionResultType.Success)
        {
            UI_DialogPrompt.Open(
                $"Error: {task.Result.ErrorMessage}");

            return;
        }

        //populate UI
        _activeMetaData = task.Result.MetaData;

        RefreshValues();
    }

    private void RefreshValues()
    {
        Title.text = _activeMetaData.Name;
        InputField_ObjectName.text = _activeMetaData.Name;

        _instantiatedKeywords
            .ForEach(x => Destroy(x));

        _instantiatedCategories
            .ForEach(x => Destroy(x));

        _instantiatedPdfDatas
            .ForEach(x => Destroy(x));

        _instantiatedKeywords.Clear();
        _instantiatedCategories.Clear();
        _instantiatedPdfDatas.Clear();

        _activeMetaData.KeyWords
            .ForEach(x => AddNewKeyword(x));

        _activeMetaData.Categories
            .ForEach(x => AddNewCategory(x));

        _activeMetaData.PdfData
            .ForEach(x => AddNewPdfData(x.Table, x.Key, x.Value));

        Toggle_IsEnabled.SetIsOnWithoutNotify(_activeMetaData.Enabled);

        InputField_NewKeyword.text = string.Empty;
        InputField_NewCategory.text = string.Empty;
        InputField_NewCategoryAttachmentPoint.text = string.Empty;
        InputField_PdfData_Key.text = string.Empty;
        InputField_PdfData_Value.text = string.Empty;
        InputField_PdfData_Table.text = string.Empty;

        if (AttachmentPoint.SelectedAttachmentPoint != null)
        {
            _instantiatedCategoriesAttachmentPoint
                .ForEach(x => Destroy(x));

            _instantiatedCategoriesAttachmentPoint.Clear();

            _instantiatedAssetBundleNamesAttachmentPoint
                .ForEach(x => Destroy(x));

            _instantiatedAssetBundleNamesAttachmentPoint.Clear();

            var apMetaData = GetAttachmentPointMetaData();

            apMetaData.MetaData
                .AllowedSelectableCategories
                .ForEach(item => AddNewCategoryAttachmentPoint(item));

            apMetaData.MetaData.AllowedSelectableAssetBundleNames.ForEach(item => 
            { 
                var task = AddNewAttachmentPointObject(item);
            });
        }
    }

    //private void HandleList(TextMeshProUGUI inputField, 
    //List<string> list, string name)
    //{
    //    if (list.Count == 0)
    //    {
    //        inputField.text = $"No {name}";
    //    }
    //    else
    //    {
    //        var stringBuilder = new StringBuilder();
    //        list.ForEach(word =>
    //        {
    //            stringBuilder.AppendLine(word);
    //        });
    //        inputField.text = stringBuilder.ToString();
    //    }
    //}

    private void UpdateMetaData()
    {
        _activeMetaData.Name = InputField_ObjectName.text;

        _activeMetaData.KeyWords = _instantiatedKeywords
            .Select(x => x.GetComponentInChildren<TextMeshProUGUI>().text)
            ?.ToList() ?? new List<string>();

        _activeMetaData.Categories = _instantiatedCategories
            .Select(x => x.GetComponentInChildren<TextMeshProUGUI>().text)
            ?.ToList() ?? new List<string>();

        _activeMetaData.PdfData = _instantiatedPdfDatas
            .Select(x =>
            {
                var texts = x.GetComponentsInChildren<TextMeshProUGUI>();
                return new PdfData
                {
                    Table = texts[0].text,
                    Key = texts[1].text,
                    Value = texts[2].text
                };
            })
            ?.ToList() ?? new List<PdfData>();

        _activeMetaData.Enabled = Toggle_IsEnabled.isOn;

        if (AttachmentPoint.SelectedAttachmentPoint != null)
        {
            var apMetaData = GetAttachmentPointMetaData();

            List<string> newCategories = _instantiatedCategoriesAttachmentPoint
                .Select(x => x.GetComponentInChildren<TextMeshProUGUI>().text)
                .ToList();

            List<string> newAssetNames = _instantiatedAssetBundleNamesAttachmentPoint
                .Select(x => x.GetComponentInChildren<TextMeshProUGUI>().text)
                .ToList();

            apMetaData.MetaData.AllowedSelectableCategories = newCategories;
            apMetaData.MetaData.AllowedSelectableAssetBundleNames = newAssetNames;
        }
    }

    private AttachmentPointGuidMetaData GetAttachmentPointMetaData()
    {
        var guid = AttachmentPoint.SelectedAttachmentPoint.MetaData.Guid;

        Selectable.AttachmentPointData data = ObjectMenu
            .LastOpenedSelectable
            .AttachmentPointDatas
            .First(x => x.Guid == guid);

        var existing = _activeMetaData
            .AttachmentPointGuidMetaData
            .FirstOrDefault(x => x.Guid == guid);

        if (existing == default)
        {
            existing = new AttachmentPointGuidMetaData
            {
                Guid = guid,
                MetaData = new AttachmentPointMetaData
                {
                    Guid = guid,
                    AllowedSelectableCategories = new(),
                    AllowedSelectableAssetBundleNames = new()
                }
            };

            _activeMetaData.AttachmentPointGuidMetaData.Add(existing);
        }

        return existing;
    }

    public void DeleteSelectables()
    {
        Destroy(ObjectMenu.LastOpenedSelectable.gameObject);
    }

    public async void Save()
    {
        UpdateMetaData();

        string assetBundleName = ObjectMenu
            .LastOpenedSelectableData.AssetBundleName;

        var task = Database.SaveMetaData(assetBundleName, _activeMetaData);

        await task;

        if (!Application.isPlaying)
            throw new Exception("App closed during async task");

        if (task.Result.ResultType == Database.MetaDataOpertaionResultType.Success)
        {
            Debug.Log("Saved data successfully");

            UI_DialogPrompt.Open(
                "Saved data successfully");

            Title.text = _activeMetaData.Name;

            ObjectMenu.Regenerate();
        }
        else
        {
            Debug.LogError("Could not save data to the server");

            UI_DialogPrompt.Open(
                $"An error occurred: {task.Result.Message}. Data was not saved.");
        }
    }

    public void OpenObjectMenuForAttachmentPointObject()
    {
        var list = _instantiatedAssetBundleNamesAttachmentPoint
            .Select(x => x.GetComponentInChildren<TextMeshProUGUI>().text)
            .ToList();

        ObjectMenu.OpenToSelectCompatibleObjects(list);
    }

    public void AddNewKeyword()
    {
        AddNewItem(Template_Keyword, 
            InputField_NewKeyword, 
            _instantiatedKeywords,
            null);
    }

    public async Task AddNewAttachmentPointObject(string assetBundleName)
    {
        SelectableData selectableData = SelectableAssetBundles
            .GetSelectableData()
            .First(x => x.AssetBundleName == assetBundleName);

        var task = Database.GetMetaData(assetBundleName, selectableData.MetaData);
        await task;

        if (!Application.isPlaying) 
            throw new Exception("Application quit during task");

        if (task.Result.ResultType != Database.MetaDataOpertaionResultType.Success)
        {
            UI_DialogPrompt.Open(
                $"An error occurred: {task.Result.ErrorMessage}. Could not fetch metadata. Please check your internet connection and restart the app.");

            return;
        }

        var metaData = task.Result.MetaData;

        string objName = metaData.Name;

        AddNewItem(
            Template_CompatibleObject, 
            null, 
            _instantiatedAssetBundleNamesAttachmentPoint, 
            objName, 
            assetBundleName);
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

    public void AddNewCategoryAttachmentPoint()
    {
        AddNewItem(Template_CategoryAttachmentPoint,
            InputField_NewCategoryAttachmentPoint,
            _instantiatedCategoriesAttachmentPoint,
            null);
    }

    private void AddNewCategoryAttachmentPoint(string category)
    {
        AddNewItem(Template_CategoryAttachmentPoint,
            InputField_NewCategoryAttachmentPoint,
            _instantiatedCategoriesAttachmentPoint,
            category);
    }

    private void AddNewKeyword(string newKeyword)
    {
        AddNewItem(Template_Keyword, 
            InputField_NewKeyword, 
            _instantiatedKeywords, 
            newKeyword);
    }

    public void AddNewCategory()
    {
        AddNewItem(Template_Category,
            InputField_NewCategory, 
            _instantiatedCategories, 
            null);
    }

    private void AddNewCategory(string categoryName)
    {
        AddNewItem(Template_Category, 
            InputField_NewCategory, 
            _instantiatedCategories, 
            categoryName);
    }

    private void AddNewItem(GameObject template, 
    TMP_InputField inputField, 
    List<GameObject> instantiatedItems, 
    string newItem, string assetBundleName = null)
    {
        newItem ??= inputField.text;

        if (string.IsNullOrWhiteSpace(newItem))
        {
            Debug.Log($"New Item is null {newItem}, {inputField.text}");
            return;
        }

        //newItem = newItem.ToLower();

        string text1 = assetBundleName ?? newItem;

        bool exists = instantiatedItems
            .Select(x => x.GetComponentInChildren<TextMeshProUGUI>().text)
            .Any(x => string.Compare(x, newItem, true) == 0);

        if (exists)
        {
            UI_DialogPrompt.Open($"Item \"{newItem}\" already exists.");
            return;
        }

        template.SetActive(true);

        var newObj = Instantiate(
            template,
            template.transform.parent);

        template.SetActive(false);

        newObj.GetComponentInChildren
            <TextMeshProUGUI>().text = text1;

        if (assetBundleName != null)
        {
            newObj.GetComponentsInChildren<TextMeshProUGUI>()[1].text = newItem;
        }
        
        int sibIndex = instantiatedItems.Count > 0 ? 
            instantiatedItems.Last()
            .transform.GetSiblingIndex() + 1 : 
            template.transform.GetSiblingIndex() + 1;

        newObj.transform.SetSiblingIndex(sibIndex);

        instantiatedItems.Add(newObj);

        newObj.GetComponentInChildren<Button>()
            .onClick.AddListener(() =>
            {
                instantiatedItems.Remove(newObj);
                Destroy(newObj);
            });

        if (inputField != null) 
        {
            inputField.text = string.Empty;
        }
    }
    #endregion
}