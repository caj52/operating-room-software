using SplenSoft.UnityUtilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UI_DbPassword : MonoBehaviour
{
    public static UI_DbPassword Instance { get; private set; }

    [field: SerializeField]
    public TextMeshProUGUI Heading { get; set; }

    [field: SerializeField] 
    public TMP_InputField InputField_OldPassword { get; set; }

    [field: SerializeField] 
    public TMP_InputField InputField_Password { get; set; }

    [field: SerializeField] 
    public TextMeshProUGUI Text_EnterOldPassword { get; set; }

    [field: SerializeField] 
    public TextMeshProUGUI Text_EnterPassword { get; set; }

    [field: SerializeField] 
    public TextMeshProUGUI Text_PasswordDirections { get; set; }

    [field: SerializeField] 
    public Button ButtonSubmit { get; set; }

    [field: SerializeField] 
    public Button ButtonCancel { get; set; }

    private const string _passwordDirections = "Password must have eight characters, at least one number, at least one uppercase letter, at least one lowercase letter and at least one special character.";

    public void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InputField_Password.onValueChanged.AddListener(ValueChanged);
        gameObject.SetActive(false);

        ButtonCancel.onClick
            .AddListener(() => 
            {
                if (SceneManager.GetActiveScene().name != "Start")
                {
                    SceneLoadDiagnostics.MarkTransitionStart("→Start");
                    SceneManager.LoadScene("Start");
                }
                gameObject.SetActive(false);
            });
    }

    private void OnDestroy()
    {
        InputField_Password.onValueChanged.RemoveListener(ValueChanged);
    }

    private void OnEnable()
    {
        ButtonSubmit.interactable = false;
    }

    private void OnDisable()
    {
        Text_EnterPassword.text = string.Empty;
        Text_EnterOldPassword.text = string.Empty;
        Instance.ButtonSubmit.onClick.RemoveAllListeners();
    }

    private void ValueChanged(string arg0)
    {
        bool valid = !Regex.IsMatch(
            arg0, 
            @"^(.{0,7}|[^0-9]*|[^A-Z]*|[^a-z]*|[a-zA-Z0-9]*)$");

        ButtonSubmit.interactable = valid;
    }

    public static async void OpenChangePassword()
    {
        while (Instance == null)
        {
            await Task.Yield();
            if (!Application.isPlaying)
                throw new AppQuitInTaskException();
        }
        Instance.Heading.text = "Change Password";
        Instance.Text_EnterOldPassword.gameObject.SetActive(true);
        Instance.Text_EnterOldPassword.text = "Enter current password";
        Instance.InputField_OldPassword.gameObject.SetActive(true);
        Instance.Text_PasswordDirections.gameObject.SetActive(true);
        Instance.Text_PasswordDirections.text = _passwordDirections;
        Instance.Text_EnterPassword.text = "Enter new password";

        Instance.ButtonSubmit.onClick.RemoveAllListeners();
        Instance.ButtonSubmit.onClick.AddListener(Instance.ChangePassword);

        Instance.gameObject.SetActive(true);
    }

    public static async void OpenEnterPassword()
    {
        while (Instance == null)
        {
            await Task.Yield();
            if (!Application.isPlaying)
                throw new AppQuitInTaskException();
        }
        Instance.Heading.text = "Login";
        Instance.Text_EnterOldPassword.gameObject.SetActive(false);
        Instance.InputField_OldPassword.gameObject.SetActive(false);
        Instance.Text_PasswordDirections.gameObject.SetActive(false);
        Instance.Text_EnterPassword.text = "Enter password";
        Instance.ButtonSubmit.onClick.AddListener(Instance.ValidatePassword);
        Instance.gameObject.SetActive(true);
    }

    public async void ChangePassword()
    {
        ToggleStates(false);

        var task = Database.ChangePassword
            (InputField_Password.text, 
            InputField_OldPassword.text);

        await task;

        if (!Application.isPlaying)
            throw new Exception("App quit during task");

        if (task.Result)
        {
            gameObject.SetActive(false);
        }

        ToggleStates(true);
    }

    private void ToggleStates(bool toggle)
    {
        InputField_OldPassword.interactable = toggle;
        InputField_Password.interactable = toggle;
        ButtonSubmit.interactable = toggle;
        ButtonCancel.interactable = toggle;
    }

    public async void ValidatePassword()
    {
        ToggleStates(false);

        Debug.LogError(InputField_Password.text);
      var task = Database.ValidatePassword(InputField_Password.text); // Anwar
     //   var task = Database.ValidatePassword("Mudabbir6900");

        await task;

        if (!Application.isPlaying)
            throw new Exception("App quit during task");

        ToggleStates(true);

        if (task.Result)
        {
            gameObject.SetActive(false);
        }
    }
}
