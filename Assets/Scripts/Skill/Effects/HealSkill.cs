using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("回復")]
public class HealSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    public int 効果数 = 1;
    public SkillArg 効果数キー = SkillArg.なし;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        int healAmount = ResolveKeyedAmount(context, 効果数, 効果数キー);

        if (IsPlayerHpTarget(効果対象))
        {
            await HealPlayers(context, healAmount);
            return;
        }

        var toHeal = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context, 対象状態);
        if (!GetSelectTarget().HasEnough(toHeal))
            return;

        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();
        var amounts = new List<string>();
        var newHps = new List<int>();
        var shownAmounts = new List<int>();

        foreach (var card in toHeal)
        {
            if (card == null) continue;
            if (card.cardKind != CardKind.バトラー && card.cardKind != CardKind.フィールド) continue;
            if (!ConditionsMet(対象状態, card)) continue;

            bool addressed = CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);
            int maxHp = GetMaxHp(card);
            int actualHealAmount = Mathf.Max(0, Mathf.Min(healAmount, maxHp - card.curHp));
            if (actualHealAmount > 0)
                await BattleManager.Instance.ShowEffect(card.transform.position, actualHealAmount, Color.green);
            card.curHp = Mathf.Min(card.curHp + healAmount, maxHp);
            card.UpdateDisplay();

            if (!addressed)
                continue;

            newHps.Add(card.curHp);
            shownAmounts.Add(actualHealAmount);
        }

        if (BattleManager.Instance.IsOnlineMode && zoneNames.Count > 0 && OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncAreaHeal(
                kinds.ToArray(),
                ids.ToArray(),
                zoneNames.ToArray(),
                indices.ToArray(),
                newHps.ToArray(),
                shownAmounts.ToArray());
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }

        await Task.Delay(300);

        await Task.CompletedTask;
    }

    public static bool IsPlayerHpTarget(TargetList target)
    {
        return target == TargetList.プレイヤー;
    }

    async Task HealPlayers(SkillContext context, int healAmount)
    {
        var battle = BattleManager.Instance;
        if (battle == null || healAmount <= 0)
            return;

        bool sourceIsPlayer = context?.発動カード == null || context.発動カード.isMyCard;
        bool hitOwnSide = TargetScope.IncludesOwner(対象側);
        bool hitOpponentSide = TargetScope.IncludesOpponent(対象側);

        int healtoMyHp = 0;
        int healToEnemyHp = 0;
        if (hitOwnSide)
        {
            if (sourceIsPlayer) healtoMyHp = healAmount;
            else healToEnemyHp = healAmount;
        }
        if (hitOpponentSide)
        {
            if (sourceIsPlayer) healToEnemyHp += healAmount;
            else healtoMyHp += healAmount;
        }

        if (healtoMyHp > 0)
            await HealPlyaerHp(battle.pResources?.Hp, battle.pFieldZone, healtoMyHp);
        if (healToEnemyHp > 0)
            await HealPlyaerHp(battle.eResources?.Hp, battle.eFieldZone, healToEnemyHp);

        if (battle.IsOnlineMode && OnlineSyncManager.Instance != null && (healtoMyHp > 0 || healToEnemyHp > 0))
        {
            OnlineSyncManager.Instance.SyncDirectHpDamage(healtoMyHp, healToEnemyHp);
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }
    }

    static async Task HealPlyaerHp(ResourceSystem hp, Transform effectAnchor, int amount)
    {
        if (hp == null || amount <= 0)
            return;

        hp.TakeDamage(-amount);
        if (effectAnchor != null)
            await BattleManager.Instance.ShowEffect(effectAnchor.position, amount, Color.yellow);
    }

    static int GetMaxHp(CardController card)
    {
        if (card.model is BattlerModel battler)
            return battler.maxHp;
        if (card.model is FieldModel field)
            return field.maxHp;
        return card.curHp;
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is HealSkill heal)
        {
            heal.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}
