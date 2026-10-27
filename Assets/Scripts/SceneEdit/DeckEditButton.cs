using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class DeckEditButton : MonoBehaviour
{
    private Button btn;
    private bool isLoading;

    void Awake()
    {
        btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        if (isLoading) return;
        isLoading = true;
        StartCoroutine(LoadAfterFade());
    }

    private IEnumerator LoadAfterFade()
    {
        yield return ButtonPressFade.Wait(btn);
        SceneChanger.Load("DeckEditScene");
    }
}
