using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleCardView : MonoBehaviour
{
    [Header("基本UI")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image artworkImage;

    [Header("Battler用UI")]
    [SerializeField] private GameObject onlyBattler;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private Image typeImage;
    [SerializeField] private Image strongElement;
    [SerializeField] private Image weakElement;
    [SerializeField] private TextMeshProUGUI move1StatsText;
    [SerializeField] private Image elementImage1;
    [SerializeField] private TextMeshProUGUI move2StatsText;
    [SerializeField] private Image elementImage2;

    [Header("Tactics用UI")]
    [SerializeField] private GameObject onlyTactics;

    [Header("Field用UI")]
    [SerializeField] private GameObject onlyField;

    [SerializeField] private GameObject canUseMovePanel;
    [SerializeField] private GameObject targetPanel;

    [Header("カード裏面")]
    [SerializeField] private GameObject onlyBack;

    public bool IsBack => onlyBack.activeSelf;

    BattleManager bm;
    CardController cc;

    // CardController への参照は通常 GameObject にあるので
    private CardController cardController;

    Transform statusIconRow;
    readonly List<StatusIconView> statusIconPool = new List<StatusIconView>();
    bool energyColorCached;
    Color defaultEnergyColor = Color.white;
    bool costColorCached;
    Color defaultCostColor = Color.white;

    class StatusIconView
    {
        public GameObject Root;
        public Image Image;
    }

    private void Start()
    {
        cc = GetComponent<CardController>();
        EnsureTargetPanel();
        if (targetPanel != null)
        {
            foreach (var graphic in targetPanel.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }
        UpdateCanUseMovePanel();
        SetTargetPanelVisible(false);
    }

    public void Show(object model)
    {
        if (cc == null)
            cc = GetComponent<CardController>();

        // 表示内容の更新
        if (model is BattlerModel battler)
        {
            nameText.text = battler.cardName;
            artworkImage.sprite = battler.artwork;
            artworkImage.rectTransform.anchoredPosition = new Vector2(6, -30);
            ShowCost(battler.cost);
            onlyBattler.SetActive(true);
            hpText.text = (cc != null) ? cc.curHp.ToString() : battler.maxHp.ToString();
            hpText.transform.parent.gameObject.SetActive(true);
            typeImage.sprite = CardIconManager.Instance.GetCardTypeSprite(battler.type);
            if (battler.得意属性 != ElementList.None)
            {
                strongElement.gameObject.SetActive(true);
                strongElement.sprite = CardIconManager.Instance.GetElementSprite(battler.得意属性);
            }
            else strongElement.gameObject.SetActive(false);
            if (battler.弱点属性 != ElementList.None)
            {
                weakElement.gameObject.SetActive(true);
                weakElement.sprite = CardIconManager.Instance.GetElementSprite(battler.弱点属性);
            }
            else weakElement.gameObject.SetActive(false);
            ShowMoveStats(move1StatsText, 0, battler.energy1, battler.power1);
            elementImage1.sprite = CardIconManager.Instance.GetElementSprite(battler.moveElement1);
            ShowMoveStats(move2StatsText, 1, battler.energy2, battler.power2);
            elementImage2.sprite = CardIconManager.Instance.GetElementSprite(battler.moveElement2);

            onlyTactics.SetActive(false);
            onlyField.SetActive(false);
            onlyBack.SetActive(false);
            RefreshStatusIcons();
        }
        else if (model is TacticsModel tactics)
        {
            nameText.text = tactics.cardName;
            artworkImage.sprite = tactics.artwork;
            artworkImage.rectTransform.anchoredPosition = new Vector2(17, -30);
            ShowCost(tactics.cost);
            onlyTactics.SetActive(true);
            onlyBattler.SetActive(false);
            onlyField.SetActive(false);
            onlyBack.SetActive(false);
            hpText.transform.parent.gameObject.SetActive(false);
            typeImage.sprite = CardIconManager.Instance.GetCardKindSprite(CardKind.作戦);
            HideStatusIcons();
        }
        else if (model is FieldModel field)
        {
            nameText.text = field.cardName;
            artworkImage.sprite = field.artwork;
            artworkImage.rectTransform.anchoredPosition = new Vector2(17, -30);
            ShowCost(field.cost);
            onlyField.SetActive(true);
            hpText.text = (cc != null) ? cc.curHp.ToString() : field.maxHp.ToString();
            hpText.transform.parent.gameObject.SetActive(true);
            typeImage.sprite = CardIconManager.Instance.GetCardKindSprite(CardKind.フィールド);
            onlyBattler.SetActive(false);
            onlyTactics.SetActive(false);
            onlyBack.SetActive(false);
            HideStatusIcons();
        }
    }

    void ShowMoveStats(TextMeshProUGUI label, int moveIndex, int printedEnergy, int printedPower)
    {
        if (label == null)
            return;

        int shownEnergy = cc != null ? cc.GetMoveEnergyCost(moveIndex) : printedEnergy;
        int shownPower = printedPower;
        if (cc != null)
        {
            shownPower = moveIndex == 0 ? cc.modifiedPower1 : cc.modifiedPower2;
            if (BattleManager.Instance != null)
                shownPower += BattleManager.Instance.GetGlobalPowerBonus(cc);
        }

        if (!energyColorCached)
        {
            defaultEnergyColor = label.color;
            energyColorCached = true;
        }

        label.color = defaultEnergyColor;
        label.text = $"{FormatStat(shownEnergy, EnergyCostColor(printedEnergy, shownEnergy))}/{FormatStat(shownPower, PowerTextColor(printedPower, shownPower))}";
    }

    string FormatStat(int value, Color color)
    {
        if (color == defaultEnergyColor)
            return value.ToString();
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{value}</color>";
    }

    void ShowCost(int printedCost)
    {
        if (costText == null)
            return;

        int shown = cc != null ? cc.GetModifiedCost() : Mathf.Max(0, printedCost);
        costText.text = (printedCost <= 0 && shown <= 0) ? "-" : shown.ToString();
        costText.color = CostTextColor(printedCost, shown);
    }

    Color CostTextColor(int printedCost, int shownCost)
    {
        if (!costColorCached && costText != null)
        {
            defaultCostColor = costText.color;
            costColorCached = true;
        }

        if (shownCost > printedCost)
            return new Color(1f, 0.35f, 0.35f);
        if (shownCost < printedCost)
            return new Color(0.45f, 0.9f, 0.45f);
        return defaultCostColor;
    }

    Color EnergyCostColor(int baseCost, int shownCost)
    {
        if (shownCost > baseCost)
            return new Color(1f, 0.35f, 0.35f);
        if (shownCost < baseCost)
            return new Color(0.45f, 0.9f, 0.45f);
        return defaultEnergyColor;
    }

    Color PowerTextColor(int basePower, int shownPower)
    {
        if (shownPower > basePower)
            return new Color(0.45f, 0.9f, 0.45f);
        if (shownPower < basePower)
            return new Color(1f, 0.35f, 0.35f);
        return defaultEnergyColor;
    }

    public void ShowCardBack()
    {
        onlyBack.SetActive(true);
        onlyBattler.SetActive(false);
        onlyTactics.SetActive(false);
        onlyField.SetActive(false);
        HideStatusIcons();
    }

    public void RefreshStatusIcons()
    {
        if (cc == null)
            cc = GetComponent<CardController>();

        var ailments = cc != null ? cc.statusAilments : null;
        var sprites = new List<Sprite>();
        if (ailments != null && CardIconManager.Instance != null)
        {
            foreach (var ailment in ailments)
            {
                if (ailment == null) continue;
                var sprite = CardIconManager.Instance.GetStatusAilmentSprite(ailment.Type);
                if (sprite != null)
                    sprites.Add(sprite);
            }
        }

        int count = sprites.Count;
        if (count == 0)
        {
            HideStatusIcons();
            return;
        }

        EnsureStatusIconRow();
        while (statusIconPool.Count < count)
            statusIconPool.Add(CreateStatusIcon());

        float size = 22f;
        float gap = 2f;
        float total = count * size + (count - 1) * gap;
        float start = -total * 0.5f + size * 0.5f;

        for (int i = 0; i < statusIconPool.Count; i++)
        {
            var icon = statusIconPool[i];
            bool visible = i < count;
            icon.Root.SetActive(visible);
            if (!visible) continue;

            icon.Image.sprite = sprites[i];
            icon.Image.color = Color.white;
            var rect = icon.Root.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(start + i * (size + gap), 0f);
        }

        statusIconRow.gameObject.SetActive(true);
        statusIconRow.SetAsLastSibling();
    }

    void HideStatusIcons()
    {
        if (statusIconRow != null)
            statusIconRow.gameObject.SetActive(false);
    }

    void EnsureStatusIconRow()
    {
        if (statusIconRow != null) return;

        var rowObject = new GameObject("StatusIconRow", typeof(RectTransform));
        rowObject.transform.SetParent(transform, false);
        var rect = rowObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 14f);
        rect.sizeDelta = new Vector2(96f, 22f);
        statusIconRow = rowObject.transform;
    }

    StatusIconView CreateStatusIcon()
    {
        var iconObject = new GameObject("StatusIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(statusIconRow, false);
        var rect = iconObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(90f, 90f);

        var image = iconObject.GetComponent<Image>();
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;

        return new StatusIconView
        {
            Root = iconObject,
            Image = image
        };
    }

    /// <summary>
    /// 使用可能な技のパネル表示更新（Battler 用のみ対応）
    /// </summary>
    public void UpdateCanUseMovePanel()
    {
        var battle = BattleManager.Instance;
        bool onPlayerSide = battle != null && transform.parent == battle.pBattlerZone;
        bool onEnemySide = battle != null && transform.parent == battle.eBattlerZone;
        bool currentTurnSide = onPlayerSide ? battle.IsMyTurn : onEnemySide && !battle.IsMyTurn;
        bool show = cc != null
            && cc.cardKind == CardKind.バトラー
            && !cc.hasUsedMove
            && !cc.IsActionBlocked
            && battle != null
            && battle.curTurn != 1
            && currentTurnSide;

        canUseMovePanel.SetActive(show);
    }

    static readonly Color ChosenTargetColor = new Color(1f, 0.72f, 0.1f, 1f);
    bool targetColorCached;
    Color defaultTargetColor = Color.white;

    public void SetTargetPanelVisible(bool isVisible)
    {
        EnsureTargetPanel();
        if (targetPanel == null)
            return;

        if (!isVisible)
            ApplyTargetColor(false);
        targetPanel.SetActive(isVisible);
    }

    public void SetTargetChosen(bool chosen)
    {
        EnsureTargetPanel();
        if (targetPanel == null)
            return;

        ApplyTargetColor(chosen);
        if (chosen)
            targetPanel.SetActive(true);
    }

    void ApplyTargetColor(bool chosen)
    {
        var image = targetPanel.GetComponent<Image>();
        if (image == null)
            image = targetPanel.GetComponentInChildren<Image>(true);
        if (image == null)
            return;

        if (!targetColorCached)
        {
            defaultTargetColor = image.color;
            targetColorCached = true;
        }

        image.color = chosen ? ChosenTargetColor : defaultTargetColor;
    }

    void EnsureTargetPanel()
    {
        if (targetPanel != null)
            return;

        Transform targetTransform = transform.Find("TargetPanel");
        if (targetTransform == null)
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "TargetPanel")
                {
                    targetTransform = child;
                    break;
                }
            }
        }

        if (targetTransform == null)
            return;

        targetPanel = targetTransform.gameObject;
        foreach (var graphic in targetPanel.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }
}
