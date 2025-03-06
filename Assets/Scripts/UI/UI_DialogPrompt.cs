using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_DialogPrompt : MonoBehaviour
{
    private static UI_DialogPrompt Instance { get; set; }
    [field: SerializeField] private Button ButtonTemplate { get; set; }
    [field: SerializeField] private TextMeshProUGUI Text { get; set; }

    private List<Button> _instantiatedButtons = new();

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(false);
    }

    public static void Open(string text, params ButtonAction[] buttonActions)
    {
        Instance.Text.text = text;

        Instance.ButtonTemplate.gameObject.SetActive(true);

        while (Instance._instantiatedButtons.Count < Math.Max(buttonActions.Length, 1)) 
        {
            var newObj = Instantiate(Instance.ButtonTemplate.gameObject, 
                Instance.ButtonTemplate.transform.parent);

            Instance._instantiatedButtons.Add (newObj.GetComponent<Button>());
        }

        Instance.ButtonTemplate.gameObject.SetActive(false);

        Instance._instantiatedButtons.ForEach(button => button.gameObject.SetActive(false));

        if (buttonActions.Length == 0) 
        {
            var actions = buttonActions.ToList();
            actions.Add(new ButtonAction("OK"));
            buttonActions = actions.ToArray();
        }

        for (int i = 0; i < buttonActions.Length; i++)
        {
            var button = Instance._instantiatedButtons[i];
            var buttonAction = buttonActions[i];

            button.gameObject.SetActive(true);

            button.GetComponentInChildren<TextMeshProUGUI>()
                .text = buttonAction.ButtonText.ToString();

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => buttonAction.Action?.Invoke());

            if (buttonAction.Action == null)
                button.onClick.AddListener(() => Instance.gameObject.SetActive(false));
        }
        Instance.gameObject.SetActive(true);
    }

    public static void Close()
    {
        Instance.gameObject.SetActive(false);
    }
}

public class ButtonAction
{
    public ButtonAction() { }

    public ButtonAction(string text) 
    { 
        ButtonText = text;
    }

    public ButtonAction(string buttonText, Action action)
    {
        ButtonText = buttonText;
        Action = action;
    }

    public string ButtonText { get; set; }
    public Action Action { get; set; }
}