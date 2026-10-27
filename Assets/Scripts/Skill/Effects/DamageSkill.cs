using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("ダメージ")]
public class DamageSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;
    public int 効果数 = 1;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (IsPlayerHpTarget(効果対象))
        {
            await DamagePlayers(context);
            return;
        }

        var toDamage = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context);
        if (!GetSelectTarget().HasEnough(toDamage))
            return;

        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();
        var amounts = new List<int>();
        var deadKinds = new List<string>();
        var deadIds = new List<int>();
        var deadZones = new List<string>();
        var deadIndices = new List<int>();

        // 各バトラー・フィールドカードにダメージを与える。住所は墓地へ動かす前に取る
        foreach (var card in toDamage)
        {
            if (card == null) continue;
            if (card.cardKind != CardKind.バトラー && card.cardKind != CardKind.フィールド) continue;

            bool addressed = CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);
            int damageToApply = 効果数;
            card.curHp -= damageToApply;
            card.UpdateDisplay();
            if (damageToApply > 0)
                BattleManager.Instance.RequestStunResolve(card);
            await BattleManager.Instance.ShowEffect(card.transform.position, damageToApply, Color.yellow);

            if (!addressed) continue;
            amounts.Add(damageToApply);
            if (card.curHp <= 0)
                CardAddress.TryAdd(card, deadKinds, deadIds, deadZones, deadIndices);
        }

        if (BattleManager.Instance.IsOnlineMode && zoneNames.Count > 0)
        {
            OnlineSyncManager.Instance.SyncAreaDamage(kinds.ToArray(), ids.ToArray(), zoneNames.ToArray(), indices.ToArray(), amounts.ToArray());
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
            if (deadIndices.Count > 0)
            {
                OnlineSyncManager.Instance.SyncAreaGrave(deadKinds.ToArray(), deadIds.ToArray(), deadZones.ToArray(), deadIndices.ToArray());
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
            }
        }

        await Task.Delay(800);

        foreach (var card in toDamage)
        {
            if (card != null && card.curHp <= 0)
                await BattleManager.Instance.HandleGraveyardTransition(card, false);
        }

        await Task.CompletedTask;
    }

    public static bool IsPlayerHpTarget(TargetList target)
    {
        return target == TargetList.プレイヤー;
    }

    async Task DamagePlayers(SkillContext context)
    {
        var battle = BattleManager.Instance;
        if (battle == null || 効果数 <= 0)
            return;

        bool sourceIsPlayer = context?.発動カード == null || context.発動カード.isMyCard;
        bool hitOwnSide = TargetScope.IncludesOwner(対象側);
        bool hitOpponentSide = TargetScope.IncludesOpponent(対象側);

        int damageToMyHp = 0;
        int damageToEnemyHp = 0;
        if (hitOwnSide)
        {
            if (sourceIsPlayer) damageToMyHp = 効果数;
            else damageToEnemyHp = 効果数;
        }
        if (hitOpponentSide)
        {
            if (sourceIsPlayer) damageToEnemyHp += 効果数;
            else damageToMyHp += 効果数;
        }

        if (damageToMyHp > 0)
            await DamagePlayerHp(battle.pResources?.Hp, battle.pFieldZone, damageToMyHp);
        if (damageToEnemyHp > 0)
            await DamagePlayerHp(battle.eResources?.Hp, battle.eFieldZone, damageToEnemyHp);

        if (battle.IsOnlineMode && OnlineSyncManager.Instance != null && (damageToMyHp > 0 || damageToEnemyHp > 0))
        {
            OnlineSyncManager.Instance.SyncDirectHpDamage(damageToMyHp, damageToEnemyHp);
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }
    }

    static async Task DamagePlayerHp(ResourceSystem hp, Transform effectAnchor, int amount)
    {
        if (hp == null || amount <= 0)
            return;

        hp.TakeDamage(amount);
        if (effectAnchor != null)
            await BattleManager.Instance.ShowEffect(effectAnchor.position, amount, Color.yellow);
        await Task.Delay(800);
    }

}
