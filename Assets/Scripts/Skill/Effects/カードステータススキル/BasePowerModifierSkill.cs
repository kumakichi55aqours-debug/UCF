using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 基本パワー修正スキル（半永続的）
/// basePowerを変更するため、ResetBasePower()を呼ばない限り変更が残り続ける
/// </summary>
[Serializable]
[SkillDisplayName("基本パワー")]
public class BasePowerModifierSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;
    public List<ElementList> 対象属性;
    public MoveTarget 適用先;
    public int 効果数 = 1;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        var targetCards = KeepContextCard(await CollectTargetsAsync(効果対象, 対象側, 対象条件, context), context);

        foreach (var card in targetCards)
        {
            if (card.cardKind != CardKind.バトラー)
                continue;

            if (対象条件 != null && !card.MeetsCriteria(対象条件, context))
                continue;

            var battler = card.model as BattlerModel;
            bool apply1 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement1, 0);
            bool apply2 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement2, 1);

            int delta1 = apply1 ? 効果数 : 0;
            int delta2 = apply2 ? 効果数 : 0;
            if (delta1 != 0) card.basePower1 += delta1;
            if (delta2 != 0) card.basePower2 += delta2;

            card.RecalculateModifiedPowers();
            card.UpdateDisplay();

            // 永続効果は各クライアントが場を見て付け直す。通知すると両側で二重に足される。
            if (タイミング != ActiveTiming.永続効果
                && (delta1 != 0 || delta2 != 0)
                && BattleManager.Instance != null
                && BattleManager.Instance.IsOnlineMode
                && CardAddress.TryFrom(card, out CardAddress address))
            {
                OnlineSyncManager.Instance?.SyncBasePower(address.Kind.ToString(), address.Id, address.ZoneName, address.Index, delta1, delta2);
            }
        }
    }
}

