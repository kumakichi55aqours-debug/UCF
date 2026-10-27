using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 全体パワーとリソースバフを、プレイヤー側と相手側のパネルに表示する。
/// 複数あるときは左右の矢印で切り替える。
/// </summary>
public class PowerBonusUI : MonoBehaviour
{
    public static PowerBonusUI Instance { get; private set; }

    [Header("プレイヤーパネル")]
    [SerializeField] private GameObject playerPanel;
    [SerializeField] private TextMeshProUGUI playerTitleText;
    [SerializeField] private TextMeshProUGUI playerTurnText;

    [Header("敵パネル")]
    [SerializeField] private GameObject enemyPanel;
    [SerializeField] private TextMeshProUGUI enemyTitleText;
    [SerializeField] private TextMeshProUGUI enemyTurnText;

    Button playerPrevButton;
    Button playerNextButton;
    Button enemyPrevButton;
    Button enemyNextButton;
    int playerPage;
    int enemyPage;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (playerPanel != null)
            playerPanel.SetActive(false);
        if (enemyPanel != null)
            enemyPanel.SetActive(false);

        playerPrevButton = CreateArrow(playerPanel, "PrevButton", "＜", true);
        playerNextButton = CreateArrow(playerPanel, "NextButton", "＞", false);
        enemyPrevButton = CreateArrow(enemyPanel, "PrevButton", "＜", true);
        enemyNextButton = CreateArrow(enemyPanel, "NextButton", "＞", false);

        if (playerPrevButton != null) playerPrevButton.onClick.AddListener(() => Shift(true, -1));
        if (playerNextButton != null) playerNextButton.onClick.AddListener(() => Shift(true, 1));
        if (enemyPrevButton != null) enemyPrevButton.onClick.AddListener(() => Shift(false, -1));
        if (enemyNextButton != null) enemyNextButton.onClick.AddListener(() => Shift(false, 1));
    }

    public void UpdateUI()
    {
        var battle = BattleManager.Instance;
        if (battle == null) return;

        ApplyPanel(playerPanel, playerTitleText, playerTurnText, playerPrevButton, playerNextButton, battle.GetActiveBuffs(true), ref playerPage);
        ApplyPanel(enemyPanel, enemyTitleText, enemyTurnText, enemyPrevButton, enemyNextButton, battle.GetActiveBuffs(false), ref enemyPage);
    }

    void Shift(bool forPlayer, int delta)
    {
        if (forPlayer) playerPage += delta;
        else enemyPage += delta;
        UpdateUI();
    }

    static void ApplyPanel(
        GameObject panel,
        TextMeshProUGUI titleText,
        TextMeshProUGUI turnText,
        Button prevButton,
        Button nextButton,
        List<BattleManager.ActiveBuffView> buffs,
        ref int page)
    {
        if (panel == null) return;

        int count = buffs != null ? buffs.Count : 0;
        bool hasBuff = count > 0;
        panel.SetActive(hasBuff);
        if (!hasBuff)
        {
            page = 0;
            return;
        }

        page %= count;
        if (page < 0) page += count;

        if (titleText != null)
            titleText.text = FormatTitle(buffs[page]);
        if (turnText != null)
            turnText.text = buffs[page].RemainingTurns.ToString();

        bool showArrows = count > 1;
        if (prevButton != null) prevButton.gameObject.SetActive(showArrows);
        if (nextButton != null) nextButton.gameObject.SetActive(showArrows);
    }

    static string FormatTitle(BattleManager.ActiveBuffView buff)
    {
        return string.IsNullOrEmpty(buff.SourceCardName) ? "不明" : buff.SourceCardName;
    }

    static Button CreateArrow(GameObject panel, string objectName, string label, bool alignLeft)
    {
        if (panel == null) return null;

        var existing = panel.transform.Find(objectName);
        if (existing != null)
            return existing.GetComponent<Button>();

        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(panel.transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = alignLeft ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(28f, 36f);
        rect.anchoredPosition = alignLeft ? new Vector2(16f, 0f) : new Vector2(-16f, 0f);

        var image = go.AddComponent<Image>();
        image.sprite = WhiteSprite();
        image.color = new Color(1f, 1f, 1f, 0.12f);
        image.raycastTarget = true;

        var layout = go.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 22;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;

        var sample = panel.GetComponentInChildren<TextMeshProUGUI>();
        if (sample != null && sample.font != null)
            text.font = sample.font;

        return button;
    }

    static Sprite whiteSprite;

    static Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        whiteSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }
}
