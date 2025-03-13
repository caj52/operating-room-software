using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

public class UI_GeneralScreenLoader : MonoBehaviour
{
  
    public static UI_GeneralScreenLoader _instance;
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(_instance.gameObject);
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
       
    }


    public void UpdateState(bool enable)
    {
        gameObject.SetActive(enable);
    }

}
