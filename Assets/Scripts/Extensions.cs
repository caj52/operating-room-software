using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static partial class Extensions
{

    public static Quaternion GetNormalized(this Quaternion q)
    {
        float f = 1f / Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return new Quaternion(q.x * f, q.y * f, q.z * f, q.w * f);
    }
    public static bool Contains(this string str, string substring,
                            StringComparison comp)
    {
        if (substring == null)
            throw new ArgumentNullException("substring",
                                            "substring cannot be null.");
        else if (!Enum.IsDefined(typeof(StringComparison), comp))
            throw new ArgumentException("comp is not a member of StringComparison",
                                        "comp");

        return str.IndexOf(substring, comp) >= 0;
    }

    public static T GetComponent<T>(this GameObject obj, out T component)
    {
        component = obj.GetComponent<T>();
        return component;
    }

    public static T GetOrAddComponent<T>(this GameObject obj) where T : Component
    {
        var component = obj.GetComponent<T>();
        if (component == null)
        {
            component = obj.AddComponent<T>();
        }

        return component;
    }

    public static int GetParentCount(this Transform transform)
    {
        int count = 0;
        Transform parent = transform.parent;

        while (parent != null)
        {
            count++;
            parent = parent.parent;
        }

        return count;
    }

    public static int ActiveChildCount(this Transform transform)
    {
        int activeCount = 0;
        transform.ForEachChild(child =>
        {
            if (child.gameObject.activeSelf)
                activeCount++;
        });
        return activeCount;
    }

    public static void ForEachChild(this Transform transform, Action<Transform> action)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            action.Invoke(transform.GetChild(i));
        }
    }

    public static List<Transform> GetChildren(this Transform transform)
    {
        List<Transform> children = new List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            children.Add(transform.GetChild(i));
        }
        return children;
    }

    public static void SetActiveConditional(this GameObject obj, bool cond)
    {
        if (obj.activeSelf != cond)
            obj.SetActive(cond);
    }

    public static void RotateTowardPlayerCamera(this GameObject obj, Vector3 cameraPosition, bool clampRotation = false)
    {
        var vec = obj.transform.position - cameraPosition;
        vec = vec.normalized;
        vec = new Vector3(vec.x, clampRotation ? Mathf.Clamp(vec.y / 0.75f, -0.3f, 0.3f) : vec.y, vec.z);
        var quat = Quaternion.LookRotation(vec);
        obj.transform.SetPositionAndRotation(obj.transform.position, quat);
    }

    /// <summary>
    /// Rotates an object so it faces the closest point on the main camera plane
    /// </summary>
    /// <param name="obj"></param>
    public static void RotateTowardCameraPlane(this GameObject obj, Camera camera = null)
    {
        if (!camera) camera = Camera.main;
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        Plane nearFrustrumPlane = planes[4];

        Vector3 planePoint = nearFrustrumPlane.ClosestPointOnPlane(obj.transform.position);
        obj.RotateTowardPlayerCamera(planePoint);
    }

    public static Type[] GetAllDerivedTypes(this AppDomain aAppDomain, Type aType)
    {
        var result = new List<Type>();
        var assemblies = aAppDomain.GetAssemblies();

        Array.ForEach(assemblies, assembly =>
        Array.ForEach(assembly.GetTypes(), type =>
        {
            if (type.IsSubclassOf(aType))
            {
                result.Add(type);
            }
        }));

        return result.ToArray();
    }

    internal static float ToFeet(this float numberInMeters)
    {
        return numberInMeters * 3.28084f;
    }

    internal static float ToMeters(this float numberInFeet)
    {
        return numberInFeet * 0.3048f;
    }

    /// <summary>
    /// Returns true if instance is a singleton, false if it will be destroyed
    /// </summary>
    internal static bool EnsureSingleton<T>(this GameObject gameObject, ref T instance)
    {
        if (instance == null)
        {
            instance = gameObject.GetComponent<T>();
            return true;
        }
        else
        {
            Debug.LogError($"GameObject {gameObject.name} with script " +
            $"{typeof(T)} was not a singleton! It will be destroyed.");

            UnityEngine.Object.Destroy(gameObject);
            return false;
        }
    }

    /// <summary>
    /// Returns current singleton if it exists, or assigns new singleton. Will destroy duplicates
    /// </summary>
    internal static T EnsureSingleton<T>(this GameObject gameObject, T instance)
    {
        if (instance == null)
        {
            return gameObject.GetComponent<T>();
        }
        else
        {
            Debug.LogError($"GameObject {gameObject.name} with script " +
            $"{typeof(T)} was not a singleton! It will be destroyed.");

            UnityEngine.Object.Destroy(gameObject);
            return instance;
        }
    }

    internal static void ForEach<TKey, TValue>(this Dictionary<TKey, TValue> dict, Action<TKey, TValue> action)
    {
        if (dict == null) throw new ArgumentNullException("dict");
        if (action == null) throw new ArgumentNullException("action");

        foreach (KeyValuePair<TKey, TValue> item in dict)
        {
            action(item.Key, item.Value);
        }
    }

    internal static void RedefineElements<T>(this List<T> list, Func<T, T> action)
    {
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = action(list[i]);
        }
    }

    internal static void AddIfNotNull<T>(this List<T> list, T item)
    {
        if (item != null)
        {
            list.Add(item);
        }
    }

    internal static void AddIfNotNullOrWhiteSpace<T>(this List<T> list, T item)
    {
        if (item != null)
        {
            if (!(item is string) || (item is string text && !string.IsNullOrWhiteSpace(text)))
                list.Add(item);
        }
    }

    internal static List<T> RemoveNull<T>(this List<T> list)
    {
        return list.Where(item => item != null).ToList();
    }

    internal static List<T> RemoveNullOrWhiteSpace<T>(this List<T> list)
    {
        return list.Where(item => item.IsNotNullOrWhiteSpace()).ToList();
    }

    private static bool IsNotNullOrWhiteSpace(this object item)
    {
        return item != null && (!(item is string) || (item is string text && !string.IsNullOrWhiteSpace(text)));
    }

    internal static void ForEachNotNullOrEmpty<T>(this List<T> source, Action<T> action)
    {
        foreach (var item in source.Where(item => item != null))
        {
            if (item is string text && string.IsNullOrEmpty(text))
            {
                continue;
            }
            action(item);
        }
    }

    internal static void ForEachNotNull<T>(this List<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            if (item != null)
                action(item);
        }
    }

    internal static void ForEachNotNullOrWhiteSpace<T>(this List<T> source, Action<T> action)
    {
        foreach (var item in source.Where(item => item != null))
        {
            if (item is string text && string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            action(item);
        }
    }

    internal static void AddIfNotContained<T>(this List<T> source, T item)
    {
        if (!source.Contains(item))
        {
            source.Add(item);
        }
    }

#if UNITY_EDITOR
    internal static void CenterOnMainWin(this EditorWindow aWin)
    {
        var main = GetEditorMainWindowPos();
        var pos = aWin.position;
        float w = (main.width - pos.width) * 0.5f;
        float h = (main.height - pos.height) * 0.5f;
        pos.x = main.x + w;
        pos.y = main.y + h;
        aWin.position = pos;
    }

    private static Rect GetEditorMainWindowPos()
    {
        var containerWinType = AppDomain.CurrentDomain
            .GetAllDerivedTypes(typeof(ScriptableObject))
            .Where(t => t.Name == "ContainerWindow").FirstOrDefault();

        if (containerWinType == null)
        {
            throw new MissingMemberException("Can't find internal " +
                "type ContainerWindow. Maybe something has changed " +
                "inside Unity");
        }

        var showModeField = containerWinType.GetField("m_ShowMode",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        var positionProperty = containerWinType.GetProperty("position",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance);

        if (showModeField == null || positionProperty == null)
        {
            throw new MissingFieldException("Can't find internal " +
                "fields 'm_ShowMode' or 'position'. Maybe something " +
                "has changed inside Unity");
        }

        var windows = Resources.FindObjectsOfTypeAll(containerWinType);
        foreach (var win in windows)
        {
            var showmode = (int)showModeField.GetValue(win);
            if (showmode == 4) // main window
            {
                var pos = (Rect)positionProperty.GetValue(win, null);
                return pos;
            }
        }

        throw new NotSupportedException("Can't find internal main " +
            "window. Maybe something has changed inside Unity");
    }
#endif
}