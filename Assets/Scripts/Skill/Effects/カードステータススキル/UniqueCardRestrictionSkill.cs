using System;
using System.Threading.Tasks;

[Serializable]
[SkillDisplayName("同一カード制限")]
public class UniqueCardRestrictionSkill : SkillDB
{
    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }
}
