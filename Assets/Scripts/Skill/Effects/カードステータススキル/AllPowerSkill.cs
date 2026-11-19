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
        var card = context?.発動カード;
        var battle = BattleManager.Instance;

        if (card == null || battle == null)
            return Task.CompletedTask;

        bool isPlayer = card.isPlayerCard;
        int duration = Mathf.Max(1, 持続ターン数);

        if (対象プレイヤー == PlayerType.プレイヤー ||
            対象プレイヤー == PlayerType.両方)
        {
            battle.SchedulePowerBonusInternal(
                isPlayer, 効果数, duration, battle.curTurn, card.cardName, true);
        }

        if (対象プレイヤー == PlayerType.相手 ||
            対象プレイヤー == PlayerType.両方)
        {
            battle.SchedulePowerBonusInternal(
                !isPlayer, 効果数, duration, battle.curTurn, card.cardName, true);
        }

        return Task.CompletedTask;
    }
}
