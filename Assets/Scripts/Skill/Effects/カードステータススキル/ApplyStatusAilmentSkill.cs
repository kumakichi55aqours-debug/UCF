using System;
using System.Threading.Tasks;

[Serializable]
[SkillDisplayName("(★付与)状態異常")]
public class ApplyStatusAilmentSkill : SkillDB
{
    public StatusAilmentType 状態異常;

    public override Task ActivateEffect(SkillContext context)
    {
        var target = context?.対象カード;
        if (target == null || 状態異常 == StatusAilmentType.None)
            return Task.CompletedTask;

        string sourceName = context.発動カード != null ? context.発動カード.cardName : null;
        // 持続は付与元の GrantSkill が管理する。状態自体はターン経過では消さない。
        target.ApplyStatus(状態異常, -1, sourceName);
        return Task.CompletedTask;
    }
}
