using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 付与中、相手が相手側のバトラー1体を対象に技を選ぶとき、このスキルが付いているバトラーしか選べなくなる。
/// </summary>
[Serializable]
[SkillDisplayName("(★付与)攻撃集中")]
public class AttackFocusSkill : SkillDB
{
    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }
}

public static class AttackFocusHelper
{
    public static bool HasAttackFocus(CardController card)
    {
        return SkillDB.GrantedSkills<AttackFocusSkill>(card).Any();
    }

    /// <summary>
    /// 候補に攻撃集中付きがいればそのバトラーだけを返す。いなければ候補をそのまま返す。
    /// </summary>
    public static List<CardController> RestrictToFocused(IEnumerable<CardController> candidates)
    {
        var list = candidates?.Where(card => card != null).ToList() ?? new List<CardController>();
        var focused = list.Where(HasAttackFocus).ToList();
        return focused.Count > 0 ? focused : list;
    }
}
