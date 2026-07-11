using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoomConfigLoader : MonoBehaviour
{
    public static RoomConfigLoader Instance;
    [field: SerializeField] public Transform contentView { get; private set; }
    [field: SerializeField] public ScrollRect scroll { get; private set; }
    [field: SerializeField] public GameObject filePrefab { get; private set; }

    void Awake()
    {
        Instance = this;
        Debug.Log(ConfigurationManager.GetSavedRoomsFolder());
        string savedFolder = ConfigurationManager.GetSavedRoomsFolder();
        if (Directory.Exists(savedFolder))
        {
            string[] files = Directory.GetFiles(savedFolder);
            foreach (string f in files.Where(x => x.EndsWith(".json")))
            {
                GenerateRoomItem(f);
            }
        }

        gameObject.SetActive(false);
    }

    public void GenerateRoomItem(string f)
    {
        RefreshOrAddRoomItem(f);
    }

    /// <summary>Adds a load-list entry, or no-ops if that room name is already listed.</summary>
    public void RefreshOrAddRoomItem(string f)
    {
        if (contentView == null || filePrefab == null || string.IsNullOrWhiteSpace(f))
            return;

        string display = Path.GetFileName(f)
            .Replace(".json", "")
            .Replace("_", " ");

        foreach (Transform child in contentView)
        {
            var label = child.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text == display)
                return;
        }

        GameObject go = Instantiate(filePrefab, Vector3.zero, Quaternion.identity);
        go.transform.SetParent(contentView);
        go.GetComponentInChildren<TMP_Text>().text = display;
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            ConfigurationManager.Instance.LoadRoom(f);
            Instance.gameObject.SetActive(false);
            transform.root.gameObject.SetActive(false);
        });
        go.transform.localScale = new Vector3(1, 1, 1);
    }
}
