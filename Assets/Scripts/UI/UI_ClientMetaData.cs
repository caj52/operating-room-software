using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class UI_ClientMetaData : MonoBehaviour
{
    private static UI_ClientMetaData Instance { get; set; }

    public static UnityEvent OnClosed { get; set; } = new();

    public static string AccountName => 
        Instance.InputFieldAccountName.text ?? "N/A";
    [field: SerializeField] 
    private TMP_InputField InputFieldAccountName 
    { get; set; }

    public static string AccountAddressLine1 => 
        Instance.InputFieldAccountAddressLine1.text ?? "N/A";
    [field: SerializeField]
    private TMP_InputField InputFieldAccountAddressLine1
    { get; set; }

    public static string AccountAddressLine2 => 
        Instance.InputFieldAccountAddressLine2.text ?? "N/A";
    [field: SerializeField]
    private TMP_InputField InputFieldAccountAddressLine2
    { get; set; }

    public static string ProjectName => 
        Instance.InputFieldProjectName.text ?? "N/A";
    [field: SerializeField]
    private TMP_InputField InputFieldProjectName
    { get; set; }

    public static string ProjectNumber => 
        Instance.InputFieldProjectNumber.text ?? "N/A";
    [field: SerializeField]
    private TMP_InputField InputFieldProjectNumber
    { get; set; }

    public static string OrderReferenceNumber => 
        Instance.InputFieldOrderReferenceNumber.text ?? "N/A";
    [field: SerializeField]
    private TMP_InputField InputFieldOrderReferenceNumber
    { get; set; }

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        OnClosed?.Invoke();
    }

    public static void Open()
    {
        Instance.gameObject.SetActive(true);
    }
}