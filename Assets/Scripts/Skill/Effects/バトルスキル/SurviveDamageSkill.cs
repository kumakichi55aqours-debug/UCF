using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 一度だけ技のダメージで倒れそうになってもHP1で踏みとどまるスキル
/// （発動条件はISkillConditionで管理する）
/// </summary>
[System.Serializable]
[SkillDisplayName("根性")]
public class SurviveDamageSkill : SkillDB
{
    public override Task ActivateEffect(SkillContext context)
    {
        if (context == null)
            return Task.CompletedTask;

        var defender = context.発動カード ?? context.対象カード;
        if (defender == null)
            return Task.CompletedTask;

        if (context.CalculatedDamage == null)
            return Task.CompletedTask;

        int currentHP = defender.curHp;
        if (currentHP <= 0)
            return Task.CompletedTask;

        int incomingDamage = context.CurrentDamage.Value;
        if (incomingDamage < currentHP)
            return Task.CompletedTask;

        int surviveDamage = Mathf.Min(incomingDamage, Mathf.Max(0, currentHP - 1));
        context.FinalDamage = surviveDamage;
        return Task.CompletedTask;
    }
}
