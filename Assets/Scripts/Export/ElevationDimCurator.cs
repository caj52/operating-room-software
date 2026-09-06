using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cutsheet length dims = PDF catalog table rows only
/// (<see cref="ElevationDimPolicy"/> / AssemblyData.OrderedSelectables).
/// </summary>
public static class ElevationDimCurator
{
    public static Dictionary<Selectable, (Measurable measurable, float sizeM)> BuildLengthOwners(
        IList<Selectable> assemblySelectables)
    {
        var lengthByOwner = new Dictionary<Selectable, (Measurable measurable, float sizeM)>();

        foreach (var row in CollectCatalogTableOwners())
        {
            if (row == null)
                continue;

            if (!ElevationDimPolicy.TryResolveCatalogLengthClaim(row, out var claim))
                continue;

            Selectable owner = claim.Owner;
            if (lengthByOwner.ContainsKey(owner))
                continue;

            Measurable measurable = ElevationCutsheetPass.EnsureToOriginForLengthOwner(
                owner, lengthByOwner, assemblySelectables);
            if (measurable == null)
            {
                Debug.LogWarning(
                    $"[ElevDim] catalog row owner={owner.name} has no ToOrigin Measurable — skipped " +
                    $"{{{ElevationLengthFormat.DiagnoseSizeBinding(owner)}}}",
                    owner);
                continue;
            }

            lengthByOwner[owner] = (measurable, claim.LengthM);
        }

        return lengthByOwner;
    }

    /// <summary>
    /// OrderedSelectables rows that may own a catalog length (own Size or primary PdfData).
    /// </summary>
    public static IEnumerable<Selectable> CollectCatalogTableOwners()
    {
        var seen = new HashSet<Selectable>();
        var datas = ElevationExportContext.AssemblyDatas;
        if (datas == null || datas.Count == 0)
        {
            Debug.LogWarning("[ElevDim] CURATE AssemblyDatas null/empty — zero catalog length dims");
            yield break;
        }

        foreach (var assembly in datas)
        {
            if (assembly?.OrderedSelectables == null)
                continue;
            foreach (var sel in assembly.OrderedSelectables)
            {
                if (sel == null || !seen.Add(sel))
                    continue;
                // Own length only — related Size inheritance is not ownership.
                if (ElevationLengthFormat.ResolveOwnSizeMeters(sel) <= 0f
                    && ElevationLengthFormat.ExtractPdfDataLengthMm(sel) <= 0)
                    continue;
                yield return sel;
            }
        }
    }
}
