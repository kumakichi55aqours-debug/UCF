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

    /// <summary>
    /// スキルの中身を返す。複合スキルは中のスキルまでたどり、それ以外はそのスキル自身を返す。
    /// </summary>
    public static IEnumerable<SkillDB> Parts(SkillDB skill)
    {
        if (skill is CompositeSkill composite)
        {
            if (composite.subSkills == null)
                yield break;
            foreach (var sub in composite.subSkills)
            {
                foreach (var part in Parts(sub))
                    yield return part;
            }
            yield break;
        }

        if (skill != null)
            yield return skill;
    }

    public static bool Contains<T>(SkillDB skill) where T : SkillDB
    {
        foreach (var part in Parts(skill))
        {
            if (part is T)
                return true;
        }
        return false;
    }

    /// <summary>
    /// スキルが付ける状態異常。複合スキルの中も見る。
    /// </summary>
    public static IEnumerable<StatusAilmentType> GrantedStatuses(SkillDB skill)
    {
        foreach (var part in Parts(skill))
        {
            if (part is ApplyStatusAilmentSkill status)
                yield return status.状態異常;
        }
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is CompositeSkill composite)
        {
            composite.subSkills = CloneSkillList(subSkills);
        }
    }
}
