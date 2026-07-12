using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared proposal state for the interactive document mock and PDF export defaults.
/// Line items / options stay live from the room; editable fields live here.
/// </summary>
public sealed class ProposalPreviewModel
{
    public const string PrefsNote1 = "SalesProposal.Note1";
    public const string PrefsNote2 = "SalesProposal.Note2";
    public const string PrefsNote3 = "SalesProposal.Note3";

    public static readonly string DefaultNote1 =
        "35% deposit. Progress billing to completion. Sales tax to be added to invoices in applicable";
    public static readonly string DefaultNote2 =
        "Quote is valid for 90 days from creation date.";
    public static readonly string DefaultNote3 =
        "Customer Acceptance and Configuration Acknowledgement.";

    public string CompanyName { get; private set; } = "Imagine Unlimited";
    public string CompanyAddress { get; private set; } = "9155 Sterling St Suite 120";
    public string CompanyCity { get; private set; } = "Irving, TX 75063";
    public string CompanyPhone { get; private set; } = "Tel: 1 877 789 8106";

    public string SalesRepName { get; set; } = "";
    public string SalesRepEmail { get; set; } = "";

    public string ClientName { get; private set; } = "";
    public string ProjectName { get; private set; } = "";
    public string ProjectNumber { get; private set; } = "";
    public string AccountName { get; private set; } = "";
    public string AccountAddressLine1 { get; private set; } = "";
    public string AccountAddressLine2 { get; private set; } = "";
    public string OrderReference { get; private set; } = "";

    public string ConfigName { get; set; } = "Configuration";
    /// <summary>When set, export uses this title instead of the saved room name.</summary>
    public static string ConfigNameOverride { get; set; }
    public float DiscountPercentage { get; set; }

    public string Note1 { get; set; } = DefaultNote1;
    public string Note2 { get; set; } = DefaultNote2;
    public string Note3 { get; set; } = DefaultNote3;

    public List<ConfigBlock> ConfigBlocks { get; } = new();
    public List<LineItem> PricingLines { get; } = new();
    public List<OptionSelection> OptionSelections { get; } = new();

    public double EquipmentTotal { get; private set; }
    /// <summary>Page-1 PDF total: selectable equipment only (excludes dropdown/install/ship).</summary>
    public double Page1EquipmentTotal { get; private set; }
    public double InstallCharge { get; private set; }
    public double ShippingCharge { get; private set; }
    public double GrandTotal { get; private set; }

    public string LightOptionsSummary { get; private set; } = "";
    public string BoomOptionsSummary { get; private set; } = "";

    public sealed class ConfigBlock
    {
        public string Title;
        public bool HasLights;
        public int LightQty;
        public string LightOptionsText;
        public bool HasBooms;
        public int BoomQty;
        public string BoomOptionsText;
        public double Subtotal;
    }

    public sealed class LineItem
    {
        public string PartNumber;
        public string Description;
        public int Qty;
        public double UnitPrice;
        public double ExtPrice;
        public bool IsSectionHeader;
        public string SectionTitle;
    }

    public sealed class OptionSelection
    {
        public DropdownPopulator Source;
        public string Label;
        public bool IsBoom;
        public string SelectedName;
        public string PartNumber;
        public double Price;
        public int DropdownIndex;
        public IReadOnlyList<string> Options;
    }

    public static ProposalPreviewModel Capture()
    {
        var model = new ProposalPreviewModel();
        model.Refresh(reloadEditableFields: true);
        return model;
    }

    public void Refresh() => Refresh(reloadEditableFields: true);

    /// <summary>
    /// Refresh live room/client/options/totals without overwriting discount/notes/sales-rep
    /// the user may be mid-edit in the workspace. Pass <paramref name="syncSalesRep"/> after
    /// Client Data closes so sales-rep fields pick up edits made there.
    /// </summary>
    public void RefreshLive(bool syncSalesRep = false) =>
        Refresh(reloadEditableFields: false, syncSalesRep: syncSalesRep);

    void Refresh(bool reloadEditableFields, bool syncSalesRep = false)
    {
        if (reloadEditableFields)
        {
            SalesRepName = UI_ClientMetaData.SalesRepName ?? "";
            SalesRepEmail = UI_ClientMetaData.SalesRepEmail ?? "";

            if (DiscountPercentage <= 0f)
                DiscountPercentage = ProposalPDFGenerator.GetSavedDiscountPercentage();

            Note1 = PlayerPrefs.GetString(PrefsNote1, DefaultNote1);
            Note2 = PlayerPrefs.GetString(PrefsNote2, DefaultNote2);
            Note3 = PlayerPrefs.GetString(PrefsNote3, DefaultNote3);
        }
        else if (syncSalesRep)
        {
            SalesRepName = UI_ClientMetaData.SalesRepName ?? "";
            SalesRepEmail = UI_ClientMetaData.SalesRepEmail ?? "";
        }

        ClientName = UI_ClientMetaData.AccountName ?? "";
        AccountName = ClientName;
        ProjectName = UI_ClientMetaData.ProjectName ?? "";
        ProjectNumber = UI_ClientMetaData.ProjectNumber ?? "";
        AccountAddressLine1 = UI_ClientMetaData.AccountAddressLine1 ?? "";
        AccountAddressLine2 = UI_ClientMetaData.AccountAddressLine2 ?? "";
        OrderReference = UI_ClientMetaData.OrderReferenceNumber ?? "";

        if (!string.IsNullOrWhiteSpace(ConfigNameOverride))
            ConfigName = ConfigNameOverride;
        else if (ExportPaths.HasSavedRoomName())
            ConfigName = ExportPaths.GetRoomExportName();
        else if (string.IsNullOrWhiteSpace(ConfigName))
            ConfigName = "Configuration";

        RebuildOptions();
        RebuildConfigBlocks();
        RebuildPricingLines();
        RecalcTotals();
    }

    public void PersistEditableFields()
    {
        UI_ClientMetaData.SalesRepName = SalesRepName?.Trim() ?? "";
        UI_ClientMetaData.SalesRepEmail = SalesRepEmail?.Trim() ?? "";
        ProposalPDFGenerator.SaveDiscountPercentage(Mathf.Max(0f, DiscountPercentage));
        PlayerPrefs.SetString(PrefsNote1, Note1 ?? DefaultNote1);
        PlayerPrefs.SetString(PrefsNote2, Note2 ?? DefaultNote2);
        PlayerPrefs.SetString(PrefsNote3, Note3 ?? DefaultNote3);
        PlayerPrefs.Save();
    }

    public void ApplyTo(ProposalPDFGenerator generator)
    {
        if (generator == null)
            return;

        PersistEditableFields();

        generator.clientName = ClientName;
        generator.accountName = AccountName;
        generator.projectName = ProjectName;
        generator.projectNumber = ProjectNumber;
        generator.referenceNumber = OrderReference;

        var addressParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(AccountAddressLine1))
            addressParts.Add(AccountAddressLine1.Trim());
        if (!string.IsNullOrWhiteSpace(AccountAddressLine2))
            addressParts.Add(AccountAddressLine2.Trim());
        generator.accountAddress = addressParts.Count > 0 ? string.Join(", ", addressParts) : "";

        generator.salesRepName = SalesRepName ?? "";
        generator.salesRepEmail = SalesRepEmail ?? "";
        generator.configName = string.IsNullOrWhiteSpace(ConfigName) ? "Configuration" : ConfigName;
        generator.discountPercentage = Mathf.Max(0f, DiscountPercentage);
        generator.note1 = string.IsNullOrWhiteSpace(Note1) ? DefaultNote1 : Note1;
        generator.note2 = string.IsNullOrWhiteSpace(Note2) ? DefaultNote2 : Note2;
        generator.note3 = string.IsNullOrWhiteSpace(Note3) ? DefaultNote3 : Note3;
    }

    void RebuildOptions()
    {
        OptionSelections.Clear();
        var states = DropdownPopulator.GetAllCurrentStates();
        foreach (var (populator, data) in states)
        {
            if (populator == null || data == null)
                continue;

            var dd = populator.GetComponent<TMPro.TMP_Dropdown>()
                     ?? populator.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
            var options = dd != null
                ? dd.options.Select(o => o.text).ToList()
                : new List<string> { data.ObjectName };

            OptionSelections.Add(new OptionSelection
            {
                Source = populator,
                Label = populator.gameObject != null ? populator.gameObject.name : (populator.isBoomExcelFileDropDown ? "Boom option" : "Light option"),
                IsBoom = populator.isBoomExcelFileDropDown,
                SelectedName = data.ObjectName,
                PartNumber = data.PartNumber,
                Price = data.ListPrice,
                DropdownIndex = dd != null ? dd.value : 0,
                Options = options
            });
        }

        LightOptionsSummary = string.Join(", ",
            OptionSelections.Where(o => !o.IsBoom && !IsNoneOption(o.SelectedName))
                .Select(o => o.SelectedName));
        BoomOptionsSummary = string.Join(", ",
            OptionSelections.Where(o => o.IsBoom && !IsNoneOption(o.SelectedName))
                .Select(o => o.SelectedName));
    }

    void RebuildConfigBlocks()
    {
        ConfigBlocks.Clear();
        var selectablePrices = UnityEngine.Object.FindObjectsByType<SelectablePrice>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        var rootGroups = selectablePrices
            .Where(sp => sp != null && sp.objectPricingData != null)
            .GroupBy(sp => sp.transform.root)
            .ToList();

        foreach (var group in rootGroups)
        {
            var lights = group.Where(sp => !sp.isBoomObject).ToList();
            var booms = group.Where(sp => sp.isBoomObject).ToList();
            if (lights.Count == 0 && booms.Count == 0)
                continue;

            int boomQty = ResolveBoomQty(group);
            double subtotal = 0;
            foreach (var sp in group)
            {
                if (sp.UIRefPricingRowDataFill != null)
                    subtotal += sp.UIRefPricingRowDataFill.Price;
                else if (sp.objectPricingData != null)
                    subtotal += sp.objectPricingData.ListPrice +
                        (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0);
            }
            // Dropdown option totals are global (not per root group) — omit here to avoid double-count.

            string lightText = BuildOptionsText(lights, false);
            string boomText = BuildOptionsText(booms, true);

            ConfigBlocks.Add(new ConfigBlock
            {
                Title = ConfigName,
                HasLights = lights.Count > 0,
                LightQty = lights.Count,
                LightOptionsText = lightText,
                HasBooms = booms.Count > 0,
                BoomQty = boomQty,
                BoomOptionsText = boomText,
                Subtotal = subtotal
            });
        }

        if (ConfigBlocks.Count == 0)
        {
            ConfigBlocks.Add(new ConfigBlock
            {
                Title = ConfigName,
                HasLights = false,
                HasBooms = false,
                LightOptionsText = LightOptionsSummary,
                BoomOptionsText = BoomOptionsSummary,
                Subtotal = 0
            });
        }
    }

    void RebuildPricingLines()
    {
        PricingLines.Clear();
        var selectablePrices = UnityEngine.Object.FindObjectsByType<SelectablePrice>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(sp => sp != null && sp.objectPricingData != null)
            .OrderBy(sp => HierarchyPath(sp.transform))
            .ToList();

        var lights = selectablePrices.Where(sp => !sp.isBoomObject).ToList();
        var booms = selectablePrices.Where(sp => sp.isBoomObject).ToList();

        if (lights.Count > 0)
        {
            PricingLines.Add(new LineItem { IsSectionHeader = true, SectionTitle = "LIGHT" });
            foreach (var sp in lights)
                AddSelectableLine(sp);
            AddDropdownLines(false);
        }

        if (booms.Count > 0)
        {
            PricingLines.Add(new LineItem { IsSectionHeader = true, SectionTitle = "ARTICULATING BOOM" });
            foreach (var sp in booms)
                AddSelectableLine(sp);
            AddDropdownLines(true);
        }

        // Options with no matching equipment section still need to appear (parity with PDF totals).
        if (lights.Count == 0)
            AddDropdownLines(false);
        if (booms.Count == 0)
            AddDropdownLines(true);

        var reader = UnityEngine.Object.FindAnyObjectByType<ExcelReader>();
        var install = reader?.GetInstallationLightsCharges();
        if (install != null && install.ListPrice > 0)
        {
            PricingLines.Add(new LineItem
            {
                PartNumber = install.PartNumber ?? "N/A",
                Description = install.ObjectName ?? "Installation",
                Qty = 1,
                UnitPrice = install.ListPrice,
                ExtPrice = install.ListPrice
            });
            InstallCharge = install.ListPrice;
        }
        else
            InstallCharge = 0;

        var ship = reader?.GetShippingLightsCharges();
        if (ship != null && ship.ListPrice > 0)
        {
            PricingLines.Add(new LineItem
            {
                PartNumber = ship.PartNumber ?? "N/A",
                Description = ship.ObjectName ?? "Shipping",
                Qty = 1,
                UnitPrice = ship.ListPrice,
                ExtPrice = ship.ListPrice
            });
            ShippingCharge = ship.ListPrice;
        }
        else
            ShippingCharge = 0;
    }

    void AddSelectableLine(SelectablePrice sp)
    {
        var data = sp.objectPricingData;
        double unit = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);
        if (sp.UIRefPricingRowDataFill != null)
            unit = sp.UIRefPricingRowDataFill.Price;

        string name = string.IsNullOrEmpty(data.ObjectSize)
            ? data.ObjectName
            : $"{data.ObjectSize} {data.ObjectName}".Trim();

        PricingLines.Add(new LineItem
        {
            PartNumber = string.IsNullOrEmpty(data.PartNumber) ? "N/A" : data.PartNumber,
            Description = name,
            Qty = 1,
            UnitPrice = unit,
            ExtPrice = unit
        });
    }

    void AddDropdownLines(bool isBoom)
    {
        foreach (var opt in OptionSelections.Where(o => o.IsBoom == isBoom))
        {
            if (opt.Price <= 0 || IsNoneOption(opt.SelectedName))
                continue;
            PricingLines.Add(new LineItem
            {
                PartNumber = string.IsNullOrEmpty(opt.PartNumber) ? "N/A" : opt.PartNumber,
                Description = opt.SelectedName,
                Qty = 1,
                UnitPrice = opt.Price,
                ExtPrice = opt.Price
            });
        }
    }

    void RecalcTotals()
    {
        double selectables = 0;
        var selectablePrices = UnityEngine.Object.FindObjectsByType<SelectablePrice>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var sp in selectablePrices)
        {
            if (sp == null) continue;
            if (sp.UIRefPricingRowDataFill != null)
                selectables += sp.UIRefPricingRowDataFill.Price;
            else if (sp.objectPricingData != null)
                selectables += sp.objectPricingData.ListPrice +
                    (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0);
        }

        // Match ProposalPDFGenerator.CalculateTotalPrice — priced options only (skip None).
        double dropdownTotal = OptionSelections
            .Where(o => !IsNoneOption(o.SelectedName))
            .Sum(o => o.Price);
        Page1EquipmentTotal = selectables; // PDF page 1 config total (selectables only)
        EquipmentTotal = selectables + dropdownTotal + InstallCharge + ShippingCharge;
        GrandTotal = EquipmentTotal - (EquipmentTotal * DiscountPercentage / 100.0);
    }

    static int ResolveBoomQty(IGrouping<Transform, SelectablePrice> group)
    {
        int boomQty = 1;
        var firstSp = group.FirstOrDefault();
        if (firstSp == null)
            return boomQty;

        var rootSelectables = firstSp.transform.root.GetComponentsInChildren<Selectable>(true);
        bool isTandem = rootSelectables.Any(s =>
            !string.IsNullOrEmpty(s.UIButtonName) &&
            s.UIButtonName.Contains("Boom - Tandem Ceiling Cover"));
        if (!isTandem)
            return boomQty;

        int serviceHeadCount = rootSelectables.Count(s => s.GetComponent<BoomHeadScaleHandler>() != null);
        if (serviceHeadCount >= 2)
            boomQty = 2;
        return boomQty;
    }

    static string BuildOptionsText(List<SelectablePrice> group, bool isBoom)
    {
        var filtered = group
            .Where(sp => sp != null && sp.objectPricingData != null && sp.isBoomObject == isBoom)
            .OrderBy(sp => HierarchyPath(sp.transform))
            .ToList();

        var baseNames = filtered.Select(sp =>
            string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                ? sp.objectPricingData.ObjectName
                : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}".Trim());

        var ddValues = DropdownPopulator.GetAllCurrentStates()
            .Where(s => s.Item1 != null && s.Item2 != null && s.Item1.isBoomExcelFileDropDown == isBoom)
            .Select(s => s.Item2.ObjectName)
            .Where(name => !IsNoneOption(name));

        return string.Join(", ",
            baseNames.Concat(ddValues)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct());
    }

    static bool IsNoneOption(string name)
        => string.IsNullOrWhiteSpace(name)
           || name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);

    static string HierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
