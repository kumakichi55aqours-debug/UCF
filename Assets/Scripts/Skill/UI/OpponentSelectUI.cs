using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 選択(セレクト)の表示。タイトルの下に、文章そのものがボタンの選択肢を縦に並べる。
/// シーンに置かなくても、表示時に YES/NO と同じキャンバス上へ作る。
/// </summary>
public class OpponentSelectUI : MonoBehaviour
{
    public static OpponentSelectUI Instance { get; private set; }

    public bool IsShowing { get; private set; }

    const float PanelWidth = 620f;
    const float TitleHeight = 48f;
    const float OptionHeight = 60f;
    const float Spacing = 12f;
    const int PadLeft = 36;
    const int PadRight = 36;
    const int PadTop = 20;
    const int PadBottom = 20;

    RectTransform panel;
    Sprite panelSprite;
    Sprite buttonSprite;
    TMP_FontAsset font;

    Func<int, Task> onSelected;
    bool choosing;
    int sessionId;

    public static OpponentSelectUI Ensure()
    {
        if (Instance != null)
            return Instance;

        Transform parent = null;
        if (YesOrNoSelectionUI.Instance != null)
            parent = YesOrNoSelectionUI.Instance.transform.parent;

        if (parent == null)
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
                parent = canvas.transform;
        }

        var go = new GameObject("OpponentSelectUI", typeof(RectTransform));
        if (parent != null)
            go.transform.SetParent(parent, false);

        return go.AddComponent<OpponentSelectUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CaptureStyle();
        BuildPanel();
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Show(string title, IReadOnlyList<string> options, Func<int, Task> onSelected)
    {
        Show(title, options, null, onSelected);
    }

    public void Show(string title, IReadOnlyList<string> options, IReadOnlyList<bool> selectable, Func<int, Task> onSelected)
    {
        if (panel == null)
            BuildPanel();

        sessionId++;
        this.onSelected = onSelected;
        choosing = false;
        ClearOptions();

        CreateTitle(string.IsNullOrWhiteSpace(title) ? "どちらを選択しますか？" : title);

        int count = options != null ? options.Count : 0;
        for (int i = 0; i < count; i++)
        {
            string label = options[i];
            if (string.IsNullOrWhiteSpace(label))
                label = $"選択肢{i + 1}";
            bool enabled = selectable == null || i >= selectable.Count || selectable[i];
            CreateOptionButton(i, label, enabled);
        }

        FitPanel(count);
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        IsShowing = true;
        FitPanel(count);
    }

    void FitPanel(int optionCount)
    {
        float height = PadTop + PadBottom + TitleHeight;
        if (optionCount > 0)
            height += optionCount * OptionHeight + optionCount * Spacing;

        var element = panel.GetComponent<LayoutElement>();
        if (element != null)
        {
            element.minHeight = height;
            element.preferredHeight = height;
        }

        panel.sizeDelta = new Vector2(PanelWidth, height);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    public Task<int> WaitForChoice(string title, IReadOnlyList<string> options, IReadOnlyList<bool> selectable)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Show(title, options, selectable, index =>
        {
            done.TrySetResult(index);
            return Task.CompletedTask;
        });
        return done.Task;
    }

    void CaptureStyle()
    {
        if (YesOrNoSelectionUI.Instance == null)
            return;

        var sampleText = YesOrNoSelectionUI.Instance.GetComponentInChildren<TextMeshProUGUI>(true);
        if (sampleText != null)
            font = sampleText.font;

        var images = YesOrNoSelectionUI.Instance.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            var image = images[i];
            if (image == null || image.sprite == null)
                continue;

            if (image.gameObject.name == "Panel")
                panelSprite = image.sprite;
            else if (image.gameObject.name == "YesButton")
                buttonSprite = image.sprite;
        }
    }

    void BuildPanel()
    {
        var rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(PanelWidth, TitleHeight + PadTop + PadBottom);
        panel = rect;

        var background = gameObject.GetComponent<Image>();
        if (background == null)
            background = gameObject.AddComponent<Image>();

        if (panelSprite != null)
        {
            background.sprite = panelSprite;
            background.color = Color.white;
        }
        else
        {
            background.sprite = BuiltinSprite();
            background.color = new Color(0.05f, 0.08f, 0.16f, 0.94f);
        }

        background.type = Image.Type.Sliced;

        background.raycastTarget = true;

        var layout = gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = gameObject.AddComponent<VerticalLayoutGroup>();

        layout.padding = new RectOffset(PadLeft, PadRight, PadTop, PadBottom);
        layout.spacing = Spacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = gameObject.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = gameObject.AddComponent<ContentSizeFitter>();

        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var element = gameObject.GetComponent<LayoutElement>();
        if (element == null)
            element = gameObject.AddComponent<LayoutElement>();

        element.minWidth = PanelWidth;
        element.preferredWidth = PanelWidth;
        element.flexibleHeight = 0f;
    }

    void CreateTitle(string title)
    {
        var go = new GameObject("Title", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var element = go.AddComponent<LayoutElement>();
        element.minHeight = TitleHeight;
        element.preferredHeight = TitleHeight;

        var text = go.AddComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.text = title;
        text.fontSize = 30f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
    }

    void CreateOptionButton(int index, string label, bool enabled)
    {
        var go = new GameObject("Option" + (index + 1), typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var element = go.AddComponent<LayoutElement>();
        element.minHeight = OptionHeight;
        element.preferredHeight = OptionHeight;

        var image = go.AddComponent<Image>();
        image.sprite = buttonSprite != null ? buttonSprite : BuiltinSprite();
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        image.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.85f);
        button.colors = colors;
        button.interactable = enabled;
        int captured = index;
        button.onClick.AddListener(() => OnOptionClicked(captured));

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(16f, 8f);
        textRect.offsetMax = new Vector2(-16f, -8f);

        var text = textGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.text = label;
        text.fontSize = 26f;
        text.color = enabled ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.enableAutoSizing = true;
        text.fontSizeMin = 18f;
        text.fontSizeMax = 26f;
        text.raycastTarget = false;
    }

    void ApplyFont(TextMeshProUGUI text)
    {
        if (font != null)
            text.font = font;
        else if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
    }

    async void OnOptionClicked(int index)
    {
        if (!IsShowing || choosing)
            return;

        int ticket = sessionId;
        choosing = true;
        IsShowing = false;
        gameObject.SetActive(false);

        var callback = onSelected;
        onSelected = null;

        try
        {
            if (callback != null)
                await callback(index);
        }
        finally
        {
            if (ticket == sessionId)
            {
                OnlineSyncManager.Instance?.EndOperationBlock();

                if (BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode)
                    OnlineSyncManager.Instance?.SyncEndOpponentDecisionBlock();

                ClearOptions();
                choosing = false;
            }
        }
    }

    void ClearOptions()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    static Sprite BuiltinSprite()
    {
        return Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
    }
}
