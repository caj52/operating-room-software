using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Repairs glTFast quirks that produce schema-invalid GLBs Blender (and other
/// strict importers) reject — most commonly empty texture-info objects like
/// <c>"metallicRoughnessTexture":{}</c> with no <c>index</c>.
/// </summary>
public static class GlbFileSanitizer
{
    private static readonly byte[] GlbMagic = Encoding.ASCII.GetBytes("glTF");
    private static readonly byte[] JsonChunkType = Encoding.ASCII.GetBytes("JSON");
    private static readonly byte[] BinChunkType = { (byte)'B', (byte)'I', (byte)'N', 0 };

    /// <summary>
    /// Returns true if the file was already valid or was successfully rewritten.
    /// </summary>
    public static bool SanitizeInPlace(string glbPath)
    {
        if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
            return false;

        byte[] data;
        try
        {
            data = File.ReadAllBytes(glbPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"GLB sanitize: failed to read '{glbPath}': {e.Message}");
            return false;
        }

        if (data.Length < 20
            || data[0] != GlbMagic[0] || data[1] != GlbMagic[1]
            || data[2] != GlbMagic[2] || data[3] != GlbMagic[3])
        {
            Debug.LogError($"GLB sanitize: not a glTF binary file: '{glbPath}'");
            return false;
        }

        uint version = BitConverter.ToUInt32(data, 4);
        uint declaredLength = BitConverter.ToUInt32(data, 8);
        if (version != 2 || declaredLength != (uint)data.Length)
        {
            Debug.LogWarning(
                $"GLB sanitize: header mismatch version={version} declared={declaredLength} actual={data.Length}");
        }

        uint jsonLen = BitConverter.ToUInt32(data, 12);
        if (20 + jsonLen > data.Length)
        {
            Debug.LogError($"GLB sanitize: JSON chunk overruns file: '{glbPath}'");
            return false;
        }

        string jsonText = Encoding.UTF8.GetString(data, 20, (int)jsonLen).TrimEnd(' ', '\0');
        JObject root;
        try
        {
            root = JObject.Parse(jsonText);
        }
        catch (Exception e)
        {
            Debug.LogError($"GLB sanitize: JSON parse failed: {e.Message}");
            return false;
        }

        int fixes = StripInvalidTextureInfos(root);
        fixes += DemoteTextureTransformRequirement(root);

        if (fixes == 0)
            return true;

        byte[] newJson = Encoding.UTF8.GetBytes(root.ToString(Newtonsoft.Json.Formatting.None));
        int jsonPad = (4 - (newJson.Length % 4)) % 4;
        if (jsonPad > 0)
        {
            var padded = new byte[newJson.Length + jsonPad];
            Buffer.BlockCopy(newJson, 0, padded, 0, newJson.Length);
            for (int i = newJson.Length; i < padded.Length; i++)
                padded[i] = (byte)' ';
            newJson = padded;
        }

        int binChunkOffset = 20 + (int)jsonLen;
        if (binChunkOffset + 8 > data.Length)
        {
            Debug.LogError($"GLB sanitize: missing BIN chunk: '{glbPath}'");
            return false;
        }

        uint binLen = BitConverter.ToUInt32(data, binChunkOffset);
        int binDataOffset = binChunkOffset + 8;
        if (binDataOffset + binLen > data.Length)
        {
            Debug.LogError($"GLB sanitize: BIN chunk overruns file: '{glbPath}'");
            return false;
        }

        byte[] binData = new byte[binLen];
        Buffer.BlockCopy(data, binDataOffset, binData, 0, (int)binLen);
        int binPad = (4 - (binData.Length % 4)) % 4;
        if (binPad > 0)
        {
            var padded = new byte[binData.Length + binPad];
            Buffer.BlockCopy(binData, 0, padded, 0, binData.Length);
            binData = padded;
        }

        int total = 12 + 8 + newJson.Length + 8 + binData.Length;
        var output = new byte[total];
        Buffer.BlockCopy(GlbMagic, 0, output, 0, 4);
        BitConverter.GetBytes(2u).CopyTo(output, 4);
        BitConverter.GetBytes((uint)total).CopyTo(output, 8);
        BitConverter.GetBytes((uint)newJson.Length).CopyTo(output, 12);
        Buffer.BlockCopy(JsonChunkType, 0, output, 16, 4);
        Buffer.BlockCopy(newJson, 0, output, 20, newJson.Length);

        int outBinHeader = 20 + newJson.Length;
        BitConverter.GetBytes((uint)binData.Length).CopyTo(output, outBinHeader);
        Buffer.BlockCopy(BinChunkType, 0, output, outBinHeader + 4, 4);
        Buffer.BlockCopy(binData, 0, output, outBinHeader + 8, binData.Length);

        try
        {
            File.WriteAllBytes(glbPath, output);
            Debug.Log($"GLB sanitize: applied {fixes} fix(es) → {glbPath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"GLB sanitize: failed to write '{glbPath}': {e.Message}");
            return false;
        }
    }

    private static int StripInvalidTextureInfos(JObject root)
    {
        int fixes = 0;
        var materials = root["materials"] as JArray;
        if (materials == null)
            return 0;

        foreach (var token in materials)
        {
            if (token is not JObject material)
                continue;

            if (material["pbrMetallicRoughness"] is JObject pbr)
            {
                fixes += RemoveIfMissingIndex(pbr, "baseColorTexture");
                fixes += RemoveIfMissingIndex(pbr, "metallicRoughnessTexture");
            }

            fixes += RemoveIfMissingIndex(material, "normalTexture");
            fixes += RemoveIfMissingIndex(material, "occlusionTexture");
            fixes += RemoveIfMissingIndex(material, "emissiveTexture");
        }

        return fixes;
    }

    private static int RemoveIfMissingIndex(JObject parent, string propertyName)
    {
        var tex = parent[propertyName];
        if (tex == null || tex.Type == JTokenType.Null)
            return 0;

        if (tex is not JObject obj || obj["index"] == null || obj["index"].Type != JTokenType.Integer)
        {
            parent.Remove(propertyName);
            return 1;
        }

        return 0;
    }

    private static int DemoteTextureTransformRequirement(JObject root)
    {
        // Keep the extension in extensionsUsed; requiring it rejects importers that
        // tolerate identity transforms without implementing the extension.
        var required = root["extensionsRequired"] as JArray;
        if (required == null)
            return 0;

        int removed = 0;
        for (int i = required.Count - 1; i >= 0; i--)
        {
            if (required[i]?.ToString() == "KHR_texture_transform")
            {
                required.RemoveAt(i);
                removed++;
            }
        }

        if (required.Count == 0)
            root.Remove("extensionsRequired");

        return removed;
    }
}
