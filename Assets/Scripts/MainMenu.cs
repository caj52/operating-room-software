using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MainMenu : MonoBehaviour
{
    [field: SerializeField]
    public UnityEvent OnPasswordSuccess { get; private set; } // load the object editor

    [field: SerializeField] private GameObject ButtonLogOut { get; set; }

    private void Awake()
    {
        Database.OnPasswordValidationAttemptCompleted
            .AddListener(UpdateStates);
    }

    private void OnDestroy()
    {
        Database.OnPasswordValidationAttemptCompleted
            .RemoveListener(UpdateStates);
    }

    private void Start()
    {
        UpdateStates();
        
       
    }

   
    private void UpdateStates()
    {
        ButtonLogOut.SetActive(!Database.MustEnterPassword);
    }

    public void OpenObjectEditor()
    {
        StartCoroutine(OpenObjectEditorCoroutine());
    }

    public void LogOut()
    {
        Database.LogOut();
    }

    public void OpenChangePassword()
    {
        UI_DbPassword.OpenChangePassword();
    }

    private IEnumerator OpenObjectEditorCoroutine()
    {
        var task = Database.ValidateSession();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Result == true) 
        { 
            OnPasswordSuccess?.Invoke();
            yield break;
        }

        UI_DbPassword.OpenEnterPassword();

        yield return new WaitUntil(() => 
            !UI_DbPassword.Instance.gameObject.activeSelf);

        if (!Database.MustEnterPassword)
        {
            OnPasswordSuccess?.Invoke();
        }
    }
}
