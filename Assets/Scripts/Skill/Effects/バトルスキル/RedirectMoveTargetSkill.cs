using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 相手側のバトラー1体への技を受けたとき、その攻撃を別の自分バトラーへ移す。
/// 移し先が1体ならそのバトラー、複数ならこのカードの持ち主が選ぶ。
/// </summary>
[System.Serializable]
[SkillDisplayName("身代わり")]
public class RedirectMoveTargetSkill : SkillDB
{
    public override bool CanActivate(SkillContext context)
    {
        if (!base.CanActivate(context))
            return false;
        if (!IsSingleOpponentBattlerMove(context))
            return false;

        var self = context.発動カード ?? context.対象カード;
        return OtherAlliedBattlers(self).Count > 0;
    }

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context == null || !IsSingleOpponentBattlerMove(context))
            return;

        var self = context.発動カード ?? context.対象カード;
        var allies = OtherAlliedBattlers(self);
        if (allies.Count == 0 || TargetSelectionManager.Instance == null)
            return;

        CardController next = await TargetSelectionManager.Instance.SelectOneFromAsync(
            allies,
            "攻撃を移すバトラーを選択してください");
        if (next == null || next == self)
            return;

        context.移した攻撃対象 = next;
        Debug.Log($"[RedirectAttackTargetSkill] {self.cardName} が攻撃対象を {next.cardName} に移す");
    }

    static bool IsSingleOpponentBattlerMove(SkillContext context)
    {
        var attacker = context?.Attacker;
        var battler = attacker?.model as BattlerModel;
        if (battler == null || context.MoveIndex < 0)
            return false;

        int moveIndex = context.MoveIndex;
        if (!MoveTargetEvasion.IsSelectableBattlerTarget(battler.GetMoveTarget(moveIndex)))
            return false;
        if (battler.GetMoveSide(moveIndex) != PlayerType.相手)
            return false;

        // 複数体を選ぶ技は、受けた側ごとに移すと対象が崩れるので移さない
        return MoveTargetEvasion.MoveSelect(attacker, moveIndex).Count <= 1;
    }

    static List<CardController> OtherAlliedBattlers(CardController self)
    {
        var allies = new List<CardController>();
        foreach (var card in MoveTargetEvasion.ListZoneBattlers(self, PlayerType.プレイヤー))
        {
            if (card == self || MoveTargetEvasion.IsEvading(card))
                continue;
            allies.Add(card);
        }

        return allies;
    }
}
