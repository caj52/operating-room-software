using UnityEngine;

namespace SplenSoft.AssetBundles
{
    public static class Log
    {
        public static void Write(LogLevel level, string message)
        {
            var settings = AssetBundleManagerSettings.Get();
            if ((int)settings.LogLevel <= (int)level)
            {
                switch (level)
                {
                    case LogLevel.Log:
                    case LogLevel.Verbose:
                        Debug.Log(message);
                        break;
                    case LogLevel.Warning:
                        Debug.LogWarning(message);
                        break;
                    case LogLevel.Error:
                        Debug.LogError(message);
                        break;
                    default:
                        throw new System.Exception(
                            $"Unhandled log type {level}");
                }
            }
        }
    }
}