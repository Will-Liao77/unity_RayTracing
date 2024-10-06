using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Dummiesman;
using SimpleFileBrowser;
using Unity.VisualScripting;

public class ObjFileBrowser : MonoBehaviour
{
    public string objName;
    // Start is called before the first frame update
    void Start()
    {
        SimpleFileBrowser.FileBrowser.SetFilters(true, new FileBrowser.Filter("OBJ Files", ".obj"));
        SimpleFileBrowser.FileBrowser.SetDefaultFilter(".obj");
        SimpleFileBrowser.FileBrowser.AddQuickLink("Users", "C:\\Users", null);
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void ButtonClick()
    {
        StartCoroutine(ShowLoadDialogCoroutine());
    }

    IEnumerator ShowLoadDialogCoroutine()
    {
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, true, null, null, "Load File", "Load");

        if (FileBrowser.Success)
        {
            // Debug.Log("Selected file: " + FileBrowser.Result);
            // LoadObj(FileBrowser.Result);
            OnFilesSelected(FileBrowser.Result);
        }
    }

    void OnFilesSelected(string[] filePaths)
    {
        string filePath = filePaths[0];
        //Debug.Log("Selected file: " + filePath);

        GameObject obj = new OBJLoader().Load(filePath);

        string[] path = filePath.Split('\\');
        string[] name = path[path.Length - 1].Split('.');
        objName = name[0];
        //Debug.Log(obj.name);

        if (obj.GetComponent<RayTracingObject>() != null)
        {
            Debug.Log("RayTracingMaster script already exists on " + obj.name);
        }
        else
        {
            // get obj child and addComponent
            obj.GetComponentInChildren<MeshFilter>().gameObject.AddComponent<RayTracingObject>();
            //Debug.Log("RayTracingMaster script added to " + obj.name);
        }

        RayTracingMaster rayTracingMaster = FindObjectOfType<RayTracingMaster>();
        if (rayTracingMaster != null)
        {
            rayTracingMaster._shouldRender = true;
        }
        else
        {
            Debug.LogError("RayTracingMaster not found in the scene.");
        }
    }
}
