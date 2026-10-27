using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 指定した種類のカードを出すときに払うコストを、ターン数のあいだ増減する。
/// プレイヤーは発動したカードの持ち主、エネミーはその相手。
/// 所持コストの残量は変えない。
/// </summary>
[Serializable]
[SkillDisplayName("コスト影響")]
public class PlayCostModifierSkill : SkillDB
{
    [Tooltip("None はすべての種類")]
    public CardKind 対象種類 = CardKind.作戦;
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
            battle.SchedulePlayCostBuff(ownerIsLocal, 対象種類, 効果数, duration, battle.curTurn, sourceName, true);
        if (対象プレイヤー != PlayerType.プレイヤー)
            battle.SchedulePlayCostBuff(!ownerIsLocal, 対象種類, 効果数, duration, battle.curTurn, sourceName, true);

        return Task.CompletedTask;
    }
}
