using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// そのカードに状態異常が付いているか。
/// リストが空、または None を含むときは、どれか1つでも付いていれば成立する。
/// 種類を並べたときは、そのいずれかが付いていれば成立する。
/// </summary>
[Serializable]
public class StatusAilmentCondition : ISkillCondition
{
    [SerializeField] private List<StatusAilmentType> 状態異常 = new List<StatusAilmentType>();

    public bool IsConditionMet(CardController card)
    {
        return Matches(card);
    }

    public bool IsConditionMet(SkillContext context)
    {
        return Matches(context != null ? context.発動カード : null);
    }

    public bool Matches(CardController card)
    {
        if (card == null || !HasAnyStatus(card))
            return false;

        if (状態異常 == null || 状態異常.Count == 0)
            return true;

        for (int i = 0; i < 状態異常.Count; i++)
        {
            StatusAilmentType type = 状態異常[i];
            if (type == StatusAilmentType.None || card.HasStatus(type))
                return true;
        }

        return false;
    }

    static bool HasAnyStatus(CardController card)
    {
        if (card.statusAilments == null)
            return false;

        for (int i = 0; i < card.statusAilments.Count; i++)
        {
            var ailment = card.statusAilments[i];
            if (ailment != null && ailment.Type != StatusAilmentType.None)
                return true;
        }

        return false;
    }
}
