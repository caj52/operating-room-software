using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Room exports hub: one Export action per deliverable.
/// Object mode is folder settings only.
/// </summary>
[RequireComponent(typeof(FullScreenMenu))]
public class UI_ExportOptions : MonoBehaviour
{
    public static UI_ExportOptions Instance { get; private set; }

    private RectTransform _innerBox;
    private VerticalLayoutGroup _layout;
    private TMP_Text _titleLabel;
    private TMP_Text _infoLabel;

    private Toggle _togglePerAssembly;
    private GameObject _perAssemblyRow;

    private GameObject _rowObj;
    private GameObject _rowElevations;
    private GameObject _rowProposal;
    private GameObject _rowSnapshots;

    private Button _buttonExportTemplate;
    private Button _buttonCancel;
    private Button _buttonCustomizeObj;
    private Button _buttonChooseFolder;

    private ExportScope _scope = ExportScope.Room;
    private ObjExportOptions _objOptions = ObjExportOptions.CreateDefaults();
    private bool _objOptionsCustomized;
    private bool _wired;
    private bool _preserveChoicesOnNextOpen;
    private ExportScope? _forcedScopeOnOpen;
    private bool _showFolderChangedBanner;

    public static void Open()
    {
        try
        {
            if (!ExportPaths.EnsureRoomSavedForExport())
                return;

            EnsureInstance();
            if (!Instance._preserveChoicesOnNextOpen)
                Instance.ResetToDefaults();
            else
                Instance.RefreshChrome();

            Instance._preserveChoicesOnNextOpen = false;
            Instance.gameObject.SetActive(true);
            Instance.EnsureBlocksRaycasts();
            Instance.RebuildLayout();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open export options: {e}");
            UI_DialogPrompt.Open(
                "Could not open export options.\nCheck the console for details.",
                new ButtonAction("OK"));
        }
    }

    public static void Reopen()
    {
        if (Instance == null)
        {
            Open();
            return;
        }

        Instance._preserveChoicesOnNextOpen = true;
        Open();
    }

    public static void Close()
    {
        if (Instance != null)
        {
            Instance._showFolderChangedBanner = false;
            Instance.gameObject.SetActive(false);
        }
    }

    /// <summary>Toolbar one-click export — object 3D only (room uses per-deliverable buttons in this panel).</summary>
    public static ExportRequest GetRequestForToolbar()
    {
        if (ExportRequest.CurrentScope() != ExportScope.SelectedObject)
            return null;

        return ExportRequest.CreateDefaultsForSelection();
    }

    private static void EnsureInstance()
    {
        if (Instance != null)
            return;

        var prefab = Resources.Load<GameObject>("Prefabs/UI_ExportOptions");
        if (prefab == null)
            throw new Exception("Missing Resources/Prefabs/UI_ExportOptions.");

        var go = Instantiate(prefab);
        go.name = nameof(UI_ExportOptions);

        var exportOptions = go.GetComponent<UI_ExportOptions>();
        if (exportOptions == null)
            exportOptions = go.AddComponent<UI_ExportOptions>();

        if (go.GetComponent<FullScreenMenu>() == null)
            go.AddComponent<FullScreenMenu>();

        exportOptions.WireExistingChrome();
        go.SetActive(false);
        DontDestroyOnLoad(go);
        Instance = exportOptions;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void WireExistingChrome()
    {
        if (_wired)
            return;
        _wired = true;

        EnsureBlocksRaycasts();

        foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.text.Contains("OBJ Export") || text.text.Contains("Export Options")
                || text.text.Contains("Room Export") || text.text.Contains("Room Exports"))
            {
                _titleLabel = text;
                break;
            }
        }
        if (_titleLabel == null)
        {
            var texts = GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts.Length > 0)
                _titleLabel = texts[0];
        }

        _innerBox = _titleLabel != null
            ? _titleLabel.transform.parent as RectTransform
            : null;

        // Legacy include toggles become unused — hide them. Keep per-boom as a preference under elevations.
        HideToggle("Toggle_IncludeFloor");
        HideToggle("Toggle_IncludeFloorObjects");
        HideToggle("Toggle_IncludeCeiling");
        HideToggle("Toggle_IncludeCeilingObjects");
        HideToggle("Toggle_IncludeWalls");
        HideToggle("Toggle_IncludeArmAssemblies");
        HideToggle("Toggle_IncludeArmBoomHeads");

        _togglePerAssembly = FindToggle("Toggle_IncludeWallObjects");
        if (_togglePerAssembly == null)
            throw new Exception("Export options template is missing the per-boom toggle.");

        _perAssemblyRow = _togglePerAssembly.gameObject;
        HideNestedSeparators(_togglePerAssembly.transform);
        SetToggleLabel(_togglePerAssembly, "One PDF per boom");
        _togglePerAssembly.isOn = false;
        EnsurePreferredHeight(_perAssemblyRow, 22f);

        if (_titleLabel != null)
        {
            EnsurePreferredHeight(_titleLabel.gameObject, 28f);
            _titleLabel.fontSize = 20;
            _titleLabel.enableWordWrapping = false;
            _titleLabel.overflowMode = TextOverflowModes.Ellipsis;
            _infoLabel = CreateInfoLabel(_titleLabel);
        }

        WireButtons();
        BuildDeliverableRows();
        ConfigureInnerBoxLayout();
    }

    private void WireButtons()
    {
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            string n = button.gameObject.name;
            if (n.Contains("ExportScene") || n.Contains("ExportAll"))
                _buttonExportTemplate = button;
            else if (n.Contains("Cancel"))
                _buttonCancel = button;
            else if (n.Contains("ExportSelection") || n.Contains("Selection"))
                _buttonCustomizeObj = button;
        }

        if (_buttonExportTemplate != null)
        {
            _buttonExportTemplate.gameObject.SetActive(false);
            var ignore = _buttonExportTemplate.GetComponent<LayoutElement>()
                         ?? _buttonExportTemplate.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
        }

        if (_buttonCancel != null)
        {
            SetButtonLabel(_buttonCancel, "Done");
            _buttonCancel.onClick = new Button.ButtonClickedEvent();
            _buttonCancel.onClick.AddListener(Close);
            StyleActionButton(_buttonCancel, 40f);
        }

        // Full-width customize button is replaced by a compact control on the 3D row.
        if (_buttonCustomizeObj != null)
        {
            _buttonCustomizeObj.gameObject.SetActive(false);
            var ignore = _buttonCustomizeObj.GetComponent<LayoutElement>()
                         ?? _buttonCustomizeObj.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
        }

        if (_buttonChooseFolder == null)
        {
            var template = _buttonCancel ?? _buttonExportTemplate;
            if (template != null)
            {
                _buttonChooseFolder = CloneActionButton(template, "Button_ChooseExportFolder", "Change folder…");
                _buttonChooseFolder.onClick = new Button.ButtonClickedEvent();
                _buttonChooseFolder.onClick.AddListener(() =>
                {
                    FullRoomSave.OpenChooseExportFolderPrompt(changed =>
                    {
                        if (!changed)
                            return;

                        _showFolderChangedBanner = true;
                        RefreshChrome();
                        RebuildLayout();
                    });
                });
                StyleQuietButton(_buttonChooseFolder, 30f, -1f);
            }
        }
    }

    private void BuildDeliverableRows()
    {
        if (_innerBox == null || _buttonCancel == null)
            return;

        _rowObj = CreateDeliverableRow(
            "3D model",
            () => RunRoomExport(includeObj: true),
            includeCustomize: true);

        _rowElevations = CreateDeliverableRow(
            "Elevations",
            () => RunRoomExport(includeElevations: true),
            includeCustomize: false);

        _rowProposal = CreateDeliverableRow(
            "Sales proposal",
            () => RunRoomExport(includeProposal: true),
            includeCustomize: false);

        _rowSnapshots = CreateDeliverableRow(
            "Snapshots",
            () => RunRoomExport(includeSnapshots: true),
            includeCustomize: false);
    }

    private GameObject CreateDeliverableRow(string labelText, Action onExport, bool includeCustomize)
    {
        var row = new GameObject(
            "Row_" + labelText,
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        row.layer = gameObject.layer;
        row.transform.SetParent(_innerBox, false);

        var h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(4, 4, 0, 0);
        h.spacing = 6;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        var rowLe = row.GetComponent<LayoutElement>();
        rowLe.minHeight = 36f;
        rowLe.preferredHeight = 36f;
        rowLe.flexibleWidth = 1f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        labelGo.layer = gameObject.layer;
        labelGo.transform.SetParent(row.transform, false);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        if (_titleLabel is TextMeshProUGUI titleTmp)
        {
            label.font = titleTmp.font;
            label.fontSharedMaterial = titleTmp.fontSharedMaterial;
        }
        label.text = labelText;
        label.fontSize = 15;
        label.color = new Color(0.18f, 0.18f, 0.18f, 1f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var labelLe = labelGo.GetComponent<LayoutElement>();
        labelLe.preferredHeight = 32f;
        labelLe.flexibleWidth = 1f;
        labelLe.minWidth = 120f;

        if (includeCustomize)
        {
            var customizeBtn = CloneActionButton(_buttonCancel, "Button_CustomizeObjInline", "Options");
            customizeBtn.transform.SetParent(row.transform, false);
            customizeBtn.gameObject.SetActive(true);
            StyleQuietButton(customizeBtn, 30f, 72f);
            customizeBtn.onClick = new Button.ButtonClickedEvent();
            customizeBtn.onClick.AddListener(OpenCustomizeObj);
            _buttonCustomizeObj = customizeBtn;
        }

        var exportBtn = CloneActionButton(_buttonCancel, "Button_ExportDeliverable", "Export");
        exportBtn.transform.SetParent(row.transform, false);
        exportBtn.gameObject.SetActive(true);
        StyleActionButton(exportBtn, 32f);
        var eLe = exportBtn.GetComponent<LayoutElement>() ?? exportBtn.gameObject.AddComponent<LayoutElement>();
        eLe.preferredWidth = 88f;
        eLe.minWidth = 88f;
        eLe.preferredHeight = 32f;
        eLe.flexibleWidth = 0f;
        exportBtn.onClick = new Button.ButtonClickedEvent();
        exportBtn.onClick.AddListener(() => onExport?.Invoke());

        return row;
    }

    private void OpenCustomizeObj()
    {
        if (_scope != ExportScope.Room)
            return;

        Close();
        UI_ObjExportOptions.OpenForCustomization(opts =>
        {
            _objOptions = opts;
            _objOptionsCustomized = true;
            _forcedScopeOnOpen = ExportScope.Room;
            Reopen();
        });
    }

    private void RunRoomExport(
        bool includeObj = false,
        bool includeElevations = false,
        bool includeProposal = false,
        bool includeSnapshots = false)
    {
        Close();
        ExportOrchestrator.Run(new ExportRequest
        {
            Scope = ExportScope.Room,
            IncludeObj = includeObj,
            IncludeElevations = includeElevations,
            IncludeProposal = includeProposal,
            IncludeSnapshots = includeSnapshots,
            ElevationMode = _togglePerAssembly != null && _togglePerAssembly.isOn
                ? ElevationExportMode.PerAssembly
                : ElevationExportMode.CombinedRoom,
            ObjOptions = _objOptions ?? ObjExportOptions.CreateDefaults()
        });
    }

    private void ConfigureInnerBoxLayout()
    {
        if (_innerBox == null)
            return;

        var bg = _innerBox.Find("Bg");
        if (bg != null)
        {
            var ignore = bg.GetComponent<LayoutElement>() ?? bg.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            var bgRt = bg as RectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
        }

        _layout = _innerBox.GetComponent<VerticalLayoutGroup>();
        if (_layout == null)
            _layout = _innerBox.gameObject.AddComponent<VerticalLayoutGroup>();

        _layout.enabled = true;
        _layout.padding = new RectOffset(24, 24, 18, 18);
        _layout.spacing = 6;
        _layout.childAlignment = TextAnchor.UpperCenter;
        _layout.childControlWidth = true;
        _layout.childControlHeight = true;
        _layout.childForceExpandWidth = true;
        _layout.childForceExpandHeight = false;

        var fitter = _innerBox.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.enabled = false;

        _innerBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 440f);
        _innerBox.localScale = Vector3.one * 1.2f;
    }

    private TMP_Text CreateInfoLabel(TMP_Text styleSource)
    {
        var go = new GameObject("InfoLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.layer = gameObject.layer;
        go.transform.SetParent(styleSource.transform.parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = styleSource.font;
        text.fontSharedMaterial = styleSource.fontSharedMaterial;
        text.fontSize = 12;
        text.color = new Color(0.35f, 0.35f, 0.35f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.lineSpacing = -20f;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 18f;
        le.preferredHeight = 22f;
        le.flexibleWidth = 1f;

        return text;
    }

    private Button CloneActionButton(Button template, string name, string label)
    {
        var go = Instantiate(template.gameObject, template.transform.parent);
        go.name = name;
        var button = go.GetComponent<Button>();
        SetButtonLabel(button, label);
        StyleActionButton(button, 40f);
        go.SetActive(false);
        return button;
    }

    private static void StyleActionButton(Button button, float height)
    {
        if (button == null)
            return;
        EnsurePreferredHeight(button.gameObject, height);
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSize = 15;
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private static void StyleQuietButton(Button button, float height, float width)
    {
        if (button == null)
            return;

        StyleActionButton(button, height);
        var img = button.GetComponent<Image>();
        if (img != null)
            img.color = new Color(1f, 1f, 1f, 0.15f);

        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSize = 13;
            label.color = new Color(0.3f, 0.35f, 0.45f, 1f);
        }

        var le = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        if (width > 0f)
        {
            le.preferredWidth = width;
            le.minWidth = width;
            le.flexibleWidth = 0f;
        }
        else
        {
            le.flexibleWidth = 1f;
        }
    }

    private void EnsureBlocksRaycasts()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        foreach (var t in GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = uiLayer;

        var rootImage = GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.raycastTarget = true;
            if (rootImage.color.a < 0.3f)
            {
                var c = rootImage.color;
                c.a = 0.61f;
                rootImage.color = c;
            }
        }

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
            canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 299);
    }

    private void ResetToDefaults()
    {
        if (!_wired)
            WireExistingChrome();

        _scope = _forcedScopeOnOpen ?? ExportRequest.CurrentScope();
        _forcedScopeOnOpen = null;
        _showFolderChangedBanner = false;

        if (_scope == ExportScope.SelectedObject)
        {
            RefreshChrome();
            return;
        }

        if (!_objOptionsCustomized)
            _objOptions = ObjExportOptions.CreateDefaults();

        RefreshChrome();
    }

    private void RefreshChrome()
    {
        if (!_wired)
            WireExistingChrome();

        if (_forcedScopeOnOpen.HasValue)
        {
            _scope = _forcedScopeOnOpen.Value;
            _forcedScopeOnOpen = null;
        }
        else if (!_preserveChoicesOnNextOpen)
        {
            _scope = ExportRequest.CurrentScope();
        }

        bool objectMode = _scope == ExportScope.SelectedObject;

        if (_titleLabel != null)
            _titleLabel.text = objectMode ? "Export Folder" : "Room Exports";

        UpdateInfoText(objectMode);

        SetActive(_rowObj, !objectMode);
        SetActive(_rowElevations, !objectMode);
        SetActive(_rowProposal, !objectMode);
        SetActive(_rowSnapshots, !objectMode);
        SetActive(_perAssemblyRow, !objectMode);

        if (_buttonCustomizeObj != null)
            _buttonCustomizeObj.gameObject.SetActive(!objectMode);

        if (_buttonChooseFolder != null)
            _buttonChooseFolder.gameObject.SetActive(true);

        if (_buttonCancel != null)
        {
            _buttonCancel.gameObject.SetActive(true);
            SetButtonLabel(_buttonCancel, "Done");
        }
    }

    private void UpdateInfoText(bool objectMode)
    {
        if (_infoLabel == null)
            return;

        string path = ShortenPath(ExportPaths.GetExportBasePath());
        string body;
        if (objectMode)
        {
            body = path;
        }
        else
        {
            body = _objOptionsCustomized
                ? path + "  ·  3D options customized"
                : path;
        }

        _infoLabel.text = _showFolderChangedBanner
            ? "New export folder set…  " + body
            : body;

        _infoLabel.ForceMeshUpdate();
        var le = _infoLabel.GetComponent<LayoutElement>();
        if (le != null)
            le.preferredHeight = Mathf.Clamp(_infoLabel.preferredHeight + 2f, 18f, 48f);
    }

    private void RebuildLayout()
    {
        if (_innerBox == null)
            return;

        var order = new List<Transform>();
        void Add(Component c)
        {
            if (c != null && c.gameObject.activeSelf)
                order.Add(c.transform);
        }
        void AddGo(GameObject go)
        {
            if (go != null && go.activeSelf)
                order.Add(go.transform);
        }

        Add(_titleLabel);
        Add(_infoLabel);
        Add(_buttonChooseFolder);
        AddGo(_rowObj);
        AddGo(_rowElevations);
        AddGo(_perAssemblyRow);
        AddGo(_rowProposal);
        AddGo(_rowSnapshots);
        Add(_buttonCancel);

        for (int i = 0; i < order.Count; i++)
            order[i].SetSiblingIndex(i + 1);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerBox);

        float contentHeight = 0f;
        if (_layout != null)
        {
            contentHeight = _layout.padding.top + _layout.padding.bottom;
            int visible = 0;
            for (int i = 0; i < order.Count; i++)
            {
                var rt = order[i] as RectTransform;
                if (rt == null)
                    continue;
                contentHeight += LayoutUtility.GetPreferredHeight(rt);
                visible++;
            }
            if (visible > 1)
                contentHeight += _layout.spacing * (visible - 1);
        }
        else
        {
            contentHeight = LayoutUtility.GetPreferredHeight(_innerBox);
        }

        _innerBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 440f);
        _innerBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(contentHeight, 100f));
        _innerBox.localScale = Vector3.one * 1.2f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerBox);
    }

    private Toggle FindToggle(string objectName)
    {
        foreach (var toggle in GetComponentsInChildren<Toggle>(true))
        {
            if (toggle.gameObject.name == objectName)
                return toggle;
        }
        return null;
    }

    private void HideToggle(string objectName)
    {
        var toggle = FindToggle(objectName);
        if (toggle != null)
        {
            toggle.gameObject.SetActive(false);
            var ignore = toggle.GetComponent<LayoutElement>() ?? toggle.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
        }
    }

    private static void HideNestedSeparators(Transform root)
    {
        foreach (Transform child in root)
        {
            if (child.name.StartsWith("separator", StringComparison.OrdinalIgnoreCase))
                child.gameObject.SetActive(false);
        }
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null)
            go.SetActive(active);
    }

    private static void EnsurePreferredHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents) &&
            path.StartsWith(documents, StringComparison.OrdinalIgnoreCase))
        {
            return "Documents" + path.Substring(documents.Length);
        }

        if (path.Length > 64)
            return "…" + path.Substring(path.Length - 60);

        return path;
    }

    private static void SetToggleLabel(Toggle toggle, string text)
    {
        if (toggle == null)
            return;
        var label = toggle.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
            return;

        label.text = text;
        label.fontSize = 12;
        label.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        label.enableAutoSizing = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void SetButtonLabel(Button button, string text)
    {
        if (button == null)
            return;
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = text;
    }
}
