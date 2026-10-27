using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("属性耐性無視")]
public class IgnoreResistanceSkill : SkillDB
{
    public ElementList 対象属性;
    public MoveTarget 適用先 = MoveTarget.両方;

    public override Task ActivateEffect(SkillContext context)
    {
        var card = context?.発動カード;
        if (card == null || card.cardKind != CardKind.バトラー)
            return Task.CompletedTask;

        if (AppliesToMove(適用先, 0))
        {
            card.ignoreResistance1 = true;
            card.ignoreResistanceTargetElement1 = 対象属性;
            Debug.Log($"{card.cardName} の技1に属性耐性無視を適用（対象属性:{対象属性}）");
        }

        if (AppliesToMove(適用先, 1))
        {
            card.ignoreResistance2 = true;
            card.ignoreResistanceTargetElement2 = 対象属性;
            Debug.Log($"{card.cardName} の技2に属性耐性無視を適用（対象属性:{対象属性}）");
        }

        return Task.CompletedTask;
    }
}
