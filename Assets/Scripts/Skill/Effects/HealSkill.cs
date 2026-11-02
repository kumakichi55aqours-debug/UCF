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

    [Header("候補を集めるときに1体ずつ見る。対象条件とは別")]
    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    public int 効果数 = 1;
    [Tooltip("なしなら効果数を使う。引数があるときは、コンテキストのその値だけを使う。無ければ 0。")]
    public SkillArg 効果数キー = SkillArg.なし;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        var toHeal = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context, 対象状態);
        int healAmount = ResolveKeyedAmount(context, 効果数, 効果数キー);

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
            if (!ConditionsMet(対象状態, card))
                continue;

            bool addressed = CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);
            int maxHp = GetMaxHp(card);
            int actualHealAmount = Mathf.Max(0, Mathf.Min(healAmount, maxHp - card.curHp));
            card.curHp = Mathf.Min(card.curHp + healAmount, maxHp);
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
