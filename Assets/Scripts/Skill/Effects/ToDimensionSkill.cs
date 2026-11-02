using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 指定ゾーンのカードを異次元へ送る。通常の撃破や墓地送りではこのゾーンへは行かない。
/// トークンは着かず消える。墓地に送られた時のトリガーは出さない。
/// </summary>
[System.Serializable]
[SkillDisplayName("異次元へ")]
public class ToDimensionSkill : SkillDB
{
    [SerializeField] public SearchCriteria 参照条件;
    [SerializeField] public CardZoneType 参照元;
    [SerializeField] public int 選択数;
    [SerializeField] public PlayerType 選択プレイヤー;
    [SerializeField] public bool ランダム = false;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context != null && context.TryConsumeSelection(out var selected))
        {
            await SendPassedSelection(context, selected);
            await Task.Delay(300);
            return;
        }

        await SendCardToDimension(context, 参照条件, 参照元, 選択数, 選択プレイヤー);
        await Task.Delay(300);
    }

    public async Task SendCardToDimension(SkillContext context, SearchCriteria criteria, CardZoneType zone, int count, PlayerType selector)
    {
        bool ownerIsLocal = context?.発動カード != null && context.発動カード.isMyCard;
        var sourceZone = CardZones.Of(zone, ownerIsLocal);
        var dimensionZone = DimensionFor(zone, ownerIsLocal);
        if (dimensionZone == null)
        {
            Debug.LogError($"[ToDimensionSkill] 異次元ゾーンがありません zone={zone}");
            return;
        }

        if (CardZones.IsDeck(zone))
            await SendFromDeck(context, criteria, zone, ownerIsLocal, dimensionZone, count, selector);
        else if (sourceZone != null)
            await SendFromExistingCards(context, criteria, sourceZone, dimensionZone, count, selector, zone, ownerIsLocal);
    }

    static Transform DimensionFor(CardZoneType sourceZone, bool ownerIsLocal)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return null;

        bool localSide = BelongsToOwner(sourceZone) == ownerIsLocal;
        return localSide ? battle.pDimensionZone : battle.eDimensionZone;
    }

    static bool BelongsToOwner(CardZoneType zone)
    {
        switch (zone)
        {
            case CardZoneType.プレイヤー手札:
            case CardZoneType.プレイヤーBゾーン:
            case CardZoneType.プレイヤーTゾーン:
            case CardZoneType.プレイヤーFゾーン:
            case CardZoneType.プレイヤー墓地:
            case CardZoneType.プレイヤーデッキ:
            case CardZoneType.プレイヤー異次元:
                return true;
            default:
                return false;
        }
    }

    async Task SendFromDeck(
        SkillContext context,
        SearchCriteria criteria,
        CardZoneType zone,
        bool ownerIsLocal,
        Transform dimensionZone,
        int count,
        PlayerType selector)
    {
        var battle = BattleManager.Instance;
        var deck = CardZones.Deck(zone, ownerIsLocal);
        Transform deckZone = CardZones.Of(zone, ownerIsLocal);
        if (battle == null || deck == null || deckZone == null)
            return;

        var filtered = deck.Where(d => SearchCriteria.AllowsDeck(criteria, d, context)).ToList();
        if (filtered.Count == 0)
            return;

        var cardsToSend = await ChooseDeckCards(filtered, count, selector, ownerIsLocal, context);
        if (cardsToSend == null || cardsToSend.Count == 0)
            return;

        bool localDeck = deck == battle.pBattleDeck;
        var sent = await MoveDeckCards(deck, cardsToSend, deckZone, dimensionZone, localDeck);
        context?.SetProcessedCards(sent);
        battle.UpdateDeckUI();
        await Task.Delay(400);
        if (localDeck && OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }
    }

    async Task SendFromExistingCards(
        SkillContext context,
        SearchCriteria criteria,
        Transform sourceZone,
        Transform dimensionZone,
        int count,
        PlayerType selector,
        CardZoneType sourceZoneType,
        bool ownerIsLocal)
    {
        var all = sourceZone.GetComponentsInChildren<CardController>().ToList();
        var crit = criteria ?? new SearchCriteria();
        var filtered = all.Where(c => c.MeetsCriteria(crit, context)).ToList();
        if (filtered.Count == 0)
            return;

        List<CardController> toSend = await ChooseExistingCards(filtered, count, selector, ownerIsLocal, context);
        CardZoneType absoluteZone = CardController.ZoneTypeFromTransform(sourceZone);
        await MoveExistingCards(context, toSend, sourceZone, dimensionZone, absoluteZone);
    }

    async Task SendPassedSelection(SkillContext context, List<SelectionItem> selections)
    {
        var processed = new List<CardController>();
        var battle = BattleManager.Instance;
        if (battle == null || selections == null)
        {
            context?.SetProcessedCards(processed);
            return;
        }

        var playerDeck = new List<DeckCard>();
        var enemyDeck = new List<DeckCard>();
        var existingByZone = new Dictionary<Transform, List<CardController>>();
        foreach (var sel in selections)
        {
            if (sel == null)
                continue;
            if (sel.source == SelectionItem.SourceType.Deck)
            {
                if (sel.deckData == null)
                    continue;
                if (battle.pBattleDeck.Contains(sel.deckData))
                    playerDeck.Add(sel.deckData);
                else if (battle.eBattleDeck.Contains(sel.deckData))
                    enemyDeck.Add(sel.deckData);
            }
            else if (sel.existingCard != null && sel.existingCard.transform.parent != null)
            {
                var parent = sel.existingCard.transform.parent;
                if (!existingByZone.TryGetValue(parent, out var list))
                {
                    list = new List<CardController>();
                    existingByZone[parent] = list;
                }
                list.Add(sel.existingCard);
            }
        }

        if (playerDeck.Count > 0)
            processed.AddRange(await MoveDeckCards(battle.pBattleDeck, playerDeck, battle.pDeckZone, battle.pDimensionZone, true));
        if (enemyDeck.Count > 0)
            processed.AddRange(await MoveDeckCards(battle.eBattleDeck, enemyDeck, battle.eDeckZone, battle.eDimensionZone, false));

        foreach (var pair in existingByZone)
        {
            if (pair.Value == null || pair.Value.Count == 0 || pair.Key == null)
                continue;
            bool playerSide = pair.Value[0].isPlayerCard;
            var dimension = playerSide ? battle.pDimensionZone : battle.eDimensionZone;
            processed.AddRange(await MoveExistingCards(
                context,
                pair.Value,
                pair.Key,
                dimension,
                CardController.ZoneTypeFromTransform(pair.Key)));
        }

        context?.SetProcessedCards(processed);
        battle.UpdateDeckUI();
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }
    }

    async Task<List<CardController>> MoveDeckCards(
        List<DeckCard> deck,
        List<DeckCard> cardsToSend,
        Transform deckZone,
        Transform dimensionZone,
        bool syncToOpponent)
    {
        var sentCards = new List<CardController>();
        if (deck == null || cardsToSend == null || deckZone == null || dimensionZone == null)
            return sentCards;

        foreach (var deckData in cardsToSend)
        {
            if (deckData == null || !deck.Remove(deckData))
                continue;

            var tempCard = BattleManager.Instance.CreateCard(deckData.カード種類, deckData.カードID, deckZone);
            if (tempCard != null && tempCard.トークン)
            {
                TokenExit.VanishLocal(tempCard);
                continue;
            }

            bool sentSync = false;
            if (syncToOpponent && OnlineSyncManager.Instance != null && tempCard != null)
            {
                OnlineSyncManager.Instance.SyncDeckToDimension(tempCard.cardKind.ToString(), tempCard.cardID);
                sentSync = true;
            }

            if (tempCard != null && CardAnimationManager.Instance != null)
            {
                await CardAnimationManager.Instance.AnimateRelocate(
                    deckZone as RectTransform,
                    dimensionZone as RectTransform,
                    tempCard);
            }

            if (tempCard != null)
            {
                sentCards.Add(tempCard);
                if (dimensionZone == BattleManager.Instance.eDimensionZone)
                    OnlineSyncManager.Instance?.RememberOpponentDimensionCard(tempCard);
            }

            BattleManager.Instance.UpdateDeckUI();
            if (sentSync)
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }

        return sentCards;
    }

    async Task<List<CardController>> MoveExistingCards(
        SkillContext context,
        List<CardController> toSend,
        Transform sourceZone,
        Transform dimensionZone,
        CardZoneType sourceZoneType)
    {
        if (toSend == null || sourceZone == null || dimensionZone == null)
            return new List<CardController>();

        var staying = new List<CardController>();
        foreach (var card in toSend)
        {
            if (card != null && card.トークン)
                TokenExit.VanishSynced(card);
            else if (card != null)
                staying.Add(card);
        }
        toSend = staying;
        if (toSend.Count == 0)
            return new List<CardController>();

        bool batchedBoard = false;
        if (OnlineSyncManager.Instance != null && IsBoardZone(sourceZoneType))
        {
            var kinds = new List<string>();
            var ids = new List<int>();
            var zones = new List<string>();
            var indices = new List<int>();
            foreach (var card in toSend)
                CardAddress.TryAdd(card, kinds, ids, zones, indices);
            if (indices.Count > 0)
            {
                OnlineSyncManager.Instance.SyncAreaToDimension(kinds.ToArray(), ids.ToArray(), zones.ToArray(), indices.ToArray());
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
                batchedBoard = true;
            }
        }

        bool cardIsReceiversHand = sourceZoneType == CardZoneType.相手手札;
        bool syncOnline = OnlineSyncManager.Instance != null && BattleManager.Instance.IsOnlineMode;
        bool offlineCpuHand = BattleManager.Instance != null
            && !BattleManager.Instance.IsOnlineMode
            && sourceZone == BattleManager.Instance.eHandZone;

        foreach (var card in toSend)
        {
            if (offlineCpuHand)
                RemoveOneEnemyHandCard(card);
            OnlineSyncManager.Instance?.NoteCardLeftOpponentDimension(card);
            ResetCardState(card);
            if (!card.isMyCard)
                card.battleCardview?.Show(card.model);

            bool sentPerCard = false;
            if (OnlineSyncManager.Instance != null && !batchedBoard && CardAddress.TryFrom(card, out CardAddress address))
            {
                if (cardIsReceiversHand && syncOnline && BattleManager.Instance.mainCanvas != null)
                    card.transform.SetParent(BattleManager.Instance.mainCanvas.transform, true);

                OnlineSyncManager.Instance.SyncCardToDimension(
                    address.Kind.ToString(),
                    address.Id,
                    address.ZoneName,
                    address.Index);
                sentPerCard = true;
            }

            if (CardAnimationManager.Instance != null)
            {
                await CardAnimationManager.Instance.AnimateRelocate(
                    sourceZone as RectTransform,
                    dimensionZone as RectTransform,
                    card);
            }

            if (sentPerCard)
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();

            card.UpdateDisplay();
            if (dimensionZone == BattleManager.Instance.eDimensionZone)
                OnlineSyncManager.Instance?.RememberOpponentDimensionCard(card);
        }

        if (cardIsReceiversHand && syncOnline)
            BattleManager.Instance.UpdateEnemyHandUI();

        context?.SetProcessedCards(toSend);
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }

        PersistentEffectManager.Instance?.RecalculateImmediately();
        return toSend;
    }

    static void ResetCardState(CardController card)
    {
        if (card == null)
            return;

        if (card.cardKind == CardKind.バトラー && card.model is BattlerModel battler)
            card.curHp = battler.maxHp;
        else if (card.cardKind == CardKind.フィールド && card.model is FieldModel field)
            card.curHp = field.maxHp;

        card.hasUsedMove = false;
        card.damageBonus = 0;
        card.hitRate = 0;
        card.hpProtection1 = false;
        card.hpProtectionTargetElement1 = ElementList.None;
        card.hpProtection2 = false;
        card.hpProtectionTargetElement2 = ElementList.None;
        card.RemoveAllTurnRestrictions();
        card.ClearInvalidElements();
        card.RemoveAllPassiveEffects();
        card.ClearStatusAilments();
        card.ResetMoveElements();
        card.RecalculateModifiedPowers();
    }

    static bool IsBoardZone(CardZoneType zoneType)
    {
        return zoneType == CardZoneType.プレイヤーBゾーン
            || zoneType == CardZoneType.プレイヤーTゾーン
            || zoneType == CardZoneType.プレイヤーFゾーン
            || zoneType == CardZoneType.相手Bゾーン
            || zoneType == CardZoneType.相手Tゾーン
            || zoneType == CardZoneType.相手Fゾーン;
    }

    async Task<List<DeckCard>> ChooseDeckCards(
        List<DeckCard> filtered,
        int count,
        PlayerType selector,
        bool ownerIsLocal,
        SkillContext context)
    {
        if (filtered == null || filtered.Count == 0 || count <= 0)
            return new List<DeckCard>();
        if (filtered.Count <= count)
            return filtered;

        int take = Mathf.Min(count, filtered.Count);
        if (ランダム || !ChooserIsLocal(selector, ownerIsLocal))
        {
            if (ShouldAskRemote(selector, ownerIsLocal, context))
            {
                var identities = new List<(int kind, int id)>();
                foreach (var deck in filtered)
                    identities.Add(deck != null ? ((int)deck.カード種類, deck.カードID) : (0, 0));
                int[] indices = await RequestRemotePick(identities, take, context);
                return TakeByIndices(filtered, indices, take);
            }

            return filtered.OrderBy(_ => Random.value).Take(take).ToList();
        }

        if (CardSelectionUI.Instance == null)
            return filtered.OrderBy(_ => Random.value).Take(take).ToList();

        var selection = await CardSelectionUI.Instance.ShowAsync(
            Enumerable.Empty<CardController>(),
            filtered,
            take,
            rejectSameKind: false,
            letCpuChoose: false);
        var chosen = new List<DeckCard>();
        if (selection == null)
            return chosen;
        foreach (var item in selection)
        {
            if (item?.deckData != null)
                chosen.Add(item.deckData);
        }
        return chosen;
    }

    async Task<List<CardController>> ChooseExistingCards(
        List<CardController> filtered,
        int count,
        PlayerType selector,
        bool ownerIsLocal,
        SkillContext context)
    {
        if (filtered == null || filtered.Count == 0 || count <= 0)
            return new List<CardController>();
        if (filtered.Count <= count)
            return filtered;

        int take = Mathf.Min(count, filtered.Count);
        if (ランダム || !ChooserIsLocal(selector, ownerIsLocal))
        {
            if (ShouldAskRemote(selector, ownerIsLocal, context))
            {
                var identities = new List<(int kind, int id)>();
                foreach (var card in filtered)
                    identities.Add(card != null ? ((int)card.cardKind, card.cardID) : (0, 0));
                int[] indices = await RequestRemotePick(identities, take, context);
                return TakeByIndices(filtered, indices, take);
            }

            return filtered.OrderBy(_ => Random.value).Take(take).ToList();
        }

        if (CardSelectionUI.Instance == null)
            return filtered.OrderBy(_ => Random.value).Take(take).ToList();

        var selections = await CardSelectionUI.Instance.ShowAsync(
            existing: filtered,
            deckItems: new List<DeckCard>(),
            count: take,
            rejectSameKind: false,
            letCpuChoose: false);
        var chosen = new List<CardController>();
        if (selections == null)
            return chosen;
        foreach (var item in selections)
        {
            if (item?.existingCard != null)
                chosen.Add(item.existingCard);
        }
        return chosen;
    }

    bool ShouldAskRemote(PlayerType selector, bool ownerIsLocal, SkillContext context)
    {
        if (ランダム || ChooserIsLocal(selector, ownerIsLocal))
            return false;

        var battle = BattleManager.Instance;
        return ownerIsLocal
            && battle != null
            && battle.IsOnlineMode
            && context?.発動カード != null
            && OnlineSyncManager.Instance != null;
    }

    static bool ChooserIsLocal(PlayerType selectBy, bool ownerIsLocal)
    {
        bool chooserIsOwner = selectBy != PlayerType.相手;
        return chooserIsOwner ? ownerIsLocal : !ownerIsLocal;
    }

    static async Task<int[]> RequestRemotePick(List<(int kind, int id)> identities, int count, SkillContext context)
    {
        int length = identities != null ? identities.Count : 0;
        var kinds = new int[length];
        var ids = new int[length];
        for (int i = 0; i < length; i++)
        {
            kinds[i] = identities[i].kind;
            ids[i] = identities[i].id;
        }

        return await OnlineSyncManager.Instance.RequestOpponentCardPick(context.発動カード, kinds, ids, count);
    }

    static List<T> TakeByIndices<T>(List<T> pool, int[] indices, int count)
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

    static void RemoveOneEnemyHandCard(CardController card)
    {
        var hand = BattleManager.Instance?.enemyHand;
        if (hand == null || card == null)
            return;

        for (int i = 0; i < hand.Count; i++)
        {
            var deck = hand[i];
            if (deck != null && deck.カード種類 == card.cardKind && deck.カードID == card.cardID)
            {
                hand.RemoveAt(i);
                return;
            }
        }
    }
}
