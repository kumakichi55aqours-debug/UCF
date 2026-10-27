using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardDetailManager : MonoBehaviour
{
    public static CardDetailManager Instance { get; private set; }

    [SerializeField] private GameObject detailPanel;

    // 基本UI
    [Header("基本UI")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Material srNameMaterial;
    [SerializeField] private TMP_ColorGradient srNameGradient;
    [SerializeField] private Image artworkImage;
    [SerializeField] private TextMeshProUGUI skillDescriptionText;

    // Battler用UI
    [Header("Battler用UI")]
    [SerializeField] private GameObject onlyBattler;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private Image typeImage;
    [SerializeField] private Image strongElement;
    [SerializeField] private TextMeshProUGUI stElementTxt;
    [SerializeField] private Image weakElement;
    [SerializeField] private TextMeshProUGUI wkElementTxt;
    [Header("技1")]
    [SerializeField] private TextMeshProUGUI moveName1Text;
    [SerializeField] private TextMeshProUGUI energy1Text;
    [SerializeField] private TextMeshProUGUI power1Text;
    [SerializeField] private Image elementImage1;
    [SerializeField] private TextMeshProUGUI moveDescription1Text;
    [Header("技2")]
    [SerializeField] private TextMeshProUGUI moveName2Text;
    [SerializeField] private TextMeshProUGUI energy2Text;
    [SerializeField] private TextMeshProUGUI power2Text;
    [SerializeField] private Image elementImage2;
    [SerializeField] private TextMeshProUGUI moveDescription2Text;

    // Tactics用UI
    [Header("Tactics用UI")]
    [SerializeField] private GameObject onlyTactics;
    
    // Field用UI
    [Header("Field用UI")]
    [SerializeField] private GameObject onlyField;
    private Material defaultNameMaterial;
    private TMP_ColorGradient defaultNameGradient;
    private bool defaultVertexGradientEnabled;

    [Header("召喚ボイス")]
    [SerializeField] private Button summonVoiceButton;

    private CardController currentDisplayedCard;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
        HideDetail();

        if (nameText != null)
        {
            defaultNameMaterial = nameText.fontSharedMaterial;
            defaultNameGradient = nameText.colorGradientPreset;
            defaultVertexGradientEnabled = nameText.enableVertexGradient;
        }
        
        // 召喚ボイスボタンのイベント設定
        if (summonVoiceButton != null)
        {
            summonVoiceButton.onClick.AddListener(PlaySummonVoice);
        }
    }

    public void ShowDetail(CardController card)
    {
        currentDisplayedCard = card;
        CardController cc = GetCurrentDisplayedCardController();
        var rarity = CardRarity.N;

        // 表示内容の更新
        if (card.model is BattlerModel battler)
        {
            nameText.text = battler.cardName;
            rarity = battler.rarity;
            artworkImage.sprite = battler.artwork;
            ShowCost(battler.cost, cc);
            skillDescriptionText.text = ConvertSkillDescription(battler.skillDescription);

            onlyBattler.SetActive(true);
            onlyTactics.SetActive(false);
            onlyField.SetActive(false);

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

            stElementTxt.text = "-" + battler.属性軽減値.ToString();
            wkElementTxt.text = "+" + battler.属性増加値.ToString();

            // 技1
            moveName1Text.text = battler.moveName1;
            int energyCost1 = cc.GetMoveEnergyCost(0);
            energy1Text.text = energyCost1.ToString();
            energy1Text.color = GetColorFromEnergyDiff(battler.energy1, energyCost1);
            elementImage1.sprite = CardIconManager.Instance.GetElementSprite(battler.moveElement1);
            moveDescription1Text.text = ConvertSkillDescription(battler.description1);
            // 技2
            moveName2Text.text = battler.moveName2;
            int energyCost2 = cc.GetMoveEnergyCost(1);
            energy2Text.text = energyCost2.ToString();
            energy2Text.color = GetColorFromEnergyDiff(battler.energy2, energyCost2);
            elementImage2.sprite = CardIconManager.Instance.GetElementSprite(battler.moveElement2);
            moveDescription2Text.text = ConvertSkillDescription(battler.description2);

            int displayPower1 = cc.modifiedPower1;
            if (BattleManager.Instance != null)
                displayPower1 += BattleManager.Instance.GetGlobalPowerBonus(cc);
            power1Text.text = displayPower1.ToString();
            power1Text.color = GetColorFromPowerDiff(battler.power1, displayPower1);

            int displayPower2 = cc.modifiedPower2;
            if (BattleManager.Instance != null)
                displayPower2 += BattleManager.Instance.GetGlobalPowerBonus(cc);
            power2Text.text = displayPower2.ToString();
            power2Text.color = GetColorFromPowerDiff(battler.power2, displayPower2);
        }
        else if (card.model is TacticsModel tactics)
        {
            nameText.text = tactics.cardName;
            rarity = tactics.rarity;
            artworkImage.sprite = tactics.artwork;
            ShowCost(tactics.cost, cc);
            skillDescriptionText.text = ConvertSkillDescription(tactics.skillDescription);

            onlyTactics.SetActive(true);
            onlyBattler.SetActive(false);
            onlyField.SetActive(false);
            hpText.transform.parent.gameObject.SetActive(false);
            typeImage.sprite = CardIconManager.Instance.GetCardKindSprite(CardKind.作戦);
        }
        else if (card.model is FieldModel field)
        {
            nameText.text = field.cardName;
            rarity = field.rarity;
            artworkImage.sprite = field.artwork;
            ShowCost(field.cost, cc);
            skillDescriptionText.text = ConvertSkillDescription(field.skillDescription);

            onlyField.SetActive(true);
            onlyBattler.SetActive(false);
            onlyTactics.SetActive(false);
            hpText.text = (cc != null) ? cc.curHp.ToString() : field.maxHp.ToString();
            hpText.transform.parent.gameObject.SetActive(true);
            typeImage.sprite = CardIconManager.Instance.GetCardKindSprite(CardKind.フィールド);
        }

        ApplyRarityVisuals(rarity);

        detailPanel.SetActive(true);
    }

    private void ApplyRarityVisuals(CardRarity rarity)
    {
        if (nameText == null)
            return;

        if (defaultNameMaterial == null)
        {
            defaultNameMaterial = nameText.fontSharedMaterial;
        }

        if (rarity == CardRarity.SR)
        {
            if (srNameMaterial != null)
            {
                nameText.fontSharedMaterial = srNameMaterial;
            }

            if (srNameGradient != null)
            {
                nameText.enableVertexGradient = true;
                nameText.colorGradientPreset = srNameGradient;
            }
        }
        else
        {
            if (defaultNameMaterial != null)
            {
                nameText.fontSharedMaterial = defaultNameMaterial;
            }

            nameText.enableVertexGradient = defaultVertexGradientEnabled;
            nameText.colorGradientPreset = defaultNameGradient;
        }
    }

    private string ConvertSkillDescription(string description)
    {
        // 属性の単語をアイコンに置換
        description = description.Replace("無属性", "<sprite name=\"Icons_Normal\">");
        description = description.Replace("炎属性", "<sprite name=\"Icons_Flame\">");
        description = description.Replace("水属性", "<sprite name=\"Icons_Water\">");
        description = description.Replace("雷属性", "<sprite name=\"Icons_Thunder\">");
        description = description.Replace("風属性", "<sprite name=\"Icons_Wind\">");
        description = description.Replace("地属性", "<sprite name=\"Icons_Ground\">");
        description = description.Replace("氷属性", "<sprite name=\"Icons_Ice\">");
        description = description.Replace("光属性", "<sprite name=\"Icons_Light\">");
        description = description.Replace("闇属性", "<sprite name=\"Icons_Dark\">");
        description = description.Replace("力タイプ", "<sprite name=\"Icons_POW\">");
        description = description.Replace("速タイプ", "<sprite name=\"Icons_SPD\">");
        description = description.Replace("技タイプ", "<sprite name=\"Icons_TEC\">");
        description = description.Replace("PAS", "<sprite name=\"Icons_PAS\">");
        description = description.Replace("ZON", "<sprite name=\"Icons_ZON\">");
        description = description.Replace("ACT", "<sprite name=\"Icons_ACT\">");
        description = description.Replace("TRG", "<sprite name=\"Icons_TRG\">");
        description = description.Replace("カテゴリ", "<sprite name=\"Icons_CAT\">");

        // 色の単語：変更
        description = description.Replace("毒", "<color=#FFC8D6>毒</color>");
        description = description.Replace("火傷", "<color=#FF8C8C>火傷</color>");
        description = description.Replace("凍結", "<color=#82BED6>凍結</color>");
        description = description.Replace("暗闇", "<color=#A082FF>暗闇</color>");
        description = description.Replace("鈍化", "<color=#6EB46E>鈍化</color>");
        description = description.Replace("気絶", "<color=#FFE64B>気絶</color>");
        description = description.Replace("混乱", "<color=#FF8AD4>混乱</color>");
        description = description.Replace("従属", "<color=#C8E68C>従属</color>");
        description = description.Replace("石化", "<color=#4BC0C0>石化</color>");
        description = description.Replace("+1", "<color=#FF6933>1</color>");
        description = description.Replace("+2", "<color=#FF6933>2</color>");
        description = description.Replace("+3", "<color=#FF6933>3</color>");
        description = description.Replace("+4", "<color=#ff1745>4</color>");
        description = description.Replace("+5", "<color=#ff1745>5</color>");
        description = description.Replace("+6", "<color=#ff1745>6</color>");
        description = description.Replace("+7", "<color=#ff1aab>7</color>");
        description = description.Replace("+8", "<color=#ff1aab>8</color>");
        description = description.Replace("+9", "<color=#ff1aab>9</color>");
        description = description.Replace("-1", "<color=#9cff9c>1</color>");
        description = description.Replace("-2", "<color=#9cff9c>2</color>");
        description = description.Replace("-3", "<color=#9cff9c>3</color>");
        description = description.Replace("-4", "<color=#8ad2ff>4</color>");
        description = description.Replace("-5", "<color=#8ad2ff>5</color>");
        description = description.Replace("-6", "<color=#8ad2ff>6</color>");
        description = description.Replace("-7", "<color=#6b9fff>7</color>");
        description = description.Replace("-8", "<color=#6b9fff>8</color>");
        description = description.Replace("-9", "<color=#6b9fff>9</color>");

        return description;
    }

    public CardController GetCurrentDisplayedCardController()
    {
        return currentDisplayedCard;
    }

    private void ShowCost(int printedCost, CardController card)
    {
        if (costText == null)
            return;

        int shown = card != null ? card.GetModifiedCost() : Mathf.Max(0, printedCost);
        costText.text = (printedCost <= 0 && shown <= 0) ? "-" : shown.ToString();
        costText.color = GetColorFromCostDiff(printedCost, shown);
    }

    private Color GetColorFromCostDiff(int printedCost, int shown)
    {
        if (shown > printedCost) return Color.red;
        if (shown < printedCost) return Color.green;
        return Color.white;
    }

    private Color GetColorFromPowerDiff(int basePower, int modified)
    {
        if (modified > basePower) return Color.green;
        if (modified < basePower) return Color.red;
        return Color.white;
    }

    private Color GetColorFromEnergyDiff(int baseEnergy, int modified)
    {
        if (modified > baseEnergy) return Color.red;  // エネルギーコストが増加（悪い）
        if (modified < baseEnergy) return Color.green; // エネルギーコストが減少（良い）
        return Color.white;
    }

    public void HideDetail()
    {
        detailPanel.SetActive(false);
    }

    // 召喚ボイスを再生する
    public void PlaySummonVoice()
    {
        if (currentDisplayedCard == null) return;

        // バトラーカードの場合のみ召喚ボイスを再生
        if (currentDisplayedCard.model is BattlerModel battlerModel && battlerModel.summonVoice != null)
            AudioManager.Instance.PlaySummonVoice(battlerModel.summonVoice);
    }
}
