using System;
using UnityEngine;

[Serializable]
public class DamageCondition : ISkillCondition
{
    [SerializeField] private DamageThresholdType 閾値タイプ;
    [SerializeField] private int 基準ダメージ;

    public DamageCondition()
    {
        閾値タイプ = DamageThresholdType.固定値以上;
        基準ダメージ = 0;
    }

    public DamageCondition(int threshold)
    {
        閾値タイプ = DamageThresholdType.固定値以上;
        基準ダメージ = threshold;
    }

    public DamageCondition(DamageThresholdType thresholdType, int threshold = 0)
    {
        閾値タイプ = thresholdType;
        基準ダメージ = threshold;
    }

    public bool IsConditionMet(CardController card)
    {
        // CardControllerのみでは判定できない（SkillContextが必要）
        return false;
    }

    public bool IsConditionMet(SkillContext context)
    {
        // ダメージ情報がない場合はfalse
        if (context?.CalculatedDamage == null)
        {
            return false;
        }

        int damage = context.CalculatedDamage.Value;
        var owner = context.発動カード;
        int currentHp = owner != null ? owner.curHp : 0;
        bool result = 閾値タイプ switch
        {
            DamageThresholdType.固定値以上 => damage >= 基準ダメージ,
            DamageThresholdType.固定値以下 => damage <= 基準ダメージ,
            DamageThresholdType.現在HP以上 => owner != null && damage >= currentHp,
            _ => false
        };

        Debug.Log($"[DamageCondition] チェック: ダメージ={damage}, 閾値タイプ={閾値タイプ}, 基準ダメージ={基準ダメージ}, 現在HP={currentHp}, 結果={result}");

        return result;
    }
}

[Serializable]
public enum DamageThresholdType
{
    固定値以上,           // 固定のダメージ値以上
    固定値以下,           // 固定のダメージ値以下
    現在HP以上            // 発動カードの現在HP以上
}

