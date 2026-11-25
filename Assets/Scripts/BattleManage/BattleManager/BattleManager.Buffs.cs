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
        public int RemainingTurns;
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
        bool playCostChanged = ExpirePlayCostBuffs();

        // パワーボーナスが変更された場合は、すべてのバトラーの表示を更新
        if (playerChanged || enemyChanged)
            RefreshAllBattlerDisplays();
        if (playCostChanged)
            RefreshVisibleHandCosts();
        if (playerChanged || enemyChanged || playCostChanged)
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
        if (attacker == null || attacker.IsZeroPowerMove(moveIndex)) return 0;

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
        if (attacker != null && attacker.IsZeroPowerMove(moveIndex)) return 0;
        return GetMoveBaseDamage(attacker, moveIndex) + CategoryDamageUpSkill.GetBonus(attacker, target, moveIndex);
    }
    #endregion

    #region リソースバフ
    // 素のコストとエネルギーをターン数へ戻すのは、その側のドローフェイズだけ。
    // スキル差分は、受け手のターンを数えて持続する。付与した受け手のターンを1回目にする。
    // 自分のドローではターン数にしたあと、まだ残っている差分を足し直す。
    // 持続が尽きた受け手のエンドでは、差分だけ戻し、ターン数への上書きはしない。
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
            RemainingTurns = duration,
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
        public SearchCriteria Criteria;
        public int Amount;
        public int StartTurn;
        public int EndTurn;
        public string SourceCardName;
    }

    readonly List<PlayCostBuffState> pPlayCostBuffs = new List<PlayCostBuffState>();
    readonly List<PlayCostBuffState> ePlayCostBuffs = new List<PlayCostBuffState>();

    // カードを出すときに足すコスト。種類が None のバフはすべてのカードに足す。
    // 付与したターンから数えて、持続ターン数のあいだ有効。
    public void SchedulePlayCostBuff(
    bool forPlayer,
    SearchCriteria criteria,
    int amount,
    int durationTurns,
    int startTurn,
    string sourceCardName,
    bool syncOnline)
    {
        if (amount == 0 || durationTurns < 1) return;

        var list = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        list.Add(new PlayCostBuffState
        {
            Criteria = criteria,
            Amount = amount,
            StartTurn = startTurn,
            EndTurn = startTurn + durationTurns - 1,
            SourceCardName = string.IsNullOrEmpty(sourceCardName) ? "支払いコスト" : sourceCardName
        });

        RefreshVisibleHandCosts();
        PowerBonusUI.Instance?.UpdateUI();

        if (syncOnline && IsOnlineMode && OnlineSyncManager.Instance != null)
            OnlineSyncManager.Instance.SyncSchedulePlayCostBuff(
                forPlayer, criteria, amount, durationTurns, startTurn, sourceCardName);
    }

    public int GetPlayCostAdjustment(bool forPlayer, CardController card)
    {
        if (card == null)
            return 0;

        var list = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        int sum = 0;

        for (int i = 0; i < list.Count; i++)
        {
            var state = list[i];

            if (curTurn < state.StartTurn || curTurn > state.EndTurn)
                continue;

            if (state.Criteria != null &&
                !card.MeetsCriteria(state.Criteria, null))
                continue;

            sum += state.Amount;
        }

        return sum;
    }

    public int GetPlayCostAdjustment(bool forPlayer, DeckCard deckCard)
    {
        if (deckCard == null)
            return 0;

        var list = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        int sum = 0;

        for (int i = 0; i < list.Count; i++)
        {
            var state = list[i];

            if (curTurn < state.StartTurn || curTurn > state.EndTurn)
                continue;

            if (state.Criteria != null &&
                !state.Criteria.MatchesDeck(
                    deckCard,
                    null,
                    checkSource: true,
                    selfMatchesSameCard: false,
                    nullSourcePassesExclude: true))
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
    // ここに残っている差分はまだ持続中なので、引き算せず足し直す。
    public void OnResourcesReset(bool forPlayer)
    {
        var list = forPlayer ? pResourceBuffs : eResourceBuffs;
        var resources = forPlayer ? pResources : eResources;

        for (int i = 0; i < list.Count; i++)
        {
            var buff = list[i];
            ApplyResourceAmount(resources, buff.Type, buff.Amount);
        }
    }

    // 手番側のエンドで、その側のリソース差分を1ターン進める。
    // 持続が尽きた分は、今の所持量から効果数だけ戻す。
    public void ExpireOwnerResourceBuffs()
    {
        bool forPlayer = IsMyTurn;
        var list = forPlayer ? pResourceBuffs : eResourceBuffs;
        if (list.Count == 0)
            return;

        var resources = forPlayer ? pResources : eResources;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var buff = list[i];
            buff.RemainingTurns--;
            if (buff.RemainingTurns > 0)
                continue;

            RevertResourceAmount(resources, buff.Type, buff.Amount);
            list.RemoveAt(i);
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
            result.Add(new ActiveBuffView(state.SourceCardName, state.RemainingTurns));
        }

        var playCosts = forPlayer ? pPlayCostBuffs : ePlayCostBuffs;
        foreach (var state in playCosts)
        {
            if (curTurn < state.StartTurn || curTurn > state.EndTurn) continue;
            result.Add(new ActiveBuffView(state.SourceCardName, state.EndTurn - curTurn + 1));
        }

        return result;
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

    static void RevertResourceAmount(ResourceGroup resources, ResourceType type, int amount)
    {
        if (resources == null || amount == 0) return;

        switch (type)
        {
            case ResourceType.エネルギー:
                if (resources.Energy == null) return;
                if (amount > 0)
                    resources.Energy.TakeEnergy(amount);
                else
                    resources.Energy.AddEnegry(-amount);
                break;
            case ResourceType.コスト:
                resources.Cost?.AddCost(-amount);
                break;
        }
    }
    #endregion
}
