using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("墓地へ")]
public class ToGraveSkill : SkillDB
{
    [SerializeField] public SearchCriteria 参照条件;
    [SerializeField] public int 処理カード番号 = 0;
    [SerializeField] public List<CardZoneType> 参照場所 = new List<CardZoneType>();
    [SerializeField] public int 選択数 = 1;
    [SerializeField] public CountMode 選択数の扱い = CountMode.ちょうど;
    [SerializeField] public PlayerType 選択プレイヤー = PlayerType.プレイヤー;
    [SerializeField] public bool ランダム = false;

    [SerializeField] public bool 全選択 = false;
    [SerializeField] public SkillArg 出力キー = SkillArg.なし;
    [SerializeField] public GraveHpValue 出力する値 = GraveHpValue.現在HP;

    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    List<SelectionItem> selections;
    int pendingHpOutput;

    public override async Task ActivateEffect(SkillContext context)
    {
        pendingHpOutput = 0;
        try
        {
            if (context != null && context.TryConsumeSelection(out var selected))
            {
                await SendPassedSelectionToGrave(context, selected);
                await Task.Delay(300);
                return;
            }

            await SendCardToGrave(context,参照条件,参照場所,選択数,選択プレイヤー);

            await Task.Delay(300);
        }
        finally
        {
            WriteHpOutput(context);
        }
    }

    public async Task SendCardToGrave(SkillContext context, SearchCriteria criteria, List<CardZoneType> zones, int count, PlayerType selector)
    {
        var selections = await SkillSelectionHelper.CollectAndSelectAsync(
            context,
            criteria,
            zones,
            count,
            selector,
            選択数の扱い,
            ランダム,
            全選択,
            c => ConditionsMet(対象状態, c));

        if (selections == null || selections.Count == 0)
            return;

        await SendPassedSelectionToGrave(context, selections);
    }

    async Task SendPassedSelectionToGrave(SkillContext context, List<SelectionItem> selections)
    {
        var processed = new List<CardController>();

        if (selections == null)
        {
            context?.SetProcessedCards(処理カード番号, processed);
            return;
        }

        selections = selections
            .Where(s =>
                s != null &&
                (s.source == SelectionItem.SourceType.Deck ||
                 (s.existingCard != null &&
                  ConditionsMet(対象状態, s.existingCard))))
            .ToList();

        if (selections.Count == 0)
        {
            context?.SetProcessedCards(処理カード番号, processed);
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
            if (battle.IsOnlineMode && OnlineSyncManager.Instance != null)
                processed.AddRange(await SendOpponentDeckCardsToGrave(enemyDeck));
            else
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

        context?.SetProcessedCards(処理カード番号,processed);
        battle.UpdateDeckUI();
        if (playerDeck.Count > 0 || enemyDeck.Count > 0)
            await Task.Delay(400);
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }
    }

    /// <summary>
    /// この端末のデッキから、指定の種類とIDのカードをすべて墓地へ送る。
    /// 相手の効果で頼まれたときに使う。動きと一覧は相手へ同期する。
    /// </summary>
    public static async Task SendOwnDeckCardsToGrave(CardKind kind, int id)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return;

        var matches = battle.pBattleDeck
            .Where(d => d != null && d.カード種類 == kind && d.カードID == id)
            .ToList();
        await SendOwnDeckCardsToGrave(matches);
    }

    /// <summary>
    /// この端末のデッキから、渡したカードを墓地へ送る。相手の効果で頼まれたときに使う。動きと一覧は相手へ同期する。
    /// </summary>
    public static async Task SendOwnDeckCardsToGrave(List<DeckCard> cards)
    {
        var battle = BattleManager.Instance;
        if (battle == null || cards == null || cards.Count == 0)
            return;

        await new ToGraveSkill().MoveDeckCardsToGrave(
            battle.pBattleDeck,
            cards,
            battle.pDeckZone,
            battle.pGraveZone,
            syncToOpponent: true);
        battle.UpdateDeckUI();

        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncDeckCountToOpponent();
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
            if (tempCard != null && tempCard.トークン)
            {
                TokenExit.VanishLocal(tempCard);
                continue;
            }
            CaptureHpOutput(tempCard);

            // デッキが0枚になった瞬間の表示更新は、残っている子を消す。
            // 動いているカードをデッキに置いたままだと、複数枚の最後の1枚がここで消える。
            if (BattleManager.Instance.mainCanvas != null)
                tempCard.transform.SetParent(BattleManager.Instance.mainCanvas.transform, true);

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

        // 相手手札は、この端末では表示用のカード。実カードは相手端末の手札にある。
        bool cardIsReceiversHand = sourceZoneType == CardZoneType.相手手札;
        bool syncOnline = OnlineSyncManager.Instance != null && BattleManager.Instance.IsOnlineMode;
        var discardedMirror = new List<(CardKind kind, int id)>();

        // オフラインのCPU手札は enemyHand が実体。表示だけ墓地へ動かすと、次の手札更新で戻る。
        bool offlineCpuHand = BattleManager.Instance != null
            && !BattleManager.Instance.IsOnlineMode
            && sourceZone == BattleManager.Instance.eHandZone;

        // 相手手札の一覧が途中で届いても、まだ送っていない表示カードを作り直さない
        bool holdEnemyHand = cardIsReceiversHand && syncOnline;
        if (holdEnemyHand)
            BattleManager.Instance.BeginEnemyHandHold();

        // 相手のカードは、持ち主が墓地一覧を送ってくる。こちらで動かし終えるまで、届いた一覧を当てない。
        bool holdEnemyGrave = syncOnline
            && graveZone == BattleManager.Instance.eGraveZone
            && toSend.Exists(card => !card.isMyCard);
        if (holdEnemyGrave)
            BattleManager.Instance.BeginEnemyGraveArrival();

        try
        {
            bool batchedBoard = false;
            if (OnlineSyncManager.Instance != null && IsBoardZone(sourceZoneType))
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

            // 3. カードを墓地に移動
            foreach (var card in toSend)
            {
                if (offlineCpuHand)
                    RemoveOneEnemyHandCard(card);
                OnlineSyncManager.Instance?.NoteCardLeavingOpponentPile(card);
                CaptureHpOutput(card);
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

                if (BattleManager.Instance != null)
                    await BattleManager.Instance.RetireEnhancementsIfSpent(card);

                if (cardIsReceiversHand && syncOnline)
                {
                    discardedMirror.Add((discardedKind, discardedId));
                    continue;
                }

                card.UpdateDisplay();
                card.battleCardview?.UpdateCanUseMovePanel();
            }
        }
        finally
        {
            if (holdEnemyGrave)
                BattleManager.Instance.EndEnemyGraveArrival();
            if (holdEnemyHand)
                BattleManager.Instance.EndEnemyHandHold();
        }

        List<CardController> processed;
        if (cardIsReceiversHand && syncOnline)
        {
            BattleManager.Instance.UpdateEnemyHandUI();
            BattleManager.Instance.UpdateEnemyGraveyardUI();
            processed = ResolveArrivedCards(BattleManager.Instance.eGraveZone, discardedMirror);
            context?.SetProcessedCards(処理カード番号,processed);
            ScheduleOpponentHandGraveTriggers(discardedMirror, processed);
        }
        else
        {
            processed = toSend;
            context?.SetProcessedCards(処理カード番号,processed);
        }

        // アニメーション完了後に同期（オンライン時）
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }

        return processed ?? new List<CardController>();
    }

    // 相手のデッキは相手端末にある。相手に送らせ、相手墓地に着いた表示カードを処理済みとして返す。
    async Task<List<CardController>> SendOpponentDeckCardsToGrave(List<DeckCard> cards)
    {
        var sent = new List<(CardKind kind, int id)>();
        foreach (var card in cards)
        {
            if (card != null)
                sent.Add((card.カード種類, card.カードID));
        }

        await OnlineSyncManager.Instance.RequestOpponentDeckCardsToOwnPile(cards, toDimension: false);

        var arrived = ResolveArrivedCards(BattleManager.Instance.eGraveZone, sent);
        arrived.RemoveAll(card => card == null);
        foreach (var card in arrived)
            CaptureHpOutput(card);
        return arrived;
    }

    // ゾーンの末尾から、送った順にカードを取り出す。見つからない位置は null。
    internal static List<CardController> ResolveArrivedCards(Transform zone, List<(CardKind kind, int id)> discarded)
    {
        var graveCards = zone != null
            ? zone.GetComponentsInChildren<CardController>()
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
            OnlineSyncManager.Instance?.SyncSentToGrave(
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
        card.ResetMoveChanges();
        card.ResetMoveElements();
        card.RecalculateModifiedPowers();
    }

    void CaptureHpOutput(CardController card)
    {
        if (出力キー == SkillArg.なし || card == null)
            return;

        pendingHpOutput += HpToOutput(card);
    }

    int HpToOutput(CardController card)
    {
        if (出力する値 == GraveHpValue.最大HP)
        {
            if (card.model is BattlerModel battler)
                return Mathf.Max(0, battler.maxHp);
            if (card.model is FieldModel field)
                return Mathf.Max(0, field.maxHp);
            return 0;
        }

        return Mathf.Max(0, card.curHp);
    }

    void WriteHpOutput(SkillContext context)
    {
        if (出力キー == SkillArg.なし || context == null)
            return;

        context.SetArg(出力キー, pendingHpOutput);
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

    public enum GraveHpValue
    {
        現在HP,
        最大HP,
    }


    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is ToGraveSkill grave)
        {
            grave.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}
