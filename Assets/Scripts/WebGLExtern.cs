#if UNITY_WEBGL
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Interface with WebGL javascript
/// </summary>
public static class WebGLExtern
{
    //[DllImport("__Internal")]
    //public static extern void SaveToFile(byte[] data);

    [DllImport("__Internal")]
    public static extern void SaveStringToFile(string data, string extension);

    [DllImport("__Internal")]
    public static extern void SaveElevationPDF(string jsonData);

    //[DllImport("__Internal")]
    //public static extern void OpenJSONTreeFile();

    //[DllImport("__Internal")]
    //private static extern void HelloString(string str);

    //[DllImport("__Internal")]
    //private static extern void PrintFloatArray(float[] array, int size);

    //[DllImport("__Internal")]
    //private static extern int AddNumbers(int x, int y);

    //[DllImport("__Internal")]
    //private static extern string StringReturnValueFunction();

    //[DllImport("__Internal")]
    //private static extern void BindWebGLTexture(int texture);
}
#endif