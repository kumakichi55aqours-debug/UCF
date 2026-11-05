using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public enum MoveRestrictionConditionMode
{
    満たさないと使えない,
    満たすと使えない,
}

/// <summary>
/// 指定した技を使えなくする。
/// 制限ターン数が0以下なら永続で、カードに書いてあるあいだ条件を毎回見て判定する。発動はいらないのでタイミングはルール効果にする。
/// 1以上なら、発動したときに発動カードへそのターン数だけ制限を付ける。このときの条件は発動条件として働く。
/// </summary>
[Serializable]
[SkillDisplayName("技の使用制限")]
public class MoveRestrictionSkill : SkillDB
{
    public MoveTarget 対象技 = MoveTarget.技1;
    [Tooltip("空ならすべての属性の技に当てる。")]
    public List<ElementList> 対象属性 = new List<ElementList>();
    [Tooltip("0以下なら永続。1以上なら発動時にこのターン数だけ制限を付ける。")]
    public int 制限ターン数 = 0;
    [Tooltip("永続のときだけ使う。条件が空なら、満たさないと使えないでは常に使え、満たすと使えないでは常に使えない。")]
    public MoveRestrictionConditionMode 条件の扱い = MoveRestrictionConditionMode.満たさないと使えない;

    public MoveRestrictionSkill()
    {
        タイミング = ActiveTiming.ルール効果;
    }

    bool IsPermanent => 制限ターン数 <= 0;

    public override Task ActivateEffect(SkillContext context)
    {
        if (!IsPermanent && context?.発動カード != null)
            context.発動カード.AddTurnRestriction(new TurnRestrictionEffect(制限ターン数, 対象技, 対象属性));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 付いているターン制限か、カードに書いてある永続の制限で、この技が使えない。
    /// </summary>
    public static bool Blocks(CardController card, int moveIndex)
    {
        if (card == null)
            return false;

        ElementList moveElement = moveIndex == 0 ? card.moveElement1 : card.moveElement2;
        if (card.IsMoveRestricted(moveIndex, moveElement))
            return true;

        foreach (var skill in PrintedSkills<MoveRestrictionSkill>(card))
        {
            if (skill.IsPermanent
                && AppliesToMoveElement(skill.対象技, skill.対象属性, moveElement, moveIndex)
                && skill.ConditionsMet(card) == (skill.条件の扱い == MoveRestrictionConditionMode.満たすと使えない))
                return true;
        }

        return false;
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is MoveRestrictionSkill restriction)
        {
            restriction.対象属性 = 対象属性 != null
                ? new List<ElementList>(対象属性)
                : new List<ElementList>();
        }
    }
}

/// <summary>
/// 発動で付いた、残りターンのある技の制限。
/// </summary>
[Serializable]
public class TurnRestrictionEffect
{
    public int RemainingTurns { get; private set; }
    public MoveTarget RestrictedMove { get; private set; }
    readonly List<ElementList> restrictedElements;

    public TurnRestrictionEffect(int turns, MoveTarget move, IReadOnlyList<ElementList> elements)
    {
        RemainingTurns = turns;
        RestrictedMove = move;
        restrictedElements = elements != null ? new List<ElementList>(elements) : new List<ElementList>();
    }

    public void Tick()
    {
        if (RemainingTurns > 0)
            RemainingTurns--;
    }

    public bool IsExpired() => RemainingTurns <= 0;

    public bool Restricts(int moveIndex, ElementList moveElement)
    {
        if (IsExpired() || !SkillDB.AppliesToMove(RestrictedMove, moveIndex))
            return false;
        return restrictedElements.Count == 0 || restrictedElements.Contains(moveElement);
    }
}
