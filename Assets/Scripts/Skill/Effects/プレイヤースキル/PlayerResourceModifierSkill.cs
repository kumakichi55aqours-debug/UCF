using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// エネルギーかコストの所持量を、ターン数のあいだ増減する。
/// プレイヤーはこのクライアントの自分側、相手は相手側。
/// HPを選んだときは ScheduleResourceBuff 側で何もしない。
/// </summary>
[Serializable]
[SkillDisplayName("リソース")]
public class PlayerResourceModifierSkill : SkillDB
{
    public ResourceType リソースタイプ = ResourceType.エネルギー;
    public int 効果数 = 1;
    public int 持続ターン数 = 1;
    public PlayerType 対象プレイヤー = PlayerType.プレイヤー;

    public override Task ActivateEffect(SkillContext context)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return Task.CompletedTask;

        int duration = Mathf.Max(0, 持続ターン数);
        string sourceName = context != null && context.発動カード != null ? context.発動カード.cardName : "リソース";

        if (対象プレイヤー != PlayerType.相手)
            battle.ScheduleResourceBuff(true, リソースタイプ, 効果数, duration, sourceName, true);
        if (対象プレイヤー != PlayerType.プレイヤー)
            battle.ScheduleResourceBuff(false, リソースタイプ, 効果数, duration, sourceName, true);

        return Task.CompletedTask;
    }
}
