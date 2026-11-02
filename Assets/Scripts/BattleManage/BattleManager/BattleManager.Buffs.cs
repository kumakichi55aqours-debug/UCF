using System.Collections.Generic;
using UnityEngine;

public partial class BattleManager
{
    public class PowerBonusState
    {
        public int Amount;
        public int StartTurn;
        public int EndTurn;
        public string SourceCardName; // スキル発動元のカード名
    }

    private readonly List<PowerBonusState> pPowerBonuses = new List<PowerBonusState>();
    private readonly List<PowerBonusState> ePowerBonuses = new List<PowerBonusState>();
    public IReadOnlyList<PowerBonusState> PlayerPowerBonuses => pPowerBonuses;
    public IReadOnlyList<PowerBonusState> EnemyPowerBonuses => ePowerBonuses;

    public class ResourceBuffState
    {
        public ResourceType Type;
        public int Amount;
        public int RemainingResets;
        public int StartTurn;
        public int EndTurn;
        public string SourceCardName;
        public bool ShowInBuffZone = true;
    }

    private readonly List<ResourceBuffState> pResourceBuffs = new List<ResourceBuffState>();
    private readonly List<ResourceBuffState> eResourceBuffs = new List<ResourceBuffState>();

    public readonly struct ActiveBuffView
    {
        public readonly string SourceCardName;
        public readonly int RemainingTurns;

        public ActiveBuffView(string sourceCardName, int remainingTurns)
        {
            SourceCardName = sourceCardName;
            RemainingTurns = remainingTurns;
        }
    }

    #region 全体パワー関連
    // 全体パワーの登録（オンライン同期含む）
    public void SchedulePowerBonusInternal(bool forPlayer, int amount, int durationTurns, int startTurn, string sourceCardName, bool syncOnline)
    {
        if (amount == 0 || durationTurns <= 0) return;

        int endTurn = startTurn + durationTurns - 1;

        var list = forPlayer ? pPowerBonuses : ePowerBonuses;
        list.Add(new PowerBonusState
        {
            Amount = amount,
            StartTurn = startTurn,
            EndTurn = endTurn,
            SourceCardName = sourceCardName ?? "不明"
        });

        PowerBonusUI.Instance?.UpdateUI();

        if (syncOnline && IsOnlineMode && OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncSchedulePowerBonus(forPlayer, amount, durationTurns, startTurn, sourceCardName);
        }
    }

    // 全体パワーの計算
    public int GetGlobalPowerBonus(CardController attacker)
    {
        if (attacker == null) return 0;

        var parent = attacker.transform.parent;
        if (parent != pBattlerZone && parent != eBattlerZone)
            return 0;

        var list = parent == pBattlerZone ? pPowerBonuses : ePowerBonuses;
        int sum = 0;
        foreach (var state in list)
        {
            if (curTurn >= state.StartTurn && curTurn <= state.EndTurn)
                sum += state.Amount;
        }

        return sum;
    }

    // 全体パワーの削除
    public void RemovePowerBonuses()
    {
        bool playerChanged = IsExpiredPowerBonuses(pPowerBonuses);
        bool enemyChanged = IsExpiredPowerBonuses(ePowerBonuses);
        
        bool energyChanged = ExpireEnergyBuffs();
        bool playCostChanged = ExpirePlayCostBuffs();

        // パワーボーナスが変更された場合は、すべてのバトラーの表示を更新
        if (playerChanged || enemyChanged)
            RefreshAllBattlerDisplays();
        if (playCostChanged)
            RefreshVisibleHandCosts();
        if (playerChanged || enemyChanged || energyChanged || playCostChanged)
            PowerBonusUI.Instance?.UpdateUI();
    }

    // 期限切れの全体パワーの消失判定
    private bool IsExpiredPowerBonuses(List<PowerBonusState> list)
    {
        int turn = curTurn;
        int beforeCount = list.Count;
        list.RemoveAll(state => turn > state.EndTurn);
        return list.Count != beforeCount;
    }
    
    // すべてのバトラーのパワー表示を更新
    public void RefreshAllBattlerDisplays()
    {
        Transform[] zones = { pBattlerZone, eBattlerZone };
        foreach (var zone in zones)
        {
            if (zone == null) continue;
            foreach (var battler in zone.GetComponentsInChildren<CardController>())
            {
                if (battler != null && battler.cardKind == CardKind.バトラー)
                {
                    battler.RecalculateModifiedPowers();
                    battler.UpdateDisplay();
                }
            }
        }
    }

    // 技選択時の基本パワーの取得（技ごと）
    public int GetMoveBaseDamage(CardController attacker, int moveIndex)
    {
        if (attacker == null) return 0;

        int baseDamage = moveIndex == 0 ? attacker.modifiedPower1 : attacker.modifiedPower2;
        baseDamage += GetGlobalPowerBonus(attacker);
        baseDamage += attacker.damageBonus;
        return Mathf.Max(0, baseDamage);
    }

    /// <summary>
    /// 技の基本ダメージに、この相手へのカテゴリアップを足す。
    /// </summary>
    public int GetMoveDamageAgainst(CardController attacker, CardController target, int moveIndex)
    {
        return GetMoveBaseDamage(attacker, moveIndex) + CategoryDamageUpSkill.GetBonus(attacker, target, moveIndex);
    }
    #endregion

    #region リソースバフ
    // コストは毎ターンのリセットで消えるので、持続中はリセットのあとに同じ差分を足し直す。
    // エネルギーは全体パワーと同じく、付与したターンから数えて持続ターン数のあいだだけ有効。
    // 自分のドローでエネルギーがターン数に戻ったあとも、まだ期間内なら同じ差分を足し直す。
    // 期間を過ぎたときは、リセット前の残量から同じ差分を引く。
    // 持続ターン数0は、効果だけそのターン分適用し、バフ表示には出さない。
    public void ScheduleResourceBuff(bool forPlayer, ResourceType type, int amount, int durationTurns, string sourceCardName, bool syncOnline)
    {
        if (amount == 0 || durationTurns < 0) return;
        if (type != ResourceType.エネルギー && type != ResourceType.コスト) return;

        bool showInBuffZone = durationTurns > 0;
        int duration = Mathf.Max(1, durationTurns);

        var list = forPlayer ? pResourceBuffs : eResourceBuffs;
        list.Add(new ResourceBuffState
        {
            Type = type,
            Amount = amount,
            RemainingResets = type == ResourceType.エネルギー ? 0 : duration - 1,
            StartTurn = curTurn,
            EndTurn = curTurn + duration - 1,
            SourceCardName = string.IsNullOrEmpty(sourceCardName) ? "不明" : sourceCardName,
            ShowInBuffZone = showInBuffZone
        });

        ApplyResourceAmount(forPlayer ? pResources : eResources, type, amount);
        PowerBonusUI.Instance?.UpdateUI();

        if (syncOnline && IsOnlineMode && OnlineSyncManager.Instance != null)
            OnlineSyncManager.Instance.SyncScheduleResourceBuff(forPlayer, (int)type, amount, durationTurns, sourceCardName);
    }

    public class PlayCostBuffState
    {
        public CardKind Kind;
        public int Amount;
        public int StartTurn;
        public int EndTurn;
        public string SourceCardName;
    }

    readonly List<PlayCostBuffState> pPlayCostBuffs = new List<PlayCostBuffState>();
    readonly List<PlayCostBuffState> ePlayCostBuffs = new List<PlayCostBuffState>();

    // カードを出すときに足すコスト。種類が None のバフはすべてのカードに足す。
    // 付与したターンから数えて、持続ターン数のあいだ有効。
    public void SchedulePlayCostBuff(bool forPlayer, CardKind kind, int amount, int durationTurns, int startTurn, string sourceCardName, bool syncOnline)
    {
        if (amount == 0 || durationTurns < 1) return;

        var list = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        list.Add(new PlayCostBuffState
        {
            Kind = kind,
            Amount = amount,
            StartTurn = startTurn,
            EndTurn = startTurn + durationTurns - 1,
            SourceCardName = string.IsNullOrEmpty(sourceCardName) ? "支払いコスト" : sourceCardName
        });

        RefreshVisibleHandCosts();
        PowerBonusUI.Instance?.UpdateUI();

        if (syncOnline && IsOnlineMode && OnlineSyncManager.Instance != null)
            OnlineSyncManager.Instance.SyncSchedulePlayCostBuff(forPlayer, (int)kind, amount, durationTurns, startTurn, sourceCardName);
    }

    public int GetPlayCostAdjustment(bool forPlayer, CardKind kind)
    {
        var list = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        int sum = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var state = list[i];
            if (curTurn < state.StartTurn || curTurn > state.EndTurn)
                continue;
            if (state.Kind != CardKind.None && state.Kind != kind)
                continue;
            sum += state.Amount;
        }

        return sum;
    }

    bool ExpirePlayCostBuffs()
    {
        int before = pPlayCostBuffs.Count + ePlayCostBuffs.Count;
        pPlayCostBuffs.RemoveAll(state => curTurn > state.EndTurn);
        ePlayCostBuffs.RemoveAll(state => curTurn > state.EndTurn);
        return pPlayCostBuffs.Count + ePlayCostBuffs.Count != before;
    }

    void RefreshVisibleHandCosts()
    {
        RefreshHandCost(pHandZone);
        RefreshHandCost(eHandZone);
    }

    static void RefreshHandCost(Transform zone)
    {
        if (zone == null) return;

        var cards = zone.GetComponentsInChildren<CardController>();
        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            if (card == null) continue;
            var view = card.GetComponent<BattleCardView>();
            if (view != null && view.IsBack)
                continue;
            card.UpdateDisplay();
        }
    }

    // その側のコスト・エネルギーをターン数へ戻した直後に呼ぶ。
    public void OnResourcesReset(bool forPlayer)
    {
        var list = forPlayer ? pResourceBuffs : eResourceBuffs;
        var resources = forPlayer ? pResources : eResources;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            var buff = list[i];
            if (buff.Type == ResourceType.エネルギー)
            {
                if (curTurn < buff.StartTurn || curTurn > buff.EndTurn)
                {
                    list.RemoveAt(i);
                    continue;
                }

                ApplyResourceAmount(resources, ResourceType.エネルギー, buff.Amount);
                continue;
            }

            if (buff.RemainingResets <= 0)
            {
                list.RemoveAt(i);
                continue;
            }

            ApplyResourceAmount(resources, buff.Type, buff.Amount);
            buff.RemainingResets--;
        }

        PowerBonusUI.Instance?.UpdateUI();
    }

    public List<ActiveBuffView> GetActiveBuffs(bool forPlayer)
    {
        var result = new List<ActiveBuffView>();
        var powers = forPlayer ? pPowerBonuses : ePowerBonuses;
        foreach (var state in powers)
        {
            if (curTurn < state.StartTurn || curTurn > state.EndTurn) continue;
            result.Add(new ActiveBuffView(state.SourceCardName, state.EndTurn - curTurn + 1));
        }

        var resources = forPlayer ? pResourceBuffs : eResourceBuffs;
        foreach (var state in resources)
        {
            if (!state.ShowInBuffZone) continue;

            if (state.Type == ResourceType.エネルギー)
            {
                if (curTurn < state.StartTurn || curTurn > state.EndTurn) continue;
                result.Add(new ActiveBuffView(state.SourceCardName, state.EndTurn - curTurn + 1));
                continue;
            }

            result.Add(new ActiveBuffView(state.SourceCardName, state.RemainingResets + 1));
        }

        var playCosts = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        foreach (var state in playCosts)
        {
            if (curTurn < state.StartTurn || curTurn > state.EndTurn) continue;
            result.Add(new ActiveBuffView(state.SourceCardName, state.EndTurn - curTurn + 1));
        }

        return result;
    }

    // ターン番号が進んで期間を外れたエネルギーを、残量から戻す。
    bool ExpireEnergyBuffs()
    {
        bool changed = RevertExpiredEnergy(pResourceBuffs, pResources);
        changed |= RevertExpiredEnergy(eResourceBuffs, eResources);
        return changed;
    }

    bool RevertExpiredEnergy(List<ResourceBuffState> list, ResourceGroup resources)
    {
        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var buff = list[i];
            if (buff.Type != ResourceType.エネルギー) continue;
            if (curTurn >= buff.StartTurn && curTurn <= buff.EndTurn) continue;

            RevertResourceEnergy(resources, buff.Amount);
            list.RemoveAt(i);
            changed = true;
        }

        return changed;
    }

    static void ApplyResourceAmount(ResourceGroup resources, ResourceType type, int amount)
    {
        if (resources == null || amount == 0) return;

        switch (type)
        {
            case ResourceType.エネルギー:
                resources.Energy?.AddEnegry(amount);
                break;
            case ResourceType.コスト:
                resources.Cost?.AddCost(amount);
                break;
        }
    }

    static void RevertResourceEnergy(ResourceGroup resources, int amount)
    {
        if (resources?.Energy == null || amount == 0) return;
        if (amount > 0)
            resources.Energy.TakeEnergy(amount);
        else
            resources.Energy.AddEnegry(-amount);
    }
    #endregion
}
