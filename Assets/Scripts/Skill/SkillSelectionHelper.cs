
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// スキル共通のカード候補収集・選択処理。
/// カードの移動や処理カード履歴の登録は各スキル側で行う。
/// </summary>
public static class SkillSelectionHelper
{
    public static async Task<List<SelectionItem>> CollectAndSelectAsync(
        SkillContext context,
        SearchCriteria criteria,
        List<CardZoneType> zones,
        int count,
        PlayerType selector,
        CountMode countMode,
        bool random,
        bool all,
        Func<CardController, bool> conditionCheck = null,
        SelectionComparisonOptions comparison = null)
    {
        var result = new List<SelectionItem>();

        if (zones == null || zones.Count == 0)
            return result;

        if (!all && count <= 0)
            return result;

        var existingCandidates = new List<CardController>();
        var deckCandidates = new List<DeckCard>();

        bool ownerIsLocal = context?.発動カード == null
            || context.発動カード.isMyCard;

        // 1. 指定されたゾーンから候補を収集
        foreach (var zone in zones)
        {
            if (CardZones.IsDeck(zone))
            {
                var deck = CardZones.Deck(zone, ownerIsLocal);

                if (deck != null)
                {
                    deckCandidates.AddRange(
                        deck.Where(d =>
                            d != null &&
                            SearchCriteria.AllowsDeck(criteria, d, context)));
                }

                continue;
            }

            var zoneTransform = CardZones.Of(zone, ownerIsLocal);

            if (zoneTransform == null)
                continue;

            existingCandidates.AddRange(
                zoneTransform.GetComponentsInChildren<CardController>()
                    .Where(card =>
                        card != null &&
                        (context != null
                            ? card.MeetsCriteria(criteria, context)
                            : card.MeetsCriteria(criteria)) &&
                        (conditionCheck == null || conditionCheck(card)))
            );
        }

        // 2. 発動カードとの比較で候補を絞る
        if (comparison != null && comparison.HasSourceComparison)
        {
            var sourcePool = SelectionItem.FromCandidates(
                existingCandidates,
                deckCandidates);

            sourcePool.RemoveAll(item =>
                !CanAddByComparison(
                    item,
                    null,
                    comparison,
                    context?.発動カード));

            existingCandidates = sourcePool
                .Where(item => item.source == SelectionItem.SourceType.Existing)
                .Select(item => item.existingCard)
                .Where(card => card != null)
                .ToList();

            deckCandidates = sourcePool
                .Where(item => item.source == SelectionItem.SourceType.Deck)
                .Select(item => item.deckData)
                .Where(card => card != null)
                .ToList();
        }

        var pool = SelectionItem.FromCandidates(
            existingCandidates,
            deckCandidates);

        if (pool.Count == 0)
            return result;

        bool upTo = countMode == CountMode.まで;

        // 3. 全選択
        if (all)
        {
            if (comparison == null || !comparison.HasPairComparison)
                return pool;

            return SelectValidCombination(
                pool,
                pool.Count,
                allowFewer: true,
                comparison,
                context?.発動カード);
        }

        // 4. ランダム選択
        if (random)
        {
            return SelectValidCombination(
                pool,
                count,
                allowFewer: upTo,
                comparison,
                context?.発動カード,
                randomize: true);
        }

        bool chooseCount = upTo && pool.Count > 1;

        if (pool.Count <= count && !chooseCount)
        {
            if (comparison == null || !comparison.HasPairComparison)
                return pool;

            return SelectValidCombination(
                pool,
                count,
                allowFewer: true,
                comparison,
                context?.発動カード);
        }

        // 5. 選択するプレイヤーを判定
        if (ChooserIsLocal(selector, ownerIsLocal))
        {
            var selected = await CardSelectionUI.Instance.ShowAsync(
                existingCandidates,
                deckCandidates,
                count,
                rejectSameKind: false,
                letCpuChoose: false,
                rejectSameType: false,
                rejectWhen: comparison != null && comparison.HasPairComparison
                    ? (selectedItems, candidate) =>
                        !CanAddByComparison(
                            candidate,
                            selectedItems,
                            comparison,
                            context?.発動カード)
                    : null,
                allowFewer: upTo);

            return selected ?? result;
        }

        // 6. 相手側に選択を依頼
        if (comparison != null && comparison.HasPairComparison)
        {
            // 一括選択RPCでは選択途中の制限ができないため、
            // 有効な組み合わせからランダムに選択する。
            return SelectValidCombination(
                pool,
                count,
                allowFewer: upTo,
                comparison,
                context?.発動カード,
                randomize: true);
        }

        return await ChooseRemote(
            pool,
            count,
            context,
            ownerIsLocal,
            upTo);
    }

    static bool ChooserIsLocal(
        PlayerType selector,
        bool ownerIsLocal)
    {
        bool chooserIsOwner = selector != PlayerType.相手;
        return chooserIsOwner ? ownerIsLocal : !ownerIsLocal;
    }

    /// <summary>
    /// 比較条件を満たすカードの組み合わせを作る。
    /// </summary>
    static List<SelectionItem> SelectValidCombination(
        List<SelectionItem> pool,
        int count,
        bool allowFewer,
        SelectionComparisonOptions comparison,
        CardController source,
        bool randomize = false)
    {
        var selected = new List<SelectionItem>();

        if (pool == null || pool.Count == 0)
            return selected;

        var candidates = randomize
            ? pool.OrderBy(_ => UnityEngine.Random.value).ToList()
            : pool.ToList();

        foreach (var candidate in candidates)
        {
            if (!CanAddByComparison(
                candidate,
                selected,
                comparison,
                source))
            {
                continue;
            }

            selected.Add(candidate);

            if (selected.Count >= count)
                break;
        }

        if (!allowFewer && selected.Count < count)
            return new List<SelectionItem>();

        return selected;
    }

    static async Task<List<SelectionItem>> ChooseRemote(
        List<SelectionItem> pool,
        int count,
        SkillContext context,
        bool ownerIsLocal,
        bool upTo)
    {
        var result = new List<SelectionItem>();

        if (pool == null || pool.Count == 0)
            return result;

        int take = Mathf.Min(count, pool.Count);

        bool online = BattleManager.Instance != null
            && BattleManager.Instance.IsOnlineMode
            && ownerIsLocal
            && context?.発動カード != null
            && OnlineSyncManager.Instance != null;

        // オンライン相手選択でない場合はランダム選択
        if (!online)
        {
            return pool.OrderBy(_ => UnityEngine.Random.value)
                .Take(take)
                .ToList();
        }

        var kinds = new int[pool.Count];
        var ids = new int[pool.Count];

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null &&
                pool[i].TryGetIdentity(out CardKind kind, out int id))
            {
                kinds[i] = (int)kind;
                ids[i] = id;
            }
        }

        int[] picked =
            await OnlineSyncManager.Instance.RequestOpponentCardPick(
                context.発動カード,
                kinds,
                ids,
                take,
                upTo);

        return TakeByIndices(pool, picked, take);
    }

    static List<T> TakeByIndices<T>(
        List<T> pool,
        int[] indices,
        int count)
    {
        var picked = new List<T>();

        if (pool == null || indices == null)
            return picked;

        var used = new HashSet<int>();

        for (int i = 0; i < indices.Length && picked.Count < count; i++)
        {
            int index = indices[i];

            if (index < 0 || index >= pool.Count || !used.Add(index))
                continue;

            picked.Add(pool[index]);
        }

        return picked;
    }

    static bool CanAddByComparison(
        SelectionItem candidate,
        IReadOnlyList<SelectionItem> selected,
        SelectionComparisonOptions options,
        CardController source)
    {
        if (candidate == null || options == null)
            return true;

        // 発動カードとの比較
        if (options.HasSourceComparison && source != null)
        {
            if (options.発動カードと異なるタイプ)
            {
                if (!candidate.TryGetBattlerType(out var candidateType))
                    return false;

                CardTypeList sourceType = source.type;

                if (sourceType == CardTypeList.None ||
                    candidateType == sourceType)
                {
                    return false;
                }
            }

            if (options.発動カードと異なるコスト)
            {
                if (GetSelectionCost(candidate) == source.cost)
                    return false;
            }
        }

        // 選択済みカードとの比較
        if (selected == null || !options.HasPairComparison)
            return true;

        foreach (var picked in selected)
        {
            if (picked == null)
                continue;

            if (options.異なるタイプ)
            {
                if (!candidate.TryGetBattlerType(out var candidateType) ||
                    !picked.TryGetBattlerType(out var pickedType))
                {
                    return false;
                }

                if (candidateType == pickedType)
                    return false;
            }

            if (options.異なるコスト)
            {
                if (GetSelectionCost(candidate) ==
                    GetSelectionCost(picked))
                {
                    return false;
                }
            }
        }

        return true;
    }

    static int GetSelectionCost(SelectionItem item)
    {
        if (item == null)
            return -1;

        if (item.source == SelectionItem.SourceType.Existing)
            return item.existingCard != null
                ? item.existingCard.cost
                : -1;

        if (item.deckData != null)
        {
            item.deckData.PopulateDerivedData();
            return item.deckData.コスト;
        }

        return -1;
    }
}

[Serializable]
public class SelectionComparisonOptions
{
    [Header("選択カード同士の比較")]
    public bool 異なるタイプ;
    public bool 異なるコスト;

    [Header("発動カードとの比較")]
    public bool 発動カードと異なるタイプ;
    public bool 発動カードと異なるコスト;

    public bool HasAny =>
        異なるタイプ ||
        異なるコスト ||
        発動カードと異なるタイプ ||
        発動カードと異なるコスト;

    public bool HasPairComparison =>
        異なるタイプ || 異なるコスト;

    public bool HasSourceComparison =>
        発動カードと異なるタイプ ||
        発動カードと異なるコスト;
}
