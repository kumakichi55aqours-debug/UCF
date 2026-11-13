using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("全体パワー")]
public class AllPowerSkill : SkillDB
{
    public int 効果数 = 1;
    public int 持続ターン数 = 1;
    public PlayerType 対象プレイヤー = PlayerType.プレイヤー;

    public override Task ActivateEffect(SkillContext context)
    {
        if (context?.発動カード == null)
        {
            return Task.CompletedTask;
        }

        var battle = BattleManager.Instance;
        bool isPlayer = context.発動カード.isPlayerCard;
        int duration = Mathf.Max(1, 持続ターン数);

        if (対象プレイヤー != PlayerType.プレイヤー)
            battle.SchedulePowerBonusInternal(isPlayer, 効果数, duration, battle.curTurn, context.発動カード.cardName, true);
        if (対象プレイヤー != PlayerType.相手)
            battle.SchedulePowerBonusInternal(!isPlayer, 効果数, duration, battle.curTurn, context.発動カード.cardName, true);
        if (対象プレイヤー != PlayerType.両方)
        {
            battle.SchedulePowerBonusInternal(isPlayer, 効果数, duration, battle.curTurn, context.発動カード.cardName, true);
            battle.SchedulePowerBonusInternal(!isPlayer, 効果数, duration, battle.curTurn, context.発動カード.cardName, true);
        }

        return Task.CompletedTask;
    }
}
