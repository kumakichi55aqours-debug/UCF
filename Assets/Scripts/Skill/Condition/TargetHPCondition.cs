using System;
using UnityEngine;

[Serializable]
public class TargetHPCondition : ISkillCondition
{
    [SerializeField] private HPThresholdType 閾値タイプ;
    [SerializeField] private int 基準HP;
    [SerializeField] private float 基準HP比率; // 最大HPに対する比率（0.0-1.0）

    public TargetHPCondition()
    {
        閾値タイプ = HPThresholdType.固定値以下;
        基準HP = 0;
        基準HP比率 = 0.5f;
    }

    public TargetHPCondition(HPThresholdType thresholdType, int fixedHP = 0, float ratio = 0.5f)
    {
        閾値タイプ = thresholdType;
        基準HP = fixedHP;
        基準HP比率 = ratio;
    }

    public bool IsConditionMet(CardController card)
    {
        // この条件は対象のHPを判定するため、SkillContextが必要
        // CardControllerのみでは判定できないため、falseを返す
        return false;
    }

    public bool IsConditionMet(SkillContext context)
    {
        if (context?.対象カード == null) return false;

        var targetCard = context.対象カード;
        if (targetCard.cardKind != CardKind.バトラー) return false;

        var targetBattler = targetCard.model as BattlerModel;
        if (targetBattler == null) return false;

        int currentHP = targetCard.curHp;
        int maxHP = targetBattler.maxHp;

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
    固定値以下,           // 固定のHP値以下
    最大HP比率以下,       // 最大HPの一定比率以下
    固定値以上,           // 固定のHP値以上
    最大HP比率以上        // 最大HPの一定比率以上
}
