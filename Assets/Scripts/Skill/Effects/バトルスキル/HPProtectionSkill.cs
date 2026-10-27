using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("HP保護")]
public class HPProtectionSkill : SkillDB
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
            card.hpProtection1 = true;
            card.hpProtectionTargetElement1 = 対象属性;
            Debug.Log($"{card.cardName} の技1にHP保護を適用（対象属性:{対象属性}）");
        }

        if (AppliesToMove(適用先, 1))
        {
            card.hpProtection2 = true;
            card.hpProtectionTargetElement2 = 対象属性;
            Debug.Log($"{card.cardName} の技2にHP保護を適用（対象属性:{対象属性}）");
        }

        return Task.CompletedTask;
    }
}
