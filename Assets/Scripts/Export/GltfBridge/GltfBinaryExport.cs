using System.Threading.Tasks;
using GLTFast.Export;
using GLTFast.Logging;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Thin bridge into glTFast.Export (not auto-referenced by default).
/// </summary>
public static class GltfBinaryExport
{
    public static async Task<bool> ExportGameObjectToGlbAsync(GameObject root, string glbPath, string sceneName)
    {
        if (root == null || string.IsNullOrWhiteSpace(glbPath))
            return false;

        var logger = new CollectingLogger();
        var exportSettings = new ExportSettings
        {
            Format = GltfFormat.Binary,
            FileConflictResolution = FileConflictResolution.Overwrite,
            ImageDestination = ImageDestination.MainBuffer,
        };
        var goSettings = new GameObjectExportSettings
        {
            OnlyActiveInHierarchy = false,
            DisabledComponents = false,
        };

        var exporter = new GameObjectExport(exportSettings, goSettings, logger: logger);
        bool added = exporter.AddScene(
            new[] { root },
            (float4x4)root.transform.worldToLocalMatrix,
            string.IsNullOrWhiteSpace(sceneName) ? root.name : sceneName);

        if (!added)
        {
            logger.LogAll();
            return false;
        }

        bool success = await exporter.SaveToFileAndDispose(glbPath);
        if (!success)
            logger.LogAll();
        return success;
    }
}
