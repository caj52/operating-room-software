using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

public static class Angle180Parser
{
    /// <summary>Wrap a degree value into (-180, 180].</summary>
    public static float ToSigned180(float deg)
    {
        // Robust wrap that handles negatives and large values
        float wrapped = deg % 360f;
        if (wrapped <= -180f) wrapped += 360f;
        else if (wrapped > 180f) wrapped -= 360f;
        return wrapped;
    }

    /// <summary>Wrap each Euler axis to (-180, 180].</summary>
    public static Vector3 ToSigned180(Vector3 eulerDeg)
    {
        return new Vector3(
            ToSigned180(eulerDeg.x),
            ToSigned180(eulerDeg.y),
            ToSigned180(eulerDeg.z)
        );
    }

    /// <summary>
    /// Convert a Quaternion to signed Euler degrees in (-180, 180] per axis.
    /// </summary>
    public static Vector3 NormalizeEuler180(Quaternion q)
    {
        return ToSigned180(q.eulerAngles);
    }

    /// <summary>
    /// Parse a string like "324, 10, -725" (also supports spaces/semicolons/degree symbols/brackets)
    /// and output a Vector3 with each axis wrapped to (-180, 180].
    /// Returns false if parsing fails.
    /// </summary>
    public static bool TryParseSignedEuler180(string input, out Vector3 signedEuler)
    {
        signedEuler = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // Remove brackets and degree symbols
        string cleaned = Regex.Replace(input, @"[\[\]\(\)°]", "");
        // Split on comma/semicolon/whitespace
        string[] parts = Regex.Split(cleaned, @"[\s,;]+", RegexOptions.CultureInvariant);

        if (parts.Length < 3) return false;

        if (TryParseFloat(parts[0], out float x) &&
            TryParseFloat(parts[1], out float y) &&
            TryParseFloat(parts[2], out float z))
        {
            signedEuler = ToSigned180(new Vector3(x, y, z));
            return true;
        }
        return false;
    }

    private static bool TryParseFloat(string s, out float value)
    {
        return float.TryParse(s,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }
}
