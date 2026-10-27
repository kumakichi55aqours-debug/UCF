using System;
using UnityEngine;

/// <summary>
/// 攻撃者の属性条件
/// 技を受ける時のスキルで、特定属性からの攻撃かチェック
/// </summary>
[Serializable]
public class AttackerElementCondition : ISkillCondition
{
    [SerializeField] private ElementList 対象属性;

    public AttackerElementCondition()
    {
        対象属性 = ElementList.None;
    }

    public AttackerElementCondition(ElementList element)
    {
        対象属性 = element;
    }

    public bool IsConditionMet(CardController card)
    {
        // CardControllerのみでは判定できない（SkillContextが必要）
        return false;
    }

    public bool IsConditionMet(SkillContext context)
    {
        // Noneの場合は全属性を対象
        if (対象属性 == ElementList.None)
        {
            return true;
        }

        // 攻撃属性がない場合はfalse
        ElementList attackElement = context.AttackerElement;
        
        bool result = attackElement == 対象属性;
        
        Debug.Log($"[AttackerElementCondition] チェック: 攻撃属性={attackElement}, 対象属性={対象属性}, 結果={result}");
        
        return result;
    }
}

