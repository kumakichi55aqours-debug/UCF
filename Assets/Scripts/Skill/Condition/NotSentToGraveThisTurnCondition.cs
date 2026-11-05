using System;

/// <summary>
/// このカードが墓地へ送られたターンには発動できない。
/// 墓地以外にいるときは、いつでも満たす。
/// </summary>
[Serializable]
public class NotSentToGraveThisTurnCondition : ISkillCondition
{
    public bool IsConditionMet(CardController card)
    {
        var battle = BattleManager.Instance;
        if (card == null || battle == null)
            return false;

        var parent = card.transform.parent;
        if (parent != battle.pGraveZone && parent != battle.eGraveZone)
            return true;

        return card.墓地に送られたターン != battle.curTurn;
    }

    public bool IsConditionMet(SkillContext context)
    {
        return IsConditionMet(context != null ? context.発動カード : null);
    }
}
