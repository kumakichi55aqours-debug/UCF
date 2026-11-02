using System;
using System.Threading.Tasks;

/// <summary>
/// 付与スキルが1つでもあるあいだ、指定した技を使えない。
/// カードに付けるときはタイミングをルール効果にする。対象技で技1、技2、両方を選ぶ。
/// </summary>
[Serializable]
[SkillDisplayName("付与なし限定")]
public class NoGrantMoveSkill : SkillDB
{
    public MoveTarget 対象技 = MoveTarget.技1;

    public NoGrantMoveSkill()
    {
        タイミング = ActiveTiming.ルール効果;
    }

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    public static bool Blocks(CardController card, int moveIndex)
    {
        if (card == null || card.passiveEffects == null || card.passiveEffects.Count == 0)
            return false;

        foreach (var skill in PrintedSkills<NoGrantMoveSkill>(card))
        {
            if (AppliesToMove(skill.対象技, moveIndex))
                return true;
        }

        return false;
    }
}
