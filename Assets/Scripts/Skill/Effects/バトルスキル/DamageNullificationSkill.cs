using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("ダメージ無効化")]
public class DamageNullificationSkill : SkillDB
{
    [SerializeField] public DamageNullificationType 無効化タイプ = DamageNullificationType.完全無効化;
    [SerializeField] public int 減算値 = 0;

    public override Task ActivateEffect(SkillContext context)
    {
        int? currentDamage = context?.CurrentDamage;
        if (currentDamage.HasValue)
        {
            context.FinalDamage = NullifiedDamage(currentDamage.Value);
            return Task.CompletedTask;
        }

        // 相手の技使用時は、まだダメージが決まっていない。
        // 技キャンセルと同じく攻撃者へ残し、その技のダメージが決まったときに適用する。
        var battle = BattleManager.Instance;
        var attacker = battle != null ? battle.ResolveAttackerFromContext(context) : null;
        if (attacker == null)
            return Task.CompletedTask;

        battle.RegisterMoveDamageNullification(attacker, 無効化タイプ, 減算値);
        return Task.CompletedTask;
    }

    int NullifiedDamage(int damage)
    {
        return 無効化タイプ == DamageNullificationType.固定値減算
            ? Mathf.Max(0, damage - 減算値)
            : 0;
    }
}
