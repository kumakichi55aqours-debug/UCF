using System;
using UnityEngine;

[Serializable]
public class HpCondition : ISkillCondition
{
    [SerializeField] private HPThresholdType 閾値タイプ;
    [SerializeField] private int 基準HP;
    [SerializeField] private float 基準HP比率; // 最大HPに対する比率（0.0-1.0）

    public HpCondition()
    {
        閾値タイプ = HPThresholdType.固定値以下;
        基準HP = 0;
        基準HP比率 = 0.5f;
    }

    public HpCondition(int threshold)
    {
        閾値タイプ = HPThresholdType.固定値以下;
        基準HP = threshold;
        基準HP比率 = 0.5f;
    }

    public HpCondition(HPThresholdType thresholdType, int fixedHP = 0, float ratio = 0.5f)
    {
        閾値タイプ = thresholdType;
        基準HP = fixedHP;
        基準HP比率 = ratio;
    }

    public bool IsConditionMet(CardController card)
    {
        // CardControllerのcurHpを使用して現在HPを取得
        if (card.cardKind != CardKind.バトラー) return false;

        var battler = card.model as BattlerModel;
        if (battler == null) return false;

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
    
    public bool IsConditionMet(SkillContext context)
    {
        // SkillContextの場合はSourceCardをチェック
        return IsConditionMet(context.発動カード);
    }
}
