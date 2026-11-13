using System;
using System.Collections.Generic;

/// <summary>
/// 今のフェイズが指定と一致するときだけ発動できる。
/// 「自分のバトルフェイズ」は、このカードの持ち主のターンのそのフェイズ。
/// </summary>
[Serializable]
public class PhaseCondition : ISkillCondition
{
    public List<TurnPhase> フェイズ = new List<TurnPhase>
    {
        TurnPhase.バトルフェイズ
    };

    public bool IsConditionMet(CardController card)
    {
        if (PhaseManager.Instance == null || !フェイズ.Contains(PhaseManager.Instance.CurrentPhase))
            return false;
        if (card == null || BattleManager.Instance == null)
            return false;

        if (card.isMyCard && BattleManager.Instance.IsOnlineMode)
            return OnlineSyncManager.Instance != null;
        if (card.isMyCard)
            return BattleManager.Instance.IsActiveSideTurn;
        return !BattleManager.Instance.IsActiveSideTurn;
    }

    public bool IsConditionMet(SkillContext context)
    {
        return IsConditionMet(context != null ? context.発動カード : null);
    }

    public static bool CardRequires(CardController card, TurnPhase phase)
    {
        if (card?.skills == null)
            return false;

        for (int i = 0; i < card.skills.Count; i++)
        {
            if (SkillRequires(card.skills[i], phase))
                return true;
        }

        return false;
    }

    static bool SkillRequires(SkillDB skill, TurnPhase phase)
    {
        if (skill?.条件 != null)
        {
            for (int i = 0; i < skill.条件.Count; i++)
            {
                if (skill.条件[i] is PhaseCondition condition && condition.フェイズ.Contains(phase))
                    return true;
            }
        }

        if (skill is CompositeSkill composite && composite.subSkills != null)
        {
            for (int i = 0; i < composite.subSkills.Count; i++)
            {
                if (SkillRequires(composite.subSkills[i], phase))
                    return true;
            }
        }

        return false;
    }
}
