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
        if (Directory.Exists(Application.persistentDataPath + "/Saved/"))
        {
            string[] files = Directory.GetFiles(Application.persistentDataPath + "/Saved/");
            foreach (string f in files.Where(x => x.EndsWith(".json")))
            {
                GenerateRoomItem(f);
            }
        }

        gameObject.SetActive(false);
    }

    public void GenerateRoomItem(string f)
    {
        GameObject go = Instantiate(filePrefab, Vector3.zero, Quaternion.identity);

        go.transform.SetParent(contentView);
        go.GetComponentInChildren<TMP_Text>().text = Path.GetFileName(f)
                                                    .Replace(".json", "")
                                                    .Replace("_", " ");
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            ConfigurationManager.Instance.LoadRoom(f);
            Instance.gameObject.SetActive(false);
            transform.root.gameObject.SetActive(false);
        });
        go.transform.localScale = new Vector3(1,1,1);
    }
}
