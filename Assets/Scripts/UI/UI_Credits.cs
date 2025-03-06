using SplenSoft.UnityUtilities;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(FullScreenMenu))]
public class UI_Credits : MonoBehaviour
{
    private static UI_Credits Instance { get; set; }

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(false);
    }

    public static async void Open()
    {
        while (Instance == null) 
        { 
            await Task.Yield();

            if (!Application.isPlaying)
                throw new AppQuitInTaskException();
        }
        Instance.gameObject.SetActive(true);
    }
}
