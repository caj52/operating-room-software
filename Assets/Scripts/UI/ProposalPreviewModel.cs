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
    public string CompanyPhone { get; private set; } = "Tel: 214.987.0404";

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
        public string LightDescription;
        public string LightOptionsText;
        public bool HasBooms;
        public int BoomQty;
        public string BoomDescription;
        public string BoomOptionsText;
        public double Subtotal;
        public List<ProposalPricingResolver.Line> LightLines = new();
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
        /// <summary>Matches PDF Subtotal row (right-aligned label + amount).</summary>
        public bool IsSubtotal;
        /// <summary>True for OPTION/ACCESSORY DESCRIPTION headers (gray in PDF).</summary>
        public bool IsOptionHeader;
        /// <summary>Config title row (AddTableTitle in PDF pricing page).</summary>
        public bool IsConfigTitle;
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

            // Keep page-1 preview blocks aligned with ProposalPricingResolver / PDF page 1.
            var lightLines = ProposalPricingResolver.ResolveLightLines(lights);
            var boomLine = ProposalPricingResolver.ResolveBoomLine(
                group.Key != null ? group.Key.gameObject : null, booms);
            double subtotal = ProposalPricingResolver.SumEquipment(group);

            string lightText = BuildOptionsText(lights, false);
            string boomText = BuildOptionsText(booms, true);

            ConfigBlocks.Add(new ConfigBlock
            {
                Title = ConfigName,
                HasLights = lightLines.Count > 0,
                LightQty = lightLines.Sum(l => Math.Max(1, l.Qty)),
                LightDescription = lightLines.Count > 0
                    ? string.Join(" + ", lightLines.Select(l => l.Description).Where(d => !string.IsNullOrWhiteSpace(d)))
                    : "",
                LightOptionsText = lightText,
                LightLines = lightLines,
                HasBooms = boomLine != null,
                BoomQty = boomLine != null ? Math.Max(1, boomLine.Qty) : 0,
                BoomDescription = boomLine?.Description ?? "",
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
            .ToList();

        var rootConfigs = selectablePrices
            .GroupBy(sp => sp.transform.root)
            .ToList();

        // Mirror ProposalPDFGenerator.GeneratePricingPage via ProposalPricingResolver.
        bool optionsEmitted = false;
        foreach (var configGroup in rootConfigs)
        {
            double subtotal = 0;
            bool emitOptions = !optionsEmitted;
            var first = configGroup.FirstOrDefault();
            var lights = configGroup.Where(sp => !sp.isBoomObject).ToList();
            var booms = configGroup.Where(sp => sp.isBoomObject).ToList();
            var lightLines = ProposalPricingResolver.ResolveLightLines(lights);
            var boomLine = ProposalPricingResolver.ResolveBoomLine(
                first != null ? first.transform.root.gameObject : null, booms);

            PricingLines.Add(new LineItem
            {
                IsConfigTitle = true,
                Description = string.IsNullOrWhiteSpace(ConfigName) ? "Configuration" : ConfigName
            });

            if (lightLines.Count > 0)
            {
                PricingLines.Add(new LineItem { IsSectionHeader = true, SectionTitle = "MODEL DESCRIPTION" });
                foreach (var line in lightLines)
                {
                    PricingLines.Add(new LineItem
                    {
                        PartNumber = string.IsNullOrEmpty(line.PartNumber) ? "N/A" : line.PartNumber,
                        Description = line.Description,
                        Qty = line.Qty,
                        UnitPrice = line.UnitPrice,
                        ExtPrice = line.ExtPrice
                    });
                    subtotal += line.ExtPrice;
                }
            }

            var lightOptions = emitOptions
                ? OptionSelections.Where(o => !o.IsBoom && !IsNoneOption(o.SelectedName) && o.Price > 0).ToList()
                : new List<OptionSelection>();
            if (lightOptions.Count > 0)
            {
                optionsEmitted = true;
                PricingLines.Add(new LineItem
                {
                    IsSectionHeader = true,
                    IsOptionHeader = true,
                    SectionTitle = "OPTION/ACCESSORY DESCRIPTION"
                });
                foreach (var opt in lightOptions)
                {
                    PricingLines.Add(new LineItem
                    {
                        PartNumber = string.IsNullOrEmpty(opt.PartNumber) ? "N/A" : opt.PartNumber,
                        Description = opt.SelectedName,
                        Qty = 1,
                        UnitPrice = opt.Price,
                        ExtPrice = opt.Price
                    });
                    subtotal += opt.Price;
                }
            }

            if (boomLine != null)
            {
                PricingLines.Add(new LineItem { IsSectionHeader = true, SectionTitle = "MODEL DESCRIPTION" });
                PricingLines.Add(new LineItem
                {
                    PartNumber = string.IsNullOrEmpty(boomLine.PartNumber) ? "N/A" : boomLine.PartNumber,
                    Description = string.IsNullOrWhiteSpace(boomLine.Description)
                        ? "ARTICULATING BOOM"
                        : boomLine.Description,
                    Qty = boomLine.Qty,
                    UnitPrice = boomLine.UnitPrice,
                    ExtPrice = boomLine.ExtPrice
                });
                subtotal += boomLine.ExtPrice;
            }

            var boomOptions = emitOptions
                ? OptionSelections.Where(o => o.IsBoom && !IsNoneOption(o.SelectedName) && o.Price > 0).ToList()
                : new List<OptionSelection>();
            if (boomOptions.Count > 0)
            {
                optionsEmitted = true;
                PricingLines.Add(new LineItem
                {
                    IsSectionHeader = true,
                    IsOptionHeader = true,
                    SectionTitle = "OPTION/ACCESSORY DESCRIPTION"
                });
                foreach (var opt in boomOptions)
                {
                    PricingLines.Add(new LineItem
                    {
                        PartNumber = string.IsNullOrEmpty(opt.PartNumber) ? "N/A" : opt.PartNumber,
                        Description = opt.SelectedName,
                        Qty = 1,
                        UnitPrice = opt.Price,
                        ExtPrice = opt.Price
                    });
                    subtotal += opt.Price;
                }
            }

            if (lightLines.Count > 0 || lightOptions.Count > 0 || boomLine != null || boomOptions.Count > 0)
            {
                PricingLines.Add(new LineItem
                {
                    IsSubtotal = true,
                    Description = "Subtotal",
                    ExtPrice = subtotal
                });
            }
        }

        // Options with no boom/light selectables still need page-3 rows (PDF empty-config path missed these).
        if (!optionsEmitted)
        {
            double optSub = 0;
            var lightOptions = OptionSelections
                .Where(o => !o.IsBoom && !IsNoneOption(o.SelectedName) && o.Price > 0).ToList();
            if (lightOptions.Count > 0)
            {
                PricingLines.Add(new LineItem
                {
                    IsSectionHeader = true,
                    IsOptionHeader = true,
                    SectionTitle = "OPTION/ACCESSORY DESCRIPTION"
                });
                foreach (var opt in lightOptions)
                {
                    PricingLines.Add(new LineItem
                    {
                        PartNumber = string.IsNullOrEmpty(opt.PartNumber) ? "N/A" : opt.PartNumber,
                        Description = opt.SelectedName,
                        Qty = 1,
                        UnitPrice = opt.Price,
                        ExtPrice = opt.Price
                    });
                    optSub += opt.Price;
                }
            }

            var boomOptions = OptionSelections
                .Where(o => o.IsBoom && !IsNoneOption(o.SelectedName) && o.Price > 0).ToList();
            if (boomOptions.Count > 0)
            {
                PricingLines.Add(new LineItem
                {
                    IsSectionHeader = true,
                    IsOptionHeader = true,
                    SectionTitle = "OPTION/ACCESSORY DESCRIPTION"
                });
                foreach (var opt in boomOptions)
                {
                    PricingLines.Add(new LineItem
                    {
                        PartNumber = string.IsNullOrEmpty(opt.PartNumber) ? "N/A" : opt.PartNumber,
                        Description = opt.SelectedName,
                        Qty = 1,
                        UnitPrice = opt.Price,
                        ExtPrice = opt.Price
                    });
                    optSub += opt.Price;
                }
            }

            if (optSub > 0)
            {
                PricingLines.Add(new LineItem
                {
                    IsSubtotal = true,
                    Description = "Subtotal",
                    ExtPrice = optSub
                });
            }
        }

        // Install / ship — same placement as PDF (after configs). Include when Excel returns a row.
        var reader = UnityEngine.Object.FindAnyObjectByType<ExcelReader>();
        var install = reader?.GetInstallationLightsCharges();
        if (install != null)
        {
            InstallCharge = install.ListPrice;
            PricingLines.Add(new LineItem
            {
                PartNumber = install.PartNumber ?? "N/A",
                Description = install.ObjectName ?? "Installation",
                Qty = 1,
                UnitPrice = install.ListPrice,
                ExtPrice = install.ListPrice
            });
        }
        else
            InstallCharge = 0;

        var ship = reader?.GetShippingLightsCharges();
        if (ship != null)
        {
            ShippingCharge = ship.ListPrice;
            PricingLines.Add(new LineItem
            {
                PartNumber = ship.PartNumber ?? "N/A",
                Description = ship.ObjectName ?? "Shipping",
                Qty = 1,
                UnitPrice = ship.ListPrice,
                ExtPrice = ship.ListPrice
            });
        }
        else
            ShippingCharge = 0;
    }

    void RecalcTotals()
    {
        var selectablePrices = UnityEngine.Object.FindObjectsByType<SelectablePrice>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        double selectables = ProposalPricingResolver.SumEquipment(selectablePrices);
        // Match page-3 / CalculateTotalPrice: skip None and $0 options.
        double dropdownTotal = OptionSelections
            .Where(o => !IsNoneOption(o.SelectedName) && o.Price > 0)
            .Sum(o => o.Price);
        Page1EquipmentTotal = selectables;
        EquipmentTotal = selectables + dropdownTotal + InstallCharge + ShippingCharge;
        GrandTotal = EquipmentTotal - (EquipmentTotal * DiscountPercentage / 100.0);
    }

    static string BuildOptionsText(List<SelectablePrice> group, bool isBoom)
    {
        // Mirror ProposalPDFGenerator.BuildOptionsDescriptionString exactly.
        var filtered = group
            .Where(sp => sp != null && sp.objectPricingData != null && sp.isBoomObject == isBoom)
            .OrderBy(sp => ProposalPDFGenerator.GetHierarchyPath(sp.transform))
            .ToList();

        var baseNames = filtered.Select(sp =>
            string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                ? sp.objectPricingData.ObjectName
                : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}".Trim());

        var ddValues = DropdownPopulator.GetAllCurrentStates()
            .Where(s => s.Item1 != null && s.Item2 != null && s.Item1.isBoomExcelFileDropDown == isBoom)
            .Select(s => s.Item2.ObjectName)
            .Where(name => !IsNoneOption(name));

        IEnumerable<string> btParts = Enumerable.Empty<string>();
        if (isBoom)
        {
            var btGrouped = ProposalPDFGenerator.GetBtNamesGroupedByType(filtered);
            if (btGrouped.TryGetValue("Boom", out var boomList) && boomList != null)
                btParts = boomList;
        }

        return string.Join(", ",
            baseNames.Concat(ddValues).Concat(btParts)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct());
    }

    static bool IsNoneOption(string name)
        => string.IsNullOrWhiteSpace(name)
           || name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);
}
