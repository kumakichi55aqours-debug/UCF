using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("複合")]
public class CompositeSkill : SkillDB
{
    [SerializeReference]
    public List<SkillDB> subSkills = new List<SkillDB>();

    public override Task ActivateEffect(SkillContext context)
    {
        return ActivateSubSkills(subSkills, context);
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is CompositeSkill composite)
        {
            composite.subSkills = CloneSkillList(subSkills);
        }
    }
}
