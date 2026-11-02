using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 1枚のカードに状態異常が付いているか。
/// 発動条件では見るカードの役割を使い、カードだけを渡された判定では発動カードのときだけそのカードを見る。
/// リストが空、または None を含むときは、どれか1つでも付いていれば成立する。
/// 種類を並べたときは、そのいずれかが付いていれば成立する。
/// </summary>
[Serializable]
public class StatusAilmentCondition : ISkillCondition
{
    [Tooltip("発動条件では、この役割のカードを見る。既定は発動カード。")]
    [SerializeField] private ContextCardRole 見るカード = ContextCardRole.発動カード;
    [SerializeField] private List<StatusAilmentType> 状態異常 = new List<StatusAilmentType>();

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
