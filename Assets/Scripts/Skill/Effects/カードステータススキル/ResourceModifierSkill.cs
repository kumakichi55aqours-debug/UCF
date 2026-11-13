using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("(〇付与)カードリソース")]
public class ResourceModifierSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    public ResourceType リソースタイプ = ResourceType.エネルギー;
    public ElementList 対象属性;
    public MoveTarget 適用先;
    public int 効果数 = 1;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        var targetCards = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context, 対象状態);

        if (context != null && context.RestrictTargetsToContextCard && context.対象カード != null)
        {
            if ((対象条件 == null || context.対象カード.MeetsCriteria(対象条件, context))
                && ConditionsMet(対象状態, context.対象カード)
                && !targetCards.Contains(context.対象カード))
            {
                targetCards.Add(context.対象カード);
            }

            targetCards = targetCards.Where(c => c == context.対象カード).ToList();
        }

        bool broadcastHp = リソースタイプ == ResourceType.HP
            && タイミング != ActiveTiming.永続効果
            && (context == null || !context.相手も同じ効果を実行する);
        var damageKinds = new List<string>();
        var damageIds = new List<int>();
        var damageZones = new List<string>();
        var damageIndices = new List<int>();
        var damageAmounts = new List<int>();
        var healKinds = new List<string>();
        var healIds = new List<int>();
        var healZones = new List<string>();
        var healIndices = new List<int>();
        var healNewHps = new List<int>();
        var healShown = new List<int>();

        // 各ターゲットカードにリソース修正を適用
        foreach (var card in targetCards)
        {
            if (card == null || !ConditionsMet(対象状態, card))
                continue;

            switch (リソースタイプ)
            {
                case ResourceType.エネルギー:
                {
                    var battler = card.model as BattlerModel;
                    if (battler == null)
                        break;

                    bool apply1 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement1, 0);
                    bool apply2 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement2, 1);

                    if (apply1) card.bonusEnergy1 += 効果数;
                    if (apply2) card.bonusEnergy2 += 効果数;

                    card.RecalculateModifiedPowers();
                    card.UpdateDisplay();

                    break;
                }

                case ResourceType.コスト:
                    card.bonusCost += 効果数;
                    var costView = card.GetComponent<BattleCardView>();
                    if (costView == null || !costView.IsBack)
                        card.UpdateDisplay();
                    break;

                case ResourceType.HP:
                {
                    int before = card.curHp;
                    card.curHp += 効果数;
                    if (card.curHp < 0)
                        card.curHp = 0;
                    card.UpdateDisplay();
                    int delta = card.curHp - before;
                    if (!broadcastHp || delta == 0)
                        break;

                    if (card.transform != null && BattleManager.Instance != null)
                    {
                        Color color = delta > 0 ? Color.green : Color.yellow;
                        await BattleManager.Instance.ShowEffect(card.transform.position, Mathf.Abs(delta), color);
                    }

                    if (delta < 0 && CardAddress.TryAdd(card, damageKinds, damageIds, damageZones, damageIndices))
                        damageAmounts.Add(-delta);
                    else if (delta > 0 && CardAddress.TryAdd(card, healKinds, healIds, healZones, healIndices))
                    {
                        healNewHps.Add(card.curHp);
                        healShown.Add(delta);
                    }
                    break;
                }
            }
        }

        if (!broadcastHp || BattleManager.Instance == null || !BattleManager.Instance.IsOnlineMode || OnlineSyncManager.Instance == null)
            return;

        if (damageZones.Count > 0)
        {
            OnlineSyncManager.Instance.SyncAreaDamage(
                damageKinds.ToArray(),
                damageIds.ToArray(),
                damageZones.ToArray(),
                damageIndices.ToArray(),
                damageAmounts.ToArray());
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }

        if (healZones.Count > 0)
        {
            OnlineSyncManager.Instance.SyncAreaHeal(
                healKinds.ToArray(),
                healIds.ToArray(),
                healZones.ToArray(),
                healIndices.ToArray(),
                healNewHps.ToArray(),
                healShown.ToArray());
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is ResourceModifierSkill resource)
        {
            resource.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}