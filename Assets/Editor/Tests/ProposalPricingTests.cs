using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A light keeps its own price unless it is the one mounted on a tandem,
/// and its name is the heads that were placed, not the ceiling cover.
/// </summary>
public class ProposalPricingTests
{
    readonly List<GameObject> _created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i] != null)
                Object.DestroyImmediate(_created[i]);
        }
        _created.Clear();
    }

    [Test]
    public void LightOnTandem_GetsTheCoverCredit()
    {
        var covers = new List<(Vector3 position, bool tandem)>
        {
            (new Vector3(0f, 2.9f, 0.5f), true)
        };

        Assert.IsTrue(ProposalPricingResolver.LightMountedOnTandem(
            new Vector3(-1.2f, 2.9f, 1.8f), covers));
    }

    [Test]
    public void LightOnItsOwnBoom_KeepsItsPriceWhenATandemIsElsewhere()
    {
        var covers = new List<(Vector3 position, bool tandem)>
        {
            (new Vector3(0f, 3f, 3f), false),
            (new Vector3(-6f, 3f, 0.5f), true)
        };

        Assert.IsFalse(ProposalPricingResolver.LightMountedOnTandem(
            new Vector3(-1.6f, 3f, 2.2f), covers));
    }

    [Test]
    public void TandemAcrossTheRoom_DoesNotDiscountALightWithNoBoom()
    {
        var covers = new List<(Vector3 position, bool tandem)>
        {
            (new Vector3(12f, 3f, 0f), true)
        };

        Assert.IsFalse(ProposalPricingResolver.LightMountedOnTandem(
            Vector3.zero, covers));
    }

    [Test]
    public void LightName_IsTheHeads_NotTheCeilingCover()
    {
        var cover = Price("Lights - Single Ceiling Cover (Standard)");
        var led = Price("Lights - U | 002");
        var panel = Price("Lights - Flat Panel Carrier");
        led.transform.SetParent(cover.transform);
        panel.transform.SetParent(cover.transform);

        string phrase = ProposalListing.LightPhrase(
            new[] { cover, led, panel }, article: false);

        StringAssert.DoesNotContain("Ceiling Cover", phrase);
        StringAssert.Contains("U|002", phrase);
        StringAssert.Contains("LED", phrase);
        StringAssert.Contains("FP", phrase);
    }

    [Test]
    public void SpringBoom_PlainFlange_IsNotThePoweredFlangeSku()
    {
        var family = ProposalPricingResolver.DetectBoomArmFamily(new[]
        {
            "Boom - Fixed Top Arm",
            "Boom - Spring Bottom Arm",
            "Boom - Ceiling Flange"
        });
        Assert.AreEqual(ProposalPricingResolver.BoomArmFamily.Spring, family);

        var quote = ProposalPricingResolver.QuoteFlange(family, 100, flangeIsXl: false);
        Assert.IsNull(quote.LookupName);
        Assert.AreEqual(0, quote.FallbackPrice);
        StringAssert.Contains("100mm", quote.Description);
        StringAssert.DoesNotContain("Powered", quote.Description);
    }

    [Test]
    public void FixedBoom_PlainFlange_IsNotThePoweredFlangeSku()
    {
        var family = ProposalPricingResolver.DetectBoomArmFamily(new[]
        {
            "Boom - Fixed Top Arm",
            "Boom - Fixed Bottom Arm",
            "Boom - Ceiling Flange"
        });
        Assert.AreEqual(ProposalPricingResolver.BoomArmFamily.Fixed, family);

        var quote = ProposalPricingResolver.QuoteFlange(family, 100, flangeIsXl: false);
        Assert.AreEqual(0, quote.FallbackPrice);
        StringAssert.DoesNotContain("Powered", quote.Description);
    }

    [Test]
    public void PoweredXlBoom_ThreeHundredMmFlange_UsesPoweredXlSku()
    {
        var family = ProposalPricingResolver.DetectBoomArmFamily(new[]
        {
            "Boom - Arm (Powered XL)",
            "Boom - Ceiling Flange (XL)"
        });
        Assert.AreEqual(ProposalPricingResolver.BoomArmFamily.PoweredXl, family);

        var quote = ProposalPricingResolver.QuoteFlange(family, 300, flangeIsXl: true);
        Assert.AreEqual("Powered XL - 300mm XL Ceiling Flange", quote.LookupName);
        Assert.AreEqual(0, quote.FallbackPrice);
    }

    [Test]
    public void PoweredXlBoom_OneHundredMmFlange_IsFree()
    {
        var quote = ProposalPricingResolver.QuoteFlange(
            ProposalPricingResolver.BoomArmFamily.PoweredXl, 100, flangeIsXl: true);
        Assert.IsNull(quote.LookupName);
        Assert.AreEqual(0, quote.FallbackPrice);
        StringAssert.Contains("100mm", quote.Description);
    }

    [Test]
    public void MissingShelfRow_IsNotGivenAStandInPrice()
    {
        Assert.AreEqual(0, ProposalPricingResolver.ShelfPackagePrice(500, 1));
        Assert.AreEqual(0, ProposalPricingResolver.ShelfPackagePrice(500, 2));
    }

    [Test]
    public void SellPrice_UsesCostMarginAndTariff()
    {
        double sell = ProposalPricingResolver.SellPrice(10069.455, 0.4, 0.1);
        Assert.AreEqual(10069.455 * 1.1 / 0.6, sell, 0.02);
        Assert.AreEqual(0, ProposalPricingResolver.SellPrice(100, 0, 0.1));
    }

    [Test]
    public void LightNames_MatchTheSheetRows()
    {
        Assert.AreEqual("LED / FP", ProposalPricingResolver.HeadModelName(new[]
        {
            "Simeon_Light_8000",
            "Lights - Flat Panel Carrier"
        }));
        Assert.AreEqual("8000 Low Ceiling", ProposalPricingResolver.ArmSelectionName("Simeon_Light_8000_LC"));
        Assert.AreEqual("32in Flat Panel", ProposalPricingResolver.ArmSelectionName("Flat Panel Carrier"));
    }

    SelectablePrice Price(string name)
    {
        var go = new GameObject(name);
        _created.Add(go);
        var price = go.AddComponent<SelectablePrice>();
        price.pricingObjectName = name;
        price.UIObjectName = name;
        return price;
    }
}
