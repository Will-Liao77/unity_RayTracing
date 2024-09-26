using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Dummiesman;
using SimpleFileBrowser;

public class ObjFileBrowser : MonoBehaviour
{
    //private bool objLoaded = false;
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

    //public bool CheckObjLoadStatus()
    //{
    //    return objLoaded;
    //}

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

            //objLoaded = true;
        }
    }

    void OnFilesSelected(string[] filePaths)
    {
        string filePath = filePaths[0];
        // Debug.Log("Selected file: " + filePath);

        GameObject obj = new OBJLoader().Load(filePath);
    }
}
