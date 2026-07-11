using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Export options menu — reuses OBJ-options chrome, laid out with a real
/// VerticalLayoutGroup so text/controls never overlap.
/// </summary>
[RequireComponent(typeof(FullScreenMenu))]
public class UI_ExportOptions : MonoBehaviour
{
    public static UI_ExportOptions Instance { get; private set; }

    private RectTransform _innerBox;
    private VerticalLayoutGroup _layout;
    private TMP_Text _titleLabel;
    private TMP_Text _infoLabel;
    private Toggle _toggleObj;
    private Toggle _toggleElevations;
    private Toggle _toggleProposal;
    private Toggle _toggleSnapshots;
    private Toggle _togglePerAssembly;
    private Toggle _toggleAdvanced;
    private GameObject _objRow;
    private GameObject _elevationsRow;
    private GameObject _proposalRow;
    private GameObject _snapshotsRow;
    private GameObject _perAssemblyRow;
    private GameObject _advancedRow;
    private Button _buttonExport;
    private Button _buttonCancel;
    private Button _buttonCustomizeObj;
    private Button _buttonChooseFolder;
    private ExportScope _scope = ExportScope.Room;
    private ObjExportOptions _objOptions = ObjExportOptions.CreateDefaults();
    private bool _objOptionsCustomized;
    private bool _wired;
    private bool _preserveChoicesOnNextOpen;
    private bool _hasRoomChoices;
    private ExportScope? _forcedScopeOnOpen;

    public static void Open()
    {
        try
        {
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
            Instance.gameObject.SetActive(false);
    }

    /// <summary>
    /// Toolbar Export uses saved room option toggles when the user has set them;
    /// otherwise falls back to defaults. Object scope is always 3D-only.
    /// </summary>
    public static ExportRequest GetRequestForToolbar()
    {
        // Object export is always just the selected 3D model — never room include filters.
        if (ExportRequest.CurrentScope() == ExportScope.SelectedObject)
            return ExportRequest.CreateDefaultsForSelection();

        ExportRequest request = (Instance != null && Instance._wired && Instance._hasRoomChoices)
            ? Instance.BuildRoomRequestFromUI()
            : ExportRequest.CreateDefaultsForRoom();

        if (!request.IncludeObj && !request.IncludeElevations
            && !request.IncludeProposal && !request.IncludeSnapshots)
        {
            UI_DialogPrompt.Open(
                "Nothing is selected to export.\nOpen Export room options… and turn at least one item on.",
                new ButtonAction("OK"));
            return null;
        }

        return request;
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
            if (text.text.Contains("OBJ Export") || text.text.Contains("Export Options"))
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

        _toggleObj = FindToggle("Toggle_IncludeFloor");
        _toggleElevations = FindToggle("Toggle_IncludeFloorObjects");
        _toggleProposal = FindToggle("Toggle_IncludeCeiling");
        _toggleSnapshots = FindToggle("Toggle_IncludeCeilingObjects");
        _toggleAdvanced = FindToggle("Toggle_IncludeWalls");
        _togglePerAssembly = FindToggle("Toggle_IncludeWallObjects");

        if (_toggleObj == null || _toggleElevations == null || _toggleProposal == null
            || _toggleSnapshots == null || _toggleAdvanced == null || _togglePerAssembly == null)
            throw new Exception("Export options template is missing expected toggles.");

        _objRow = _toggleObj.gameObject;
        _elevationsRow = _toggleElevations.gameObject;
        _proposalRow = _toggleProposal.gameObject;
        _snapshotsRow = _toggleSnapshots.gameObject;
        _advancedRow = _toggleAdvanced.gameObject;
        _perAssemblyRow = _togglePerAssembly.gameObject;

        HideToggle("Toggle_IncludeArmAssemblies");
        HideToggle("Toggle_IncludeArmBoomHeads");
        _advancedRow.SetActive(false);

        SetToggleLabel(_toggleObj, "3D model (OBJ / MTL)");
        SetToggleLabel(_toggleElevations, "Elevation sheets (PDF)");
        SetToggleLabel(_toggleProposal, "Sales proposal (PDF)");
        SetToggleLabel(_toggleSnapshots, "Presentation snapshots (PNG)");
        SetToggleLabel(_togglePerAssembly, "One PDF per boom (instead of one combined PDF)");

        _toggleElevations.onValueChanged.RemoveAllListeners();
        _toggleElevations.onValueChanged.AddListener(_ =>
        {
            RefreshPerAssemblyVisibility();
            if (isActiveAndEnabled)
                RebuildLayout();
        });

        // Nested separators under toggles fight the cleaned-up spacing — hide them.
        foreach (var toggle in new[]
                 {
                     _toggleObj, _toggleElevations, _toggleProposal,
                     _toggleSnapshots, _togglePerAssembly, _toggleAdvanced
                 })
        {
            HideNestedSeparators(toggle.transform);
            EnsurePreferredHeight(toggle.gameObject, 26f);
        }

        if (_titleLabel != null)
        {
            EnsurePreferredHeight(_titleLabel.gameObject, 28f);
            _titleLabel.fontSize = 20;
            _titleLabel.enableWordWrapping = false;
            _titleLabel.overflowMode = TextOverflowModes.Ellipsis;

            _infoLabel = CreateInfoLabel(_titleLabel);
        }

        WireButtons();
        ConfigureInnerBoxLayout();
    }

    private void ConfigureInnerBoxLayout()
    {
        if (_innerBox == null)
            return;

        // Background must not participate in layout; stretch to fill the (resized) panel.
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
        _layout.padding = new RectOffset(22, 22, 16, 16);
        _layout.spacing = 8;
        _layout.childAlignment = TextAnchor.UpperCenter;
        _layout.childControlWidth = true;
        _layout.childControlHeight = true;
        _layout.childForceExpandWidth = true;
        _layout.childForceExpandHeight = false;

        // Prefer explicit sizing in RebuildLayout — CSF alone left the prefab's tall box.
        var fitter = _innerBox.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.enabled = false;

        _innerBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500f);
        // Uniform scale of the whole menu (not just wider).
        _innerBox.localScale = Vector3.one * 1.35f;
    }

    private TMP_Text CreateInfoLabel(TMP_Text styleSource)
    {
        var go = new GameObject("InfoLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.layer = gameObject.layer;
        go.transform.SetParent(styleSource.transform.parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = styleSource.font;
        text.fontSharedMaterial = styleSource.fontSharedMaterial;
        text.fontSize = 14;
        text.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;
        text.lineSpacing = -10f;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 40f;
        le.preferredHeight = 52f;
        le.flexibleWidth = 1f;

        return text;
    }

    private void WireButtons()
    {
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            string n = button.gameObject.name;
            if (n.Contains("ExportScene") || n.Contains("ExportAll"))
                _buttonExport = button;
            else if (n.Contains("Cancel"))
                _buttonCancel = button;
            else if (n.Contains("ExportSelection") || n.Contains("Selection"))
                _buttonCustomizeObj = button;
        }

        // Toolbar owns Export — never show a duplicate in this panel.
        if (_buttonExport != null)
        {
            _buttonExport.gameObject.SetActive(false);
            var ignore = _buttonExport.GetComponent<LayoutElement>()
                         ?? _buttonExport.gameObject.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
        }

        if (_buttonCancel != null)
        {
            SetButtonLabel(_buttonCancel, "Done");
            _buttonCancel.onClick = new Button.ButtonClickedEvent();
            _buttonCancel.onClick.AddListener(() =>
            {
                if (_scope == ExportScope.Room)
                    _hasRoomChoices = true;
                Close();
            });
            StyleActionButton(_buttonCancel, 40f);
        }

        if (_buttonCustomizeObj != null)
        {
            SetButtonLabel(_buttonCustomizeObj, "Customize 3D model contents…");
            _buttonCustomizeObj.onClick = new Button.ButtonClickedEvent();
            _buttonCustomizeObj.onClick.AddListener(() =>
            {
                // Room-only: these toggles filter whole-room OBJ contents.
                if (_scope != ExportScope.Room)
                    return;

                _hasRoomChoices = true;
                Close();
                UI_ObjExportOptions.OpenForCustomization(opts =>
                {
                    _objOptions = opts;
                    _objOptionsCustomized = true;
                    _forcedScopeOnOpen = ExportScope.Room;
                    Reopen();
                });
            });
            StyleActionButton(_buttonCustomizeObj, 40f);
        }

        if (_buttonChooseFolder == null)
        {
            var template = _buttonCustomizeObj ?? _buttonCancel;
            if (template != null)
            {
                _buttonChooseFolder = CloneActionButton(template, "Button_ChooseExportFolder", "Choose export folder…");
                _buttonChooseFolder.onClick = new Button.ButtonClickedEvent();
                _buttonChooseFolder.onClick.AddListener(() =>
                {
                    var returnScope = _scope;
                    if (_scope == ExportScope.Room)
                        _hasRoomChoices = true;
                    Close();
                    FullRoomSave.OpenChooseExportFolderPrompt(() =>
                    {
                        _forcedScopeOnOpen = returnScope;
                        Reopen();
                    });
                });
            }
        }
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
            label.fontSize = 16;
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
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

        // Object options must not wipe room deliverable / 3D-include choices.
        if (_scope == ExportScope.SelectedObject)
        {
            RefreshChrome();
            return;
        }

        // First time in room options this session: load defaults.
        // After the user has confirmed choices once, leave their toggles alone.
        if (!_hasRoomChoices)
        {
            var defaults = ExportRequest.CreateDefaultsForRoom();
            if (!_objOptionsCustomized)
                _objOptions = defaults.ObjOptions ?? ObjExportOptions.CreateDefaults();

            _toggleObj.isOn = defaults.IncludeObj;
            _toggleElevations.isOn = defaults.IncludeElevations;
            _toggleProposal.isOn = defaults.IncludeProposal;
            _toggleSnapshots.isOn = defaults.IncludeSnapshots;
            _togglePerAssembly.isOn = defaults.ElevationMode == ElevationExportMode.PerAssembly;
        }

        RefreshChrome();
    }

    private void RefreshChrome()
    {
        if (!_wired)
            WireExistingChrome();

        // Apply a one-shot scope override (e.g. returning from room 3D customize).
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
            _titleLabel.text = objectMode ? "Export Folder" : "Room Export Options";

        UpdateInfoText(objectMode);

        // Deliverable toggles + room OBJ include customization are room-only.
        _objRow.SetActive(!objectMode);
        _elevationsRow.SetActive(!objectMode);
        _proposalRow.SetActive(!objectMode);
        _snapshotsRow.SetActive(!objectMode);
        _advancedRow.SetActive(false);
        RefreshPerAssemblyVisibility();

        if (_buttonExport != null)
            _buttonExport.gameObject.SetActive(false);

        // Include floor/ceiling/walls only applies to whole-room OBJ export.
        if (_buttonCustomizeObj != null)
            _buttonCustomizeObj.gameObject.SetActive(!objectMode);

        // Folder choice applies to both room and object exports.
        if (_buttonChooseFolder != null)
            _buttonChooseFolder.gameObject.SetActive(true);

        if (_buttonCancel != null)
        {
            _buttonCancel.gameObject.SetActive(true);
            SetButtonLabel(_buttonCancel, "Done");
        }
    }

    private void RefreshPerAssemblyVisibility()
    {
        bool show = _scope == ExportScope.Room
                    && _elevationsRow != null
                    && _elevationsRow.activeSelf
                    && _toggleElevations != null
                    && _toggleElevations.isOn;
        if (_perAssemblyRow != null)
            _perAssemblyRow.SetActive(show);
    }

    private void UpdateInfoText(bool objectMode)
    {
        if (_infoLabel == null)
            return;

        string path = ShortenPath(ExportPaths.GetExportBasePath());
        string folderLine = ExportPaths.HasCustomParentFolder()
            ? $"Save under (custom):\n{path}"
            : $"Save under:\n{path}";

        string hint = objectMode
            ? "Choose where the selected object's 3D model is saved,\nthen use Export object 3D model on the toolbar."
            : (_objOptionsCustomized
                ? "Choose what to include. Custom 3D contents are on.\nExport from the toolbar when ready."
                : "Choose what to include, then export from the toolbar.");

        _infoLabel.text = folderLine + "\n\n" + hint;
        _infoLabel.ForceMeshUpdate();
        float needed = Mathf.Clamp(_infoLabel.preferredHeight + 4f, 44f, 88f);
        var le = _infoLabel.GetComponent<LayoutElement>();
        if (le != null)
            le.preferredHeight = needed;
    }

    private void RebuildLayout()
    {
        if (_innerBox == null)
            return;

        // Stable visual order for whatever is currently active.
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
        AddGo(_objRow);
        AddGo(_elevationsRow);
        AddGo(_proposalRow);
        AddGo(_snapshotsRow);
        AddGo(_perAssemblyRow);
        Add(_buttonCustomizeObj);
        Add(_buttonChooseFolder);
        Add(_buttonCancel);

        for (int i = 0; i < order.Count; i++)
            order[i].SetSiblingIndex(i + 1); // keep Bg at 0

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerBox);

        // Shrink the panel to the laid-out content (kills the empty bottom gap).
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

        _innerBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500f);
        _innerBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(contentHeight, 120f));
        _innerBox.localScale = Vector3.one * 1.35f;
        LayoutRebuilder.ForceRebuildLayoutImmediate(_innerBox);
    }

    private ExportRequest BuildRoomRequestFromUI()
    {
        return new ExportRequest
        {
            Scope = ExportScope.Room,
            IncludeObj = _toggleObj.isOn,
            IncludeElevations = _toggleElevations.isOn,
            IncludeProposal = _toggleProposal.isOn,
            IncludeSnapshots = _toggleSnapshots.isOn,
            ElevationMode = _togglePerAssembly.isOn
                ? ElevationExportMode.PerAssembly
                : ElevationExportMode.CombinedRoom,
            ObjOptions = _objOptions ?? ObjExportOptions.CreateDefaults()
        };
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
            toggle.gameObject.SetActive(false);
    }

    private static void HideNestedSeparators(Transform root)
    {
        foreach (Transform child in root)
        {
            if (child.name.StartsWith("separator", StringComparison.OrdinalIgnoreCase))
                child.gameObject.SetActive(false);
        }
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
        label.fontSize = 15;
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
