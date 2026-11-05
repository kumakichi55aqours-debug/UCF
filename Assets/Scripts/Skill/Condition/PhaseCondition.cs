using System;

/// <summary>
/// 今のフェイズが指定と一致するときだけ発動できる。
/// 「自分のバトルフェイズ」は、このカードの持ち主のターンのそのフェイズ。
/// </summary>
[Serializable]
public class PhaseCondition : ISkillCondition
{
    public TurnPhase フェイズ = TurnPhase.バトルフェイズ;

    public bool IsConditionMet(CardController card)
    {
        if (PhaseManager.Instance == null || PhaseManager.Instance.CurrentPhase != フェイズ)
            return false;
        if (card == null || BattleManager.Instance == null)
            return false;

        // オフラインの相手ターンでも、この端末がフェイズを進めている。持ち主のターンかで見る。
        if (card.isMyCard && BattleManager.Instance.IsOnlineMode)
            return OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.CanPlayerOperateCards();
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
                if (skill.条件[i] is PhaseCondition condition && condition.フェイズ == phase)
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
