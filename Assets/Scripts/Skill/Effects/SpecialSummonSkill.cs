using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("特殊登場")]
public class SpecialSummonSkill : SkillDB
{
    const int BattlerZoneLimit = 5;

    public List<CardZoneType> 参照ゾーンリスト = new List<CardZoneType>();
    public SearchCriteria 対象条件;

    [Tooltip("支払うコストの計算式。空欄ならコストを払わない。例: card.cost / card.cost - 1")]
    public string 支払うコスト式;

    public override async Task ActivateEffect(SkillContext context)
    {
        var cond = 対象条件;
        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;

        // --- 1) すべての選択候補をリストアップして整理 ---
        var existingCandidates = new List<CardController>();
        var deckCandidates = new List<DeckCard>();

        existingCandidates = existingCandidates
            .Where(c => MeetsConditionsForCandidate(context, c))
            .ToList();

        foreach (var zone in 参照ゾーンリスト)
        {
            if (CardZones.IsDeck(zone))
            {
                var deck = CardZones.Deck(zone, ownerIsLocal);
                if (deck != null)
                    deckCandidates.AddRange(deck.Where(d => SearchCriteria.AllowsDeck(cond, d, context)));
                continue;
            }

            if (!IsSummonableZone(zone))
                continue;

            Transform zoneTransform = CardZones.Of(zone, ownerIsLocal);
            if (zoneTransform == null)
                continue;

            existingCandidates.AddRange(
                zoneTransform.GetComponentsInChildren<CardController>()
                    .Where(c => c.MeetsCriteria(cond, context)));
        }

        // --- 2) 同一カード制限のチェック：候補リストから同一カードを排除 ---
        // 既存カードの候補から、同一カード制限により場に出せないカードを排除
        existingCandidates = existingCandidates
            .Where(c => !UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(c))
            .ToList();

        // デッキカードの候補から、同一カード制限により場に出せないカードを排除
        bool isMyCard = context.発動カード != null && context.発動カード.isMyCard;
        deckCandidates = deckCandidates
            .Where(d => !UniqueCardRestrictionHelper.IsDeckCardRestrictedByUniqueCardRule(d, isMyCard))
            .ToList();

        // 登場先ゾーンが埋まっているカードは選べない。デッキのカードは発動した側に作って出す。
        bool deckToPlayer = context.発動カード != null && context.発動カード.isPlayerCard;
        existingCandidates = existingCandidates
            .Where(c => CanSummonToZone(c.cardKind, c.isMyCard))
            .ToList();
        deckCandidates = deckCandidates
            .Where(d => CanSummonToZone(d.カード種類, deckToPlayer))
            .ToList();

        // --- 3) 候補がないチェック ---
        if (existingCandidates.Count == 0 && deckCandidates.Count == 0)
            return;

        bool payCost = !string.IsNullOrWhiteSpace(支払うコスト式);
        int availableCost = payCost ? CurrentCost(context) : int.MaxValue;
        bool cpuChooses = payCost && SkillContext.IsOfflineCpuResolving;

        if (cpuChooses)
        {
            existingCandidates = existingCandidates
                .Where(c => c != null && GetSummonCost(c) <= availableCost)
                .ToList();
            deckCandidates = deckCandidates
                .Where(d => d != null && GetSummonCost(d) <= availableCost)
                .ToList();

            if (existingCandidates.Count == 0 && deckCandidates.Count == 0)
                return;
        }

        Func<IReadOnlyList<SelectionItem>, SelectionItem, bool> cantAfford = null;
        if (payCost && !cpuChooses)
            cantAfford = (_, item) => PaymentOf(item) > availableCost;

        // --- 4) 候補が1枚しかない場合は自動選択 ---
        // コストを払うときは、払えない1枚も一覧に出してグレーにする。
        SelectionItem sel;
        int candidateCount = existingCandidates.Count + deckCandidates.Count;
        SelectionItem lone = null;
        if (candidateCount == 1)
        {
            lone = existingCandidates.Count == 1
                ? new SelectionItem { source = SelectionItem.SourceType.Existing, existingCard = existingCandidates[0] }
                : new SelectionItem { source = SelectionItem.SourceType.Deck, deckData = deckCandidates[0] };
        }
        bool onlyUnaffordable = lone != null && payCost && !cpuChooses && PaymentOf(lone) > availableCost;

        if (lone != null && !onlyUnaffordable)
        {
            sel = lone;
        }
        else
        {
            // --- 5) UI で選択（既存カードがあれば優先） ---
            var selections = await CardSelectionUI.Instance
                .ShowAsync(existingCandidates, deckCandidates, 1, rejectWhen: cantAfford);
            if (selections == null || selections.Count == 0) return;

            sel = selections[0];
        }

        // 選んでいるあいだに埋まったときは出さない
        bool selectedIsPlayer = sel.source == SelectionItem.SourceType.Existing
            ? sel.existingCard != null && sel.existingCard.isMyCard
            : deckToPlayer;
        CardKind selectedKind = sel.source == SelectionItem.SourceType.Existing
            ? (sel.existingCard != null ? sel.existingCard.cardKind : CardKind.None)
            : (sel.deckData != null ? sel.deckData.カード種類 : CardKind.None);
        if (!CanSummonToZone(selectedKind, selectedIsPlayer))
            return;

        CardController toSummon = null;

        // --- 6) 選択結果に応じて本格処理 ---
        SummonSource summonSource = SummonSource.手札; // デフォルトは手札

        if (sel.source == SelectionItem.SourceType.Existing)
        {
            toSummon = sel.existingCard;
            // 既存カードの場合は召喚元を判定
            if (toSummon.transform.parent == BattleManager.Instance.pGraveZone ||
                toSummon.transform.parent == BattleManager.Instance.eGraveZone)
            {
                summonSource = SummonSource.墓地;
            }
            else if (toSummon.transform.parent == BattleManager.Instance.pDimensionZone ||
                     toSummon.transform.parent == BattleManager.Instance.eDimensionZone)
            {
                summonSource = SummonSource.異次元;
            }
            else if (toSummon.transform.parent == BattleManager.Instance.pHandZone ||
                     toSummon.transform.parent == BattleManager.Instance.eHandZone)
            {
                summonSource = SummonSource.手札;
            }
        }
        else // Deck
        {
            var pick = sel.deckData;
            summonSource = SummonSource.デッキ;

            // デッキリストから削除
            if (BattleManager.Instance.pBattleDeck.Contains(pick)) BattleManager.Instance.pBattleDeck.Remove(pick);
            else if (BattleManager.Instance.eBattleDeck.Contains(pick)) BattleManager.Instance.eBattleDeck.Remove(pick);

            // 親ゾーン
            var parentZone = context.発動カード.isPlayerCard
                ? BattleManager.Instance.pDeckZone
                : BattleManager.Instance.eDeckZone;

            // カードを作成
            toSummon = BattleManager.Instance.CreateCard(
                pick.カード種類,
                pick.カードID,
                parentZone
            );
        }

        // --- 7) 選択されたカードが同一カード制限により場に出せないか再チェック（念のため） ---
        if (toSummon != null && UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(toSummon))
            return;

        // --- 8) SummonRoutine で召喚処理 ---
        if (toSummon != null)
        {
            if (payCost)
            {
                int summonCost = PaymentOf(sel);
                if (!TryPaySummonCost(context, summonCost))
                    return;
            }

            // バトラー→Bゾーン、作戦→Tゾーン、フィールド→Fゾーン
            var targetZone = ResolveSummonZone(toSummon.cardKind, toSummon.isMyCard);
            if (targetZone == null || !CanSummonToZone(toSummon.cardKind, toSummon.isMyCard))
                return;

            // 通常召喚は出す前に手札リストから外す。特殊登場は表示だけ動くので、ここで同じ1枚を外す。
            if (summonSource == SummonSource.手札)
                BattleManager.Instance.ConsumeOfflineEnemyHand(toSummon);

            bool playVoice = toSummon.cardKind != CardKind.作戦;
            await BattleManager.Instance.SummonCard(toSummon, targetZone, playVoice: playVoice, deductCost: false, summonSource: summonSource, specialSummon: true);
            context?.SetProcessedCards(new[] { toSummon });
        }

        BattleManager.Instance.UpdateDeckUI();
    }

    static Transform ResolveSummonZone(CardKind kind, bool playerSide)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return null;

        return kind switch
        {
            CardKind.バトラー => playerSide ? battle.pBattlerZone : battle.eBattlerZone,
            CardKind.作戦 => playerSide ? battle.pTacticsZone : battle.eTacticsZone,
            CardKind.フィールド => playerSide ? battle.pFieldZone : battle.eFieldZone,
            _ => null
        };
    }

    /// <summary>
    /// カード種類に応じた登場先（B/T/F）に空きがあるか。
    /// </summary>
    public static bool CanSummonToZone(CardKind kind, bool playerSide)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return false;

        switch (kind)
        {
            case CardKind.バトラー:
            {
                Transform zone = playerSide ? battle.pBattlerZone : battle.eBattlerZone;
                return zone != null && zone.childCount < BattlerZoneLimit;
            }
            case CardKind.作戦:
                return battle.TacticsZoneHasRoom(playerSide);
            case CardKind.フィールド:
            {
                Transform zone = playerSide ? battle.pFieldZone : battle.eFieldZone;
                return zone != null && zone.childCount == 0;
            }
            default:
                return false;
        }
    }

    int PaymentOf(SelectionItem item)
    {
        if (item == null)
            return 0;

        if (item.source == SelectionItem.SourceType.Existing)
            return GetSummonCost(item.existingCard);

        return GetSummonCost(item.deckData);
    }

    int GetSummonCost(CardController card)
    {
        int baseCost = card != null ? Mathf.Max(0, card.GetModifiedCost()) : 0;
        return CalculateCost(baseCost);
    }

    int GetSummonCost(DeckCard card)
    {
        int baseCost = card != null ? Mathf.Max(0, card.コスト) : 0;
        return CalculateCost(baseCost);
    }

    int CalculateCost(int cardCost)
    {
        // 空欄ならコストを支払わない
        if (string.IsNullOrWhiteSpace(支払うコスト式))
            return 0;

        string expression = 支払うコスト式
            .Trim()
            .Replace("card.cost", cardCost.ToString());

        // 数字だけ
        if (int.TryParse(expression, out int fixedCost))
            return Mathf.Max(0, fixedCost);

        // 「+ 数字」「- 数字」を処理
        var match = Regex.Match(
            expression,
            @"^(\d+)\s*([+-])\s*(\d+)$");

        if (match.Success)
        {
            int left = int.Parse(match.Groups[1].Value);
            int right = int.Parse(match.Groups[3].Value);

            int result = match.Groups[2].Value == "+"
                ? left + right
                : left - right;

            return Mathf.Max(0, result);
        }

        Debug.LogWarning($"特殊登場のコスト式を解析できません: {支払うコスト式}");
        return 0;
    }

    int CurrentCost(SkillContext context)
    {
        var resources = PayerResources(context);
        if (resources == null || resources.Cost == null) return 0;
        return Mathf.Max(0, resources.Cost.currentCost);
    }

    ResourceGroup PayerResources(SkillContext context)
    {
        if (BattleManager.Instance == null)
            return null;
        bool player = context?.発動カード == null || context.発動カード.isPlayerCard;
        return player ? BattleManager.Instance.pResources : BattleManager.Instance.eResources;
    }

    bool TryPaySummonCost(SkillContext context, int cost)
    {
        int pay = Mathf.Max(0, cost);
        if (pay == 0)
            return true;
        if (pay > CurrentCost(context))
            return false;

        var resources = PayerResources(context);
        if (resources == null || resources.Cost == null)
            return false;

        resources.Cost.TakeCost(pay);
        bool player = context?.発動カード == null || context.発動カード.isPlayerCard;
        if (player && BattleManager.Instance.IsOnlineMode && OnlineSyncManager.Instance != null)
            OnlineSyncManager.Instance.SyncOpponentCostChange(pay);
        return true;
    }

    bool MeetsConditionsForCandidate(SkillContext context, CardController candidate)
    {
        if (条件 == null || 条件.Count == 0)
            return true;

        context.SetConditionTarget(candidate);

        foreach (var condition in 条件)
        {
            if (condition == null)
                continue;

            if (!condition.IsConditionMet(context))
            {
                context.SetConditionTarget(null);
                return false;
            }
        }

        context.SetConditionTarget(null);
        return true;
    }

    bool MeetsOwnConditions(SkillContext context)
    {
        if (条件 == null || 条件.Count == 0)
            return true;

        foreach (var condition in 条件)
        {
            if (condition == null)
                continue;
            if (context == null || !condition.IsConditionMet(context))
                return false;
        }

        return true;
    }

    static bool IsSummonableZone(CardZoneType zone)
    {
        return zone == CardZoneType.プレイヤー手札
            || zone == CardZoneType.プレイヤー墓地
            || zone == CardZoneType.プレイヤー異次元
            || zone == CardZoneType.相手手札
            || zone == CardZoneType.相手墓地
            || zone == CardZoneType.相手異次元;
    }
}