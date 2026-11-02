using System;
using UnityEngine;

/// <summary>
/// 1枚のバトラーの技に、指定した属性があるか。
/// 発動条件では見るカードの役割を使い、カードだけを渡された判定では発動カードのときだけそのカードを見る。
/// 見る技が両方のときは、どちらかの技が指定属性なら成立する。
/// 指定属性が None のときは、バトラーであれば属性を問わず成立する。
/// </summary>
[Serializable]
public class MoveElementCondition : ISkillCondition
{
    [Tooltip("発動条件では、この役割のカードを見る。既定は発動カード。")]
    [SerializeField] private ContextCardRole 見るカード = ContextCardRole.発動カード;
    [Tooltip("この技の属性を見る。両方のときは、どちらかが指定属性なら成立する。")]
    [SerializeField] private MoveTarget 見る技 = MoveTarget.両方;
    [Tooltip("この属性が技にあれば成立する。None のときは属性を問わない。")]
    [SerializeField] private ElementList 対象属性 = ElementList.None;

    public MoveElementCondition()
    {
        見るカード = ContextCardRole.発動カード;
        見る技 = MoveTarget.両方;
        対象属性 = ElementList.None;
    }

    public MoveElementCondition(ElementList element, MoveTarget move = MoveTarget.両方)
    {
        見るカード = ContextCardRole.発動カード;
        見る技 = move;
        対象属性 = element;
    }

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

    bool Matches(CardController card)
    {
        if (card == null || card.cardKind != CardKind.バトラー)
            return false;

        if (対象属性 == ElementList.None)
            return true;

        bool move1 = SkillDB.AppliesToMove(見る技, 0) && card.moveElement1 == 対象属性;
        bool move2 = SkillDB.AppliesToMove(見る技, 1) && card.moveElement2 == 対象属性;
        return move1 || move2;
    }
}
