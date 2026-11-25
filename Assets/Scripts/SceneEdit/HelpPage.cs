using TMPro;
using UnityEngine;

public class HelpPage : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI bodyText;

    public void Apply(string text)
    {
        if (bodyText == null || string.IsNullOrEmpty(text))
            return;

        bodyText.richText = true;
        bodyText.text = text;
    }
}
