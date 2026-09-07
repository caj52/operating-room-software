using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Deterministic diagnostics for boom/service-head config save/load.
/// Off by default — set <see cref="Enabled"/> true when investigating load issues.
/// </summary>
public static class BoomConfigLoadDiag
{
    const string Prefix = "[BoomConfigLoad]";

    /// <summary>When false, all logging is a no-op (default).</summary>
    public static bool Enabled = true;

    static string _sessionPath;
    static readonly StringBuilder _buffer = new StringBuilder(8 * 1024);

    public static string LastSessionPath => _sessionPath;

    public static void BeginSession(string reason)
    {
        if (!Enabled)
            return;
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "Logs");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _sessionPath = Path.Combine(dir, $"BoomConfigLoad_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            _buffer.Clear();
            Event("SESSION", $"{reason} path={_sessionPath}");
        }
        catch (Exception ex)
        {
            _sessionPath = null;
            Debug.LogWarning($"{Prefix} failed to open session log: {ex.Message}");
        }
    }

    public static void EndSession(string summary = null)
    {
        if (!Enabled)
            return;
        if (!string.IsNullOrEmpty(summary))
            Event("SESSION_END", summary);
        Flush();
        if (!string.IsNullOrEmpty(_sessionPath))
            Debug.Log($"{Prefix} full log: {_sessionPath}");
    }

    public static void Event(string phase, string message)
    {
        if (!Enabled)
            return;
        string line = $"{DateTime.Now:HH:mm:ss.fff} {phase}: {message}";
        Debug.Log($"{Prefix} {line}");
        lock (_buffer)
        {
            _buffer.AppendLine(line);
            if (_buffer.Length > 200_000)
                Flush();
        }
    }

    public static void Flush()
    {
        if (!Enabled || string.IsNullOrEmpty(_sessionPath))
            return;
        try
        {
            string chunk;
            lock (_buffer)
            {
                if (_buffer.Length == 0)
                    return;
                chunk = _buffer.ToString();
                _buffer.Clear();
            }
            File.AppendAllText(_sessionPath, chunk);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{Prefix} flush failed: {ex.Message}");
        }
    }
}
