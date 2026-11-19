
using System;
using System.Collections.Generic;
using UnityEngine;

public enum ConditionGroupType
{
    AND,
    OR
}

[Serializable]
public class ConditionGroup : ISkillCondition
{
    [SerializeField]
    private ConditionGroupType グループタイプ = ConditionGroupType.AND;

    [SerializeReference]
    public List<ISkillCondition> 条件 = new List<ISkillCondition>();

    public bool IsConditionMet(CardController card)
    {
        return Evaluate(condition =>
            condition.IsConditionMet(card));
    }

    public bool IsConditionMet(SkillContext context)
    {
        return Evaluate(condition =>
            condition.IsConditionMet(context));
    }

    bool Evaluate(Func<ISkillCondition, bool> evaluate)
    {
        if (条件 == null || 条件.Count == 0)
            return グループタイプ == ConditionGroupType.AND;

        foreach (var condition in 条件)
        {
            if (condition == null)
                continue;

            bool met = evaluate(condition);

            if (グループタイプ == ConditionGroupType.AND && !met)
                return false;

            if (グループタイプ == ConditionGroupType.OR && met)
                return true;
        }

        return グループタイプ == ConditionGroupType.AND;
    }
}
