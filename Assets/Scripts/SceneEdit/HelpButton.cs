using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class HelpButton : MonoBehaviour
{
    Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(ToggleHelp);
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(ToggleHelp);
    }

    void ToggleHelp()
    {
        HelpWindow window = FindObjectOfType<HelpWindow>(true);
        if (window == null)
            return;

        window.Toggle();
    }
}
