using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class BattleManager
{
    public const int MaxTacticsZoneCount = 3;

    readonly Dictionary<int, Task> enhancementRetires = new Dictionary<int, Task>();
    readonly Dictionary<int, List<CardController>> enhancementsLeftByHost = new Dictionary<int, List<CardController>>();
    readonly List<CardController> expiredEnhancementSources = new List<CardController>();

    public bool IsBattlerZone(Transform zone)
    {
        return zone != null && (zone == pBattlerZone || zone == eBattlerZone);
    }

    public bool IsFieldZone(Transform zone)
    {
        return zone != null && (zone == pFieldZone || zone == eFieldZone);
    }

    public bool IsTacticsZone(Transform zone)
    {
        return zone != null && (zone == pTacticsZone || zone == eTacticsZone);
    }

    public bool CanCommitHandTactics(CardController card)
    {
        return CanPayHandTactics(card) && card != null && TacticsZoneHasRoom(card.isPlayerCard);
    }

    public bool TacticsZoneHasRoom(bool playerZone)
    {
        Transform zone = playerZone ? pTacticsZone : eTacticsZone;
        return zone != null && zone.childCount < MaxTacticsZoneCount;
    }

    /// <summary>
    /// 強化作戦は、場に付与が残っているあいだ作戦ゾーンに置く。
    /// 付与先が墓地へ離れるなどして付与が無くなった時点で墓地へ送る。
    /// </summary>
    public bool KeepsTacticsOnField(CardController card)
    {
        return card != null
            && card.cardKind == CardKind.作戦
            && card.type == CardTypeList.強化
            && HasLivingGrant(card);
    }

    public bool HasLivingGrant(CardController source)
    {
        if (source == null)
            return false;

        foreach (CardController card in EnumerateBoardCards())
        {
            if (card?.passiveEffects == null)
                continue;
            foreach (ActivePassiveEffect effect in card.passiveEffects)
            {
                if (effect != null && effect.SourceCard == source && effect.RemainingTurns != 0)
                    return true;
            }
        }

        return false;
    }

    public List<CardController> EnhancementSourcesOf(CardController host)
    {
        return CollectEnhancementSources(host);
    }

    List<CardController> CollectEnhancementSources(CardController host)
    {
        var sources = new List<CardController>();
        if (host?.passiveEffects == null)
            return sources;

        foreach (ActivePassiveEffect effect in host.passiveEffects)
        {
            CardController source = effect?.SourceCard;
            if (source == null || source.type != CardTypeList.強化)
                continue;
            if (source.transform == null || !IsTacticsZone(source.transform.parent))
                continue;
            if (!sources.Contains(source))
                sources.Add(source);
        }

        return sources;
    }

    /// <summary>
    /// 付与先が場を離れる前に、出し元の強化を控える。効果を空にしてからでは辿れない。
    /// </summary>
    public void RememberEnhancementsLeftBy(CardController host)
    {
        if (host == null)
            return;

        List<CardController> sources = CollectEnhancementSources(host);
        if (sources.Count == 0)
            return;

        int id = host.GetInstanceID();
        if (!enhancementsLeftByHost.TryGetValue(id, out List<CardController> pending))
        {
            pending = new List<CardController>();
            enhancementsLeftByHost[id] = pending;
        }

        foreach (CardController source in sources)
        {
            if (source != null && !pending.Contains(source))
                pending.Add(source);
        }
    }

    /// <summary>
    /// 付与先が居なくなった強化を墓地へ送る。期限切れのエンドフェイズ処理とは別。
    /// 付与先の撃破や墓地送りが終わったあとで待つ。
    /// </summary>
    public Task RetireEnhancementsIfSpent(CardController host)
    {
        if (host == null)
            return Task.CompletedTask;

        int id = host.GetInstanceID();
        if (!enhancementsLeftByHost.TryGetValue(id, out List<CardController> sources))
            return Task.CompletedTask;

        enhancementsLeftByHost.Remove(id);
        return RetireSpentSources(sources);
    }

    /// <summary>
    /// 呼び出し側が待ち損ねた、付与先の退場分をまとめて墓地へ送る。
    /// </summary>
    public Task RetireEnhancementsIfSpentPending()
    {
        if (enhancementsLeftByHost.Count == 0)
            return Task.CompletedTask;

        var sources = new List<CardController>();
        foreach (List<CardController> batch in enhancementsLeftByHost.Values)
        {
            if (batch != null)
                sources.AddRange(batch);
        }

        enhancementsLeftByHost.Clear();
        return RetireSpentSources(sources);
    }

    async Task RetireSpentSources(List<CardController> sources)
    {
        if (sources == null)
            return;

        foreach (CardController source in sources)
        {
            if (source != null && !HasLivingGrant(source))
                await RetireEnhancement(source);
        }
    }

    public void NoteExpiredEnhancement(CardController source)
    {
        if (source == null || source.type != CardTypeList.強化)
            return;
        if (!expiredEnhancementSources.Contains(source))
            expiredEnhancementSources.Add(source);
    }

    /// <summary>
    /// ターン経過で付与が切れた強化を、そのエンドフェイズで墓地へ送る。
    /// </summary>
    public Task RetireExpiredEnhancements()
    {
        if (expiredEnhancementSources.Count == 0)
            return Task.CompletedTask;

        var pending = new List<CardController>(expiredEnhancementSources);
        expiredEnhancementSources.Clear();
        return RetireSpentSources(pending);
    }

    void ClearGrantsFrom(CardController source)
    {
        if (source == null)
            return;

        bool removed = false;
        foreach (CardController card in EnumerateZoneCards())
        {
            if (card != null && card.RemovePassivesFromSource(source))
                removed = true;
        }

        if (removed)
            PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    /// <summary>
    /// カードがゾーンを離れたとき、付与のつながりを片づける。
    /// 強化作戦が作戦ゾーンを離れたら、どの経路でも付与を外す。通常作戦の付与は付与ターン数まで残す。
    /// </summary>
    public void OnCardLeftZone(CardController card, Transform leftZone)
    {
        if (!isActiveAndEnabled || !gameObject.scene.isLoaded)
            return;

        if (card != null && card.cardKind == CardKind.作戦 && card.type == CardTypeList.強化 && IsTacticsZone(leftZone))
        {
            ClearGrantsFrom(card);
            return;
        }

        OnBattlerLeftBoard(card, leftZone);
    }

    void OnBattlerLeftBoard(CardController battler, Transform leftZone)
    {
        bool leftBoard = battler != null
            && ((battler.cardKind == CardKind.バトラー && IsBattlerZone(leftZone))
                || (battler.cardKind == CardKind.フィールド && IsFieldZone(leftZone)));
        if (!leftBoard)
            return;

        // 親が変わった瞬間は、付与先の墓地送りチェーンの途中なので、ここでは動かさない。
        RememberEnhancementsLeftBy(battler);
        if (battler.passiveEffects != null && battler.passiveEffects.Count > 0)
            battler.ClearPassiveEffectsOnly();
    }

    public Task RetireEnhancement(CardController card)
    {
        if (card == null || card.type != CardTypeList.強化 || card.transform == null || !IsTacticsZone(card.transform.parent))
            return Task.CompletedTask;
        if (IsOnlineMode && !card.isMyCard)
            return Task.CompletedTask;

        int id = card.GetInstanceID();
        if (enhancementRetires.TryGetValue(id, out Task inFlight))
            return inFlight;

        Task retire = RetireEnhancementCore(card, id);
        enhancementRetires[id] = retire;
        return retire;
    }

    async Task RetireEnhancementCore(CardController card, int id)
    {
        try
        {
            await Task.Yield();
            if (card == null || card.type != CardTypeList.強化 || card.transform == null || !IsTacticsZone(card.transform.parent))
                return;

            bool hasAddress = CardAddress.TryFrom(card, out CardAddress address);
            bool ownCard = card.isMyCard;
            CardZoneType fromLocation = CardController.ZoneTypeFromTransform(card.transform.parent);

            await MoveEnhancementToGrave(card);

            // 相手は表示の移動を受けたあと、同じ「墓地に送られた時」の窓へ入る。
            if (ownCard && IsOnlineMode && hasAddress && OnlineSyncManager.Instance != null)
                OnlineSyncManager.Instance.SyncTacticsSkillCompleted(address);

            bool queued = SkillChainManager.Instance != null
                && SkillChainManager.Instance.ScheduleSentToGraveyard(card, fromLocation);
            if (!queued && SkillChainManager.Instance != null)
                await SkillChainManager.Instance.WaitUntilIdle();
        }
        finally
        {
            enhancementRetires.Remove(id);
        }
    }

    /// <summary>
    /// 強化を作戦ゾーンから墓地へ動かす。墓地に送られた時は、移動のあとで出す。
    /// </summary>
    Task MoveEnhancementToGrave(CardController card)
    {
        if (card == null || CardAnimationManager.Instance == null)
            return Task.CompletedTask;

        return AnimateTacticsToGrave(card, suppressSkillTrigger: true);
    }

    async Task AnimateTacticsToGrave(CardController card, bool suppressSkillTrigger)
    {
        Transform tacticsZone = card.isMyCard ? pTacticsZone : eTacticsZone;
        Transform graveZone = card.isMyCard ? pGraveZone : eGraveZone;
        await CardAnimationManager.Instance.AnimateDiscard(
            tacticsZone as RectTransform,
            graveZone as RectTransform,
            card,
            suppressSkillTrigger);

        if (card == null)
            return;

        card.UpdateDisplay();
        PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    IEnumerable<CardController> EnumerateBoardCards()
    {
        Transform[] zones =
        {
            pBattlerZone, pTacticsZone, pFieldZone,
            eBattlerZone, eTacticsZone, eFieldZone
        };

        foreach (Transform zone in zones)
        {
            if (zone == null)
                continue;
            foreach (CardController card in zone.GetComponentsInChildren<CardController>())
            {
                if (card != null)
                    yield return card;
            }
        }
    }

    /// <summary>
    /// 指定した強化作戦が現在付与されているカードをすべて取得する。
    /// </summary>
    public List<CardController> GetEnhancementHosts(CardController source)
    {
        var hosts = new List<CardController>();

        if (source == null)
            return hosts;

        foreach (CardController card in EnumerateBoardCards())
        {
            if (card == null)
                continue;

            if (EnhancementSourcesOf(card).Contains(source))
                hosts.Add(card);
        }

        return hosts;
    }

    IEnumerable<CardController> EnumerateZoneCards()
    {
        Transform[] zones =
        {
            pHandZone, pBattlerZone, pTacticsZone, pFieldZone, pGraveZone, pDeckZone, pDimensionZone,
            eHandZone, eBattlerZone, eTacticsZone, eFieldZone, eGraveZone, eDeckZone, eDimensionZone
        };

        foreach (Transform zone in zones)
        {
            if (zone == null)
                continue;
            foreach (CardController card in zone.GetComponentsInChildren<CardController>())
            {
                if (card != null)
                    yield return card;
            }
        }
    }
}
