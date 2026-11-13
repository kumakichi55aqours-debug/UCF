using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// コスト
/// </summary>
[Serializable]
public class CostCondition : ISkillCondition
{
    public bool IsConditionMet(CardController card)
    {
        return Matches(card);
    }

    public bool IsConditionMet(SkillContext context)
    {
        var target = context?.条件対象カード ?? context?.発動カード;
        return Matches(target);
    }

    public bool Matches(CardController card)
    {
        var battle = BattleManager.Instance;
        if (card == null || battle == null) return false;

        int curCardCost = card.GetModifiedCost();
        int checkCost = card.isMyCard
            ? battle.pResources.Cost.currentCost
            : battle.eResources.Cost.currentCost;

        return curCardCost <= checkCost;
    }

    // デッキカード用
    public bool Matches(DeckCard card, bool isMyCard)
    {
        var battle = BattleManager.Instance;
        if (card == null || battle == null)
            return false;

        int checkCost = isMyCard
            ? battle.pResources.Cost.currentCost
            : battle.eResources.Cost.currentCost;

        return card.コスト <= checkCost;
    }
}
