using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("墓地へ")]
public class ToGraveSkill : SkillDB
{
    [SerializeField] public SearchCriteria 参照条件; // 検索条件
    [SerializeField] public CardZoneType 参照元; // 送る元のゾーン
    [SerializeField] public int 選択数; // 選択数
    [SerializeField] public PlayerType 選択プレイヤー; // どちらが選択するか
    [SerializeField] public bool ランダム = false;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context != null && context.TryConsumeSelection(out var selected))
        {
            await SendPassedSelectionToGrave(context, selected);
            await Task.Delay(300);
            return;
        }

        await SendCardToGrave(context, 参照条件, 参照元, 選択数, 選択プレイヤー);

        await Task.Delay(300);
    }

    public async Task SendCardToGrave(SkillContext context, SearchCriteria criteria, CardZoneType zone, int count, PlayerType selector)
    {
        Debug.Log($"[SendToGraveSkill] SendCardToGraveyard開始: zone={zone}, context.SourceCard={context?.発動カード?.cardName}, isMyCard={context?.発動カード?.isMyCard}");
        
        bool ownerIsLocal = context?.発動カード != null && context.発動カード.isMyCard;
        var sourceZoneTransform = CardZones.Of(zone, ownerIsLocal);
        var graveZoneTransform = CardZones.GraveFor(zone, ownerIsLocal);
        
        if (sourceZoneTransform == null || graveZoneTransform == null)
        {
            Debug.LogError($"[SendToGraveSkill] ゾーン設定エラー: sourceZoneTransform={sourceZoneTransform?.name}, graveZoneTransform={graveZoneTransform?.name}, zone={zone}, context.SourceCard={context?.発動カード?.cardName}");
            return;
        }

        // デッキからの場合の処理
        if (CardZones.IsDeck(zone))
        {
            await SendFromDeck(context, criteria, graveZoneTransform, count, selector);
        }
        // 実在カードからの場合の処理
        else
        {
            await SendFromExistingCards(context, criteria, sourceZoneTransform, graveZoneTransform, count, selector, zone);
        }
    }

    private async Task SendFromDeck(SkillContext context, SearchCriteria criteria, Transform graveZone, int count, PlayerType selector)
    {
        // 条件に合致するカードをデッキから検索
        var filtered = BattleManager.Instance.pBattleDeck
            .Where(d => SearchCriteria.AllowsDeck(criteria, d, context))
            .ToList();

        if (filtered.Count == 0)
        {
            Debug.Log("SkillManager: 対応するカードがデッキにありません。");
            return;
        }

        List<DeckCard> cardsToSend;
        if (filtered.Count > count)
        {
            // CardSelectionUI を使って、デッキからの選択を行う
            var selection = await CardSelectionUI.Instance.ShowAsync(
                existing: Enumerable.Empty<CardController>(),
                deckItems: filtered,
                count: count
            );
            cardsToSend = selection.Select(s => s.deckData).ToList();
        }
        else
        {
            // 条件に合致するカードが選択数以下の場合は全て移動
            cardsToSend = filtered;
        }

        // デッキゾーンの位置を取得（アニメーション用）
        Transform deckZone = GetDeckZoneTransform();
        if (deckZone == null)
        {
            Debug.LogError("デッキゾーンが見つかりません");
            return;
        }

        // オンライン時は相手にアニメーション同期を送信
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
        }

        var sentCards = await MoveDeckCardsToGrave(
            BattleManager.Instance.pBattleDeck,
            cardsToSend,
            deckZone,
            graveZone,
            syncToOpponent: true);

        context?.SetProcessedCards(sentCards);

        BattleManager.Instance.UpdateDeckUI();

        await Task.Delay(400);

        // アニメーション完了後に同期（オンライン時）
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
        }
    }

    private async Task SendFromExistingCards(SkillContext context, SearchCriteria criteria, Transform sourceZone, Transform graveZone, int count, PlayerType selector, CardZoneType sourceZoneType)
    {
        // 1. 実在カードフィルタ
        var all = sourceZone.GetComponentsInChildren<CardController>().ToList();
        var crit = criteria ?? new SearchCriteria();
        var filtered = all.Where(c => c.MeetsCriteria(crit, context)).ToList();

        // デバッグログ追加
        Debug.Log($"[SendToGraveSkill] SendFromExistingCards開始: sourceZone={sourceZone?.name}, sourceZoneType={sourceZoneType}, isMyCard={context?.発動カード?.isMyCard}, 全カード数={all.Count}, フィルタ後={filtered.Count}");
        if (all.Count > 0)
        {
            Debug.Log($"[SendToGraveSkill] ゾーン内のカード: {string.Join(", ", all.Select(c => $"{c.cardName}(ID:{c.cardID})"))}");
        }

        if (filtered.Count == 0)
        {
            Debug.LogWarning($"[SendToGraveSkill] 対象となるカードが指定ゾーンにありません。sourceZone={sourceZone?.name}, sourceZoneType={sourceZoneType}, isMyCard={context?.発動カード?.isMyCard}, 全カード数={all.Count}");
            return;
        }

        // 2. 選択 or ランダム選択
        List<CardController> toSend;

        if (count <= 0)
        {
            toSend = new List<CardController>();
        }
        else if (filtered.Count <= count)
        {
            toSend = filtered;
        }
        else if (ランダム)
        {
            toSend = filtered.OrderBy(_ => Random.value).Take(count).ToList();
        }
        else if (selector == PlayerType.プレイヤー)
        {
            // CardSelectionUI.ShowAsync で実在カードのみを表示
            var selections = await CardSelectionUI.Instance.ShowAsync(
                existing: filtered,
                deckItems: new List<DeckCard>(),
                count: count
            );
            toSend = selections.Select(s => s.existingCard).ToList();
        }
        else
            return;

        await MoveExistingCardsToGrave(context, toSend, sourceZone, graveZone, sourceZoneType);
    }

    async Task SendPassedSelectionToGrave(SkillContext context, List<SelectionItem> selections)
    {
        var processed = new List<CardController>();
        if (selections == null || selections.Count == 0)
        {
            context?.SetProcessedCards(processed);
            return;
        }

        var battle = BattleManager.Instance;
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
            else if (sel.existingCard != null)
            {
                var parent = sel.existingCard.transform.parent;
                if (parent == null)
                    continue;
                if (!existingByZone.TryGetValue(parent, out var list))
                {
                    list = new List<CardController>();
                    existingByZone[parent] = list;
                }
                list.Add(sel.existingCard);
            }
        }

        if (playerDeck.Count > 0)
        {
            processed.AddRange(await MoveDeckCardsToGrave(
                battle.pBattleDeck, playerDeck, battle.pDeckZone, battle.pGraveZone, syncToOpponent: true));
        }
        if (enemyDeck.Count > 0)
        {
            processed.AddRange(await MoveDeckCardsToGrave(
                battle.eBattleDeck, enemyDeck, battle.eDeckZone, battle.eGraveZone, syncToOpponent: false));
        }

        foreach (var pair in existingByZone)
        {
            var cards = pair.Value;
            if (cards == null || cards.Count == 0 || pair.Key == null)
                continue;

            bool playerSide = cards[0].isPlayerCard;
            var grave = playerSide ? battle.pGraveZone : battle.eGraveZone;
            var zoneType = CardController.ZoneTypeFromTransform(pair.Key);
            processed.AddRange(await MoveExistingCardsToGrave(context, cards, pair.Key, grave, zoneType));
        }

        context?.SetProcessedCards(processed);
        battle.UpdateDeckUI();
        if (playerDeck.Count > 0 || enemyDeck.Count > 0)
            await Task.Delay(400);
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
        }
    }

    async Task<List<CardController>> MoveDeckCardsToGrave(
        List<DeckCard> deck,
        List<DeckCard> cardsToSend,
        Transform deckZone,
        Transform graveZone,
        bool syncToOpponent)
    {
        var sentCards = new List<CardController>();
        if (deck == null || cardsToSend == null || deckZone == null || graveZone == null)
            return sentCards;

        foreach (var deckData in cardsToSend)
        {
            if (deckData == null || !deck.Remove(deckData))
                continue;

            var tempCard = BattleManager.Instance.CreateCard(deckData.カード種類, deckData.カードID, deckZone);

            bool sentSync = false;
            if (syncToOpponent && OnlineSyncManager.Instance != null)
            {
                OnlineSyncManager.Instance.SyncDeckToGraveyard(
                    tempCard.cardKind.ToString(),
                    tempCard.cardID
                );
                sentSync = true;
            }

            await CardAnimationManager.Instance.AnimateDiscard(
                deckZone as RectTransform,
                graveZone as RectTransform,
                tempCard);
            sentCards.Add(tempCard);
            BattleManager.Instance.UpdateDeckUI();
            if (sentSync)
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }

        return sentCards;
    }

    async Task<List<CardController>> MoveExistingCardsToGrave(
        SkillContext context,
        List<CardController> toSend,
        Transform sourceZone,
        Transform graveZone,
        CardZoneType sourceZoneType)
    {
        if (toSend == null || sourceZone == null || graveZone == null)
            return new List<CardController>();

        bool batchedBoard = false;
        if (OnlineSyncManager.Instance != null && IsBoardZone(sourceZoneType) && toSend.Count > 0)
        {
            var graveKinds = new List<string>();
            var graveIds = new List<int>();
            var graveZones = new List<string>();
            var graveIndices = new List<int>();
            foreach (var card in toSend)
                CardAddress.TryAdd(card, graveKinds, graveIds, graveZones, graveIndices);

            if (graveIndices.Count > 0)
            {
                OnlineSyncManager.Instance.SyncAreaGrave(graveKinds.ToArray(), graveIds.ToArray(), graveZones.ToArray(), graveIndices.ToArray());
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
                batchedBoard = true;
            }
        }

        // 相手手札は、この端末では表示用のカード。実カードは相手端末の手札にある。
        bool cardIsReceiversHand = sourceZoneType == CardZoneType.相手手札;
        bool syncOnline = OnlineSyncManager.Instance != null && BattleManager.Instance.IsOnlineMode;
        var discardedMirror = new List<(CardKind kind, int id)>();

        // 3. カードを墓地に移動
        foreach (var card in toSend)
        {
            ResetCardStateForGraveyard(card);
            if (!card.isMyCard)
                card.battleCardview?.Show(card.model);
            CardKind discardedKind = card.cardKind;
            int discardedId = card.cardID;
            RectTransform sourceSlot = sourceZone as RectTransform;
            RectTransform graveSlot = graveZone as RectTransform;

            bool sentPerCard = false;
            if (OnlineSyncManager.Instance != null)
            {
                if (IsHandZone(sourceZoneType) || !IsBoardZone(sourceZoneType))
                {
                    bool addressed = CardAddress.TryFrom(card, out CardAddress address);
                    if (cardIsReceiversHand && syncOnline && BattleManager.Instance.mainCanvas != null)
                        card.transform.SetParent(BattleManager.Instance.mainCanvas.transform, true);

                    if (addressed)
                    {
                        OnlineSyncManager.Instance.SyncDiscardAnimation(
                            address.Kind.ToString(),
                            address.Id,
                            address.ZoneName,
                            address.Index);
                        sentPerCard = true;
                    }
                }
                else if (!batchedBoard && CardAddress.TryFrom(card, out CardAddress boardAddress))
                {
                    OnlineSyncManager.Instance.SyncCardToGrave(
                        boardAddress.Kind.ToString(),
                        boardAddress.Id,
                        boardAddress.ZoneName,
                        boardAddress.Index);
                    sentPerCard = true;
                }
            }

            await CardAnimationManager.Instance.AnimateDiscard(
                sourceSlot,
                graveSlot,
                card,
                suppressSkillTrigger: cardIsReceiversHand && syncOnline);
            if (sentPerCard)
            {
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
            }

            if (cardIsReceiversHand && syncOnline)
            {
                discardedMirror.Add((discardedKind, discardedId));
                continue;
            }

            card.UpdateDisplay();
            card.battleCardview?.UpdateCanUseMovePanel();
        }

        List<CardController> processed;
        if (cardIsReceiversHand && syncOnline)
        {
            BattleManager.Instance.UpdateEnemyHandUI();
            BattleManager.Instance.UpdateEnemyGraveyardUI();
            processed = ResolveMirrorGraveCards(discardedMirror);
            context?.SetProcessedCards(processed);
            ScheduleOpponentHandGraveTriggers(discardedMirror, processed);
        }
        else
        {
            processed = toSend;
            context?.SetProcessedCards(processed);
        }

        // アニメーション完了後に同期（オンライン時）
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
        }

        return processed ?? new List<CardController>();
    }

    // 相手の手札一覧で作り直した墓地カードを、捨てた順に取り出す。見つからない位置は null。
    private List<CardController> ResolveMirrorGraveCards(List<(CardKind kind, int id)> discarded)
    {
        var graveCards = BattleManager.Instance.eGraveZone != null
            ? BattleManager.Instance.eGraveZone.GetComponentsInChildren<CardController>()
            : System.Array.Empty<CardController>();
        var used = new HashSet<int>();
        var resolved = new CardController[discarded.Count];

        for (int i = discarded.Count - 1; i >= 0; i--)
        {
            for (int g = graveCards.Length - 1; g >= 0; g--)
            {
                CardController graveCard = graveCards[g];
                if (graveCard == null || used.Contains(graveCard.GetInstanceID()))
                    continue;
                if (graveCard.cardKind != discarded[i].kind || graveCard.cardID != discarded[i].id)
                    continue;

                used.Add(graveCard.GetInstanceID());
                resolved[i] = graveCard;
                break;
            }
        }

        return new List<CardController>(resolved);
    }

    // 起動効果の窓が閉じたあと、両者が同じ「墓地に送られた時」の窓へ入る。
    private void ScheduleOpponentHandGraveTriggers(List<(CardKind kind, int id)> discarded, List<CardController> resolved)
    {
        for (int i = 0; i < discarded.Count; i++)
        {
            CardController card = i < resolved.Count ? resolved[i] : null;
            string graveZone = string.Empty;
            int graveIndex = -1;
            if (card != null && CardAddress.TryFrom(card, out CardAddress graveAddress))
            {
                graveZone = graveAddress.ZoneName;
                graveIndex = graveAddress.Index;
            }
            SkillChainManager.Instance?.ScheduleSentToGraveyard(card, CardZoneType.相手手札);
            OnlineSyncManager.Instance?.SyncSentToGraveyard(
                discarded[i].kind.ToString(),
                discarded[i].id,
                graveZone,
                graveIndex,
                (int)CardZoneType.相手手札);
        }
    }

    private bool IsHandZone(CardZoneType zoneType)
    {
        return zoneType == CardZoneType.プレイヤー手札 || zoneType == CardZoneType.相手手札;
    }

    private bool IsBoardZone(CardZoneType zoneType)
    {
        return zoneType == CardZoneType.プレイヤーBゾーン
            || zoneType == CardZoneType.プレイヤーTゾーン
            || zoneType == CardZoneType.プレイヤーFゾーン
            || zoneType == CardZoneType.相手Bゾーン
            || zoneType == CardZoneType.相手Tゾーン
            || zoneType == CardZoneType.相手Fゾーン;
    }

    private void ResetCardStateForGraveyard(CardController card)
    {
        if (card == null)
        {
            return;
        }

        if (card.cardKind == CardKind.バトラー)
        {
            if (card.model is BattlerModel battlerModel)
            {
                card.curHp = battlerModel.maxHp;
            }
        }
        else if (card.cardKind == CardKind.フィールド)
        {
            if (card.model is FieldModel fieldModel)
            {
                card.curHp = fieldModel.maxHp;
            }
        }

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

    private Transform GetDeckZoneTransform()
    {
        // プレイヤーのデッキゾーンを返す（アニメーション用）
        return BattleManager.Instance.pDeckZone;
    }
}
