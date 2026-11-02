using System;
using UnityEngine;

/// <summary>
/// 1枚のバトラーの現在HPを見る。
/// 発動条件では見るカードの役割を使い、カードだけを渡された判定では発動カードのときだけそのカードを見る。
/// </summary>
[Serializable]
public class HpCondition : ISkillCondition
{
    [Tooltip("発動条件では、この役割のカードを見る。既定は発動カード。")]
    [SerializeField] private ContextCardRole 見るカード = ContextCardRole.発動カード;
    [SerializeField] private HPThresholdType 閾値タイプ;
    [SerializeField] private int 基準HP;
    [SerializeField] private float 基準HP比率; // 最大HPに対する比率（0.0-1.0）

    public HpCondition()
    {
        見るカード = ContextCardRole.発動カード;
        閾値タイプ = HPThresholdType.固定値以下;
        基準HP = 0;
        基準HP比率 = 0.5f;
    }

    public HpCondition(int threshold)
    {
        見るカード = ContextCardRole.発動カード;
        閾値タイプ = HPThresholdType.固定値以下;
        基準HP = threshold;
        基準HP比率 = 0.5f;
    }

    public HpCondition(HPThresholdType thresholdType, int fixedHP = 0, float ratio = 0.5f)
    {
        見るカード = ContextCardRole.発動カード;
        閾値タイプ = thresholdType;
        基準HP = fixedHP;
        基準HP比率 = ratio;
    }

    public bool IsConditionMet(CardController card)
    {
        if (見るカード != ContextCardRole.発動カード)
            return false;
        return Matches(card);
    }

    public bool IsConditionMet(SkillContext context)
    {
        return Matches(ContextCardRoles.Resolve(見るカード, context));
    }

    bool Matches(CardController card)
    {
        if (card == null || card.cardKind != CardKind.バトラー)
            return false;

        var battler = card.model as BattlerModel;
        if (battler == null)
            return false;

        int currentHP = card.curHp;
        int maxHP = battler.maxHp;

        return 閾値タイプ switch
        {
            HPThresholdType.固定値以下 => currentHP <= 基準HP,
            HPThresholdType.最大HP比率以下 => currentHP <= (int)(maxHP * 基準HP比率),
            HPThresholdType.固定値以上 => currentHP >= 基準HP,
            HPThresholdType.最大HP比率以上 => currentHP >= (int)(maxHP * 基準HP比率),
            _ => false
        };
    }
}

[Serializable]
public enum HPThresholdType
{
    固定値以下,
    最大HP比率以下,
    固定値以上,
    最大HP比率以上
}
