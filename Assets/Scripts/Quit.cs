using UnityEditor;
using UnityEngine;

/// <summary>
/// Used for a button event in the main menu
/// </summary>
public class Quit : MonoBehaviour
{
    public void QuitApp()
    {
        Application.Quit();
    }
}