using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("コスト影響")]
public class PlayCostModifierSkill : SkillDB
{
    public SearchCriteria 対象条件;
    public int 効果数 = 1;
    public int 持続ターン数 = 1;
    public PlayerType 対象プレイヤー = PlayerType.プレイヤー;

    public override Task ActivateEffect(SkillContext context)
    {
        var battle = BattleManager.Instance;
        if (battle == null || context?.発動カード == null)
            return Task.CompletedTask;

        int duration = Mathf.Max(1, 持続ターン数);
        if (効果数 == 0)
            return Task.CompletedTask;

        bool ownerIsLocal = context.発動カード.isPlayerCard;
        string sourceName = context.発動カード.cardName;

        if (対象プレイヤー != PlayerType.相手)
            battle.SchedulePlayCostBuff(ownerIsLocal, 対象条件, 効果数, duration, battle.curTurn, sourceName, true);

        if (対象プレイヤー != PlayerType.プレイヤー)
            battle.SchedulePlayCostBuff(!ownerIsLocal, 対象条件, 効果数, duration, battle.curTurn, sourceName, true);

        return Task.CompletedTask;
    }
}