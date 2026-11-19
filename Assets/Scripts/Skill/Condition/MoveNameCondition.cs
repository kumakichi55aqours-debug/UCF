using System;
using UnityEngine;

/// <summary>
/// 1枚のバトラーの技に、指定した名前があるか。
/// 発動条件では見るカードの役割を使い、カードだけを渡された判定では発動カードのときだけそのカードを見る。
/// 見る技が両方のときは、どちらかの技名が一致すれば成立する。
/// </summary>
[Serializable]
public class MoveNameCondition : ISkillCondition
{
    [SerializeField] private ContextCardRole 見るカード = ContextCardRole.発動カード;
    [SerializeField] private MoveTarget 見る技 = MoveTarget.両方;
    [SerializeField] private string 対象技名 = "";

    public MoveNameCondition()
    {
        見るカード = ContextCardRole.発動カード;
        見る技 = MoveTarget.両方;
        対象技名 = "";
    }

    public MoveNameCondition(string moveName, MoveTarget move = MoveTarget.両方)
    {
        見るカード = ContextCardRole.発動カード;
        見る技 = move;
        対象技名 = moveName;
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

        if (string.IsNullOrEmpty(対象技名))
            return false;

        bool move1 = SkillDB.AppliesToMove(見る技, 0) &&
                     card.model is BattlerModel battler1 &&
                     battler1.moveName1 == 対象技名;

        bool move2 = SkillDB.AppliesToMove(見る技, 1) &&
                     card.model is BattlerModel battler2 &&
                     battler2.moveName2 == 対象技名;

        return move1 || move2;
    }
}