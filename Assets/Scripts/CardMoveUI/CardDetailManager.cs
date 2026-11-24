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

    [Header("トークン")]
    [SerializeField] private Sprite tokenCardFrame;

    public Sprite TokenCardFrame => tokenCardFrame;

    Image battlerFrameImage;
    Sprite battlerFrameSprite;
    bool battlerFrameCached;

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
            nameText.text = battler.カード名;
            rarity = battler.レア度;
            artworkImage.sprite = battler.画像;
            ShowCost(battler.コスト, cc);
            skillDescriptionText.text = ConvertSkillDescription(battler.テキスト);

            onlyBattler.SetActive(true);
            onlyTactics.SetActive(false);
            onlyField.SetActive(false);

            hpText.text = (cc != null) ? cc.curHp.ToString() : battler.maxHp.ToString();
            hpText.transform.parent.gameObject.SetActive(true);
            typeImage.sprite = CardIconManager.Instance.GetCardTypeSprite(battler.タイプ);
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

            ShowPower(power1Text, cc, 0, battler.power1, cc.modifiedPower1);
            ShowPower(power2Text, cc, 1, battler.power2, cc.modifiedPower2);
            ApplyTokenFrame(card != null && card.トークン);
        }
        else if (card.model is TacticsModel tactics)
        {
            nameText.text = tactics.カード名;
            rarity = tactics.レア度;
            artworkImage.sprite = tactics.画像;
            ShowCost(tactics.コスト, cc);
            skillDescriptionText.text = ConvertSkillDescription(tactics.テキスト);

            onlyTactics.SetActive(true);
            onlyBattler.SetActive(false);
            onlyField.SetActive(false);
            hpText.transform.parent.gameObject.SetActive(false);
            typeImage.sprite = CardIconManager.Instance.GetCardTypeSprite(tactics.タイプ);
        }
        else if (card.model is FieldModel field)
        {
            nameText.text = field.カード名;
            rarity = field.レア度;
            artworkImage.sprite = field.画像;
            ShowCost(field.コスト, cc);
            skillDescriptionText.text = ConvertSkillDescription(field.テキスト);

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

    void ApplyTokenFrame(bool token)
    {
        if (battlerFrameImage == null && onlyBattler != null)
        {
            Transform panel = onlyBattler.transform.Find("BattlerPanel");
            if (panel != null)
                battlerFrameImage = panel.GetComponent<Image>();
        }

        if (battlerFrameImage == null)
            return;

        if (!battlerFrameCached)
        {
            battlerFrameSprite = battlerFrameImage.sprite;
            battlerFrameCached = true;
        }

        battlerFrameImage.sprite = token && tokenCardFrame != null
            ? tokenCardFrame
            : battlerFrameSprite;
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
        description = description.Replace("力タイプ", "<sprite name=\"Icons_力\">");
        description = description.Replace("速タイプ", "<sprite name=\"Icons_速\">");
        description = description.Replace("技タイプ", "<sprite name=\"Icons_技\">");
        description = description.Replace("作戦タイプ", "<sprite name=\"Icons_作戦\">");
        description = description.Replace("強化タイプ", "<sprite name=\"Icons_強化\">");
        description = description.Replace("即応タイプ", "<sprite name=\"Icons_即応\">");
        description = description.Replace("Fタイプ", "<sprite name=\"Icons_フィールド\">");
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
        description = description.Replace("+1", "<color=#ffff0a>1</color>");
        description = description.Replace("+2", "<color=#ffff0a>2</color>");
        description = description.Replace("+3", "<color=#ffff0a>3</color>");
        description = description.Replace("+4", "<color=#ff4c4c>4</color>");
        description = description.Replace("+5", "<color=#ff4c4c>5</color>");
        description = description.Replace("+6", "<color=#ff4c4c>6</color>");
        description = description.Replace("+7", "<color=#ff66b2>7</color>");
        description = description.Replace("+8", "<color=#ff66b2>8</color>");
        description = description.Replace("+9", "<color=#ff66b2>9</color>");
        description = description.Replace("-1", "<color=#60ffff>1</color>");
        description = description.Replace("-2", "<color=#60ffff>2</color>");
        description = description.Replace("-3", "<color=#60ffff>3</color>");
        description = description.Replace("-4", "<color=#6bb5ff>4</color>");
        description = description.Replace("-5", "<color=#6bb5ff>5</color>");
        description = description.Replace("-6", "<color=#6bb5ff>6</color>");
        description = description.Replace("-7", "<color=#bc7aff>7</color>");
        description = description.Replace("-8", "<color=#bc7aff>8</color>");
        description = description.Replace("-9", "<color=#bc7aff>9</color>");

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

    void ShowPower(TextMeshProUGUI label, CardController cc, int moveIndex, int printedPower, int modifiedPower)
    {
        if (cc.IsZeroPowerMove(moveIndex))
        {
            label.text = "-";
            label.color = Color.white;
            return;
        }

        int displayPower = modifiedPower;
        if (BattleManager.Instance != null)
            displayPower += BattleManager.Instance.GetGlobalPowerBonus(cc);
        label.text = displayPower.ToString();
        label.color = GetColorFromPowerDiff(printedPower, displayPower);
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
