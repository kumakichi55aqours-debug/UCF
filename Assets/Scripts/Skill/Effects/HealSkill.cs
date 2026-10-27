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
    public int 効果数 = 1;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        var toHeal = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context);

        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();
        var newHps = new List<int>();
        var shownAmounts = new List<int>();

        foreach (var card in toHeal)
        {
            if (card == null)
                continue;
            if (card.cardKind != CardKind.バトラー && card.cardKind != CardKind.フィールド)
                continue;

            bool addressed = CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);
            int maxHp = GetMaxHp(card);
            int actualHealAmount = Mathf.Max(0, Mathf.Min(効果数, maxHp - card.curHp));
            card.curHp = Mathf.Min(card.curHp + 効果数, maxHp);
            card.UpdateDisplay();

            if (actualHealAmount > 0)
                await BattleManager.Instance.ShowEffect(card.transform.position, actualHealAmount, Color.green);

            Debug.Log($"{card.cardName} を {actualHealAmount} 回復しました。現在HP: {card.curHp}/{maxHp}");

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

        await Task.CompletedTask;
    }

    static int GetMaxHp(CardController card)
    {
        if (card.model is BattlerModel battler)
            return battler.maxHp;
        if (card.model is FieldModel field)
            return field.maxHp;
        return card.curHp;
    }
}
