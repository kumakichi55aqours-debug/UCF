using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MessageManager : MonoBehaviour
{
    public static MessageManager Instance { get; private set; }

    [SerializeField] private GameObject messageWindowPrefab;
    private GameObject currentMessageWindow;
    private Button confirmButton;
    private Action confirmAction;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// メッセージパネルを表示する。
    /// onConfirm を渡したときだけ決定ボタンを出す。閉じるのは HideMessage。
    /// </summary>
    public void ShowMessage(string message, Action onConfirm = null)
    {
        HideMessage();

        currentMessageWindow = Instantiate(messageWindowPrefab, transform);
        confirmAction = onConfirm;

        TextMeshProUGUI txt = currentMessageWindow.transform.Find("MessageText").GetComponent<TextMeshProUGUI>();
        if (txt != null)
            txt.text = message;

        if (onConfirm != null)
            CreateConfirmButton(txt);
    }

    public void SetConfirmInteractable(bool interactable)
    {
        if (confirmButton != null)
            confirmButton.interactable = interactable;
    }

    public void HideMessage()
    {
        confirmButton = null;
        confirmAction = null;
        if (currentMessageWindow != null)
        {
            Destroy(currentMessageWindow);
            currentMessageWindow = null;
        }
    }

    void CreateConfirmButton(TextMeshProUGUI messageText)
    {
        var windowRect = currentMessageWindow.GetComponent<RectTransform>();
        if (windowRect != null)
            windowRect.sizeDelta = new Vector2(windowRect.sizeDelta.x + 140f, windowRect.sizeDelta.y);

        if (messageText != null)
        {
            var textRect = messageText.rectTransform;
            textRect.offsetMax = new Vector2(textRect.offsetMax.x - 140f, textRect.offsetMax.y);
        }

        var buttonObject = new GameObject("ConfirmButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(currentMessageWindow.transform, false);

        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(120f, 44f);
        rect.anchoredPosition = new Vector2(-12f, 0f);

        var image = buttonObject.GetComponent<Image>();
        image.sprite = WhiteSprite();
        image.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        image.raycastTarget = true;

        confirmButton = buttonObject.GetComponent<Button>();
        confirmButton.targetGraphic = image;
        confirmButton.interactable = false;
        confirmButton.onClick.AddListener(OnConfirmClicked);

        var labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var label = labelObject.AddComponent<TextMeshProUGUI>();
        if (messageText != null && messageText.font != null)
            label.font = messageText.font;
        label.text = "決定";
        label.fontSize = 24;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        label.raycastTarget = false;
    }

    void OnConfirmClicked()
    {
        confirmAction?.Invoke();
    }

    static Sprite whiteSprite;

    static Sprite WhiteSprite()
    {
        if (whiteSprite != null)
            return whiteSprite;

        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }
}
