using UnityEngine;
using UnityEngine.UI;

public class TabManager : MonoBehaviour
{
    public Button tab1ButtonItem;
    public Button tab2ButtonAdditionalPrice;
    public Button tab3ButtonExportQuote;
    public GameObject tab1Panel;
    public GameObject tab2Panel;
    public GameObject tab3Panel;

    private Color selectedColor = new Color(0.8f, 0.8f, 0.8f, 1f); // Light Gray
    private Color defaultColor = Color.white; // Normal Button Color

    private void Start()
    {
        // Add listeners for button clicks
        tab1ButtonItem.onClick.AddListener(() => OpenTab(tab1ButtonItem, tab1Panel));
        tab2ButtonAdditionalPrice.onClick.AddListener(() => OpenTab(tab2ButtonAdditionalPrice, tab2Panel));

        if (tab3ButtonExportQuote != null)
            tab3ButtonExportQuote?.onClick.AddListener(() => OpenTab(tab3ButtonExportQuote, tab3Panel));

        // Initialize default active tab
        OpenTab(tab1ButtonItem, tab1Panel);
    }

    private void OpenTab(Button activeButton, GameObject activePanel)
    {
        // Deactivate all panels
        tab1Panel.SetActive(false);
        tab2Panel.SetActive(false);
        if (tab3Panel != null)
            tab3Panel.SetActive(false);

        // Activate the selected panel
        activePanel.SetActive(true);

        // Reset all button colors to default
        tab1ButtonItem.GetComponent<Image>().color = defaultColor;
        tab2ButtonAdditionalPrice.GetComponent<Image>().color = defaultColor;
        if(tab3ButtonExportQuote != null)
            tab3ButtonExportQuote.GetComponent<Image>().color = defaultColor;

        // Change the active button color to indicate the active tab
        activeButton.GetComponent<Image>().color = selectedColor;
    }
}
