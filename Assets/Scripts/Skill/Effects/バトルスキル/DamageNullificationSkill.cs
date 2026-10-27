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
        if (!currentDamage.HasValue)
            return Task.CompletedTask;

        context.FinalDamage = 無効化タイプ == DamageNullificationType.固定値減算
            ? Mathf.Max(0, currentDamage.Value - 減算値)
            : 0;
        return Task.CompletedTask;
    }
}
