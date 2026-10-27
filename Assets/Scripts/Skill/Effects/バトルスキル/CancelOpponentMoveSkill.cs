using System;
using System.Threading.Tasks;

[Serializable]
[SkillDisplayName("技キャンセル")]
public class CancelOpponentMoveSkill : SkillDB
{
    public override Task ActivateEffect(SkillContext context)
    {
        var attacker = BattleManager.Instance.ResolveAttackerFromContext(context);
        if (attacker == null)
            return Task.CompletedTask;

        BattleManager.Instance.RegisterOpponentMoveCancellation(attacker);

        return Task.CompletedTask;
    }
}

