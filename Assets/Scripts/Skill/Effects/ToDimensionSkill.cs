using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("異次元へ")]
public class ToDimensionSkill : SkillDB
{
    [SerializeField] public SearchCriteria 参照条件;
    [SerializeField] public int 処理カード番号 = 0;
    [SerializeField] public List<CardZoneType> 参照元 = new List<CardZoneType>();
    [SerializeField] public int 選択数 = 1;
    [SerializeField] public CountMode 選択数の扱 = CountMode.ちょうど;
    [SerializeField] public PlayerType 選択プレイヤー = PlayerType.プレイヤー;
    [SerializeField] public bool ランダム = false;
    [SerializeField] public bool 全選択 = false;
    [SerializeField] public SkillArg 出力キー = SkillArg.なし;
    [SerializeField] public DimensionHpValue 出力する値 = DimensionHpValue.現在HP;

    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    int pendingHpOutput;

    public override async Task ActivateEffect(SkillContext context)
    {
        pendingHpOutput = 0;
        try
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
        finally
        {
            WriteHpOutput(context);
        }
    }

    public async Task SendCardToDimension(
    SkillContext context,
    SearchCriteria criteria,
    List<CardZoneType> zones,
    int count,
    PlayerType selector)
    {
        var selections = await SkillSelectionHelper.CollectAndSelectAsync(
            context,
            criteria,
            zones,
            count,
            selector,
            選択数の扱,
            ランダム,
            全選択,
            c => ConditionsMet(対象状態, c));

        await SendPassedSelection(context, selections);
    }

    async Task SendPassedSelection(SkillContext context, List<SelectionItem> selections)
    {
        var processed = new List<CardController>();
        var battle = BattleManager.Instance;
        if (battle == null || selections == null)
        {
            context?.SetProcessedCards(処理カード番号, processed);
            return;
        }

        var playerDeck = new List<DeckCard>();
        var enemyDeck = new List<DeckCard>();
        var existingByZone = new Dictionary<Transform, List<CardController>>();

        if (selections == null)
            return;

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
        {
            if (battle.IsOnlineMode && OnlineSyncManager.Instance != null)
            {
                var arrived = await SendOpponentDeckCards(enemyDeck);
                foreach (var card in arrived)
                    CaptureHpOutput(card);
                processed.AddRange(arrived);
            }
            else
                processed.AddRange(await MoveDeckCards(battle.eBattleDeck, enemyDeck, battle.eDeckZone, battle.eDimensionZone, false));
        }

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

        context?.SetProcessedCards(処理カード番号, processed);
        battle.UpdateDeckUI();
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }
    }

    /// <summary>
    /// この端末のデッキから、渡したカードを異次元へ送る。相手の効果で頼まれたときに使う。動きと一覧は相手へ同期する。
    /// </summary>
    public static async Task SendOwnDeckCardsToDimension(List<DeckCard> cards)
    {
        var battle = BattleManager.Instance;
        if (battle == null || cards == null || cards.Count == 0)
            return;

        await new ToDimensionSkill().MoveDeckCards(battle.pBattleDeck, cards, battle.pDeckZone, battle.pDimensionZone, true);
        battle.UpdateDeckUI();

        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncDeckCountToOpponent();
            OnlineSyncManager.Instance.SendDimensionInfoToOpponent();
        }
    }

    // 相手のデッキは相手端末にある。相手に送らせ、相手異次元に着いた表示カードを処理済みとして返す。
    static async Task<List<CardController>> SendOpponentDeckCards(List<DeckCard> cards)
    {
        var sent = new List<(CardKind kind, int id)>();
        foreach (var card in cards)
        {
            if (card != null)
                sent.Add((card.カード種類, card.カードID));
        }

        await OnlineSyncManager.Instance.RequestOpponentDeckCardsToOwnPile(cards, toDimension: true);

        var arrived = ToGraveSkill.ResolveArrivedCards(BattleManager.Instance.eDimensionZone, sent);
        arrived.RemoveAll(card => card == null);
        return arrived;
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
            CaptureHpOutput(tempCard);

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

        // 相手手札の一覧が途中で届いても、まだ送っていない表示カードを作り直さない
        bool holdEnemyHand = cardIsReceiversHand && syncOnline;
        if (holdEnemyHand)
            BattleManager.Instance.BeginEnemyHandHold();

        try
        {
            foreach (var card in toSend)
            {
                if (offlineCpuHand)
                    RemoveOneEnemyHandCard(card);
                OnlineSyncManager.Instance?.NoteCardLeavingOpponentPile(card);
                CaptureHpOutput(card);
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

                if (BattleManager.Instance != null)
                    await BattleManager.Instance.RetireEnhancementsIfSpent(card);

                card.UpdateDisplay();
                if (dimensionZone == BattleManager.Instance.eDimensionZone)
                    OnlineSyncManager.Instance?.RememberOpponentDimensionCard(card);
            }
        }
        finally
        {
            if (holdEnemyHand)
                BattleManager.Instance.EndEnemyHandHold();
        }

        if (cardIsReceiversHand && syncOnline)
            BattleManager.Instance.UpdateEnemyHandUI();

        context?.SetProcessedCards(処理カード番号,toSend);
        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
            OnlineSyncManager.Instance.SendGraveInfoToOpponent();
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
        card.ResetMoveChanges();
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

    void CaptureHpOutput(CardController card)
    {
        if (出力キー == SkillArg.なし || card == null)
            return;

        pendingHpOutput += HpToOutput(card);
    }

    int HpToOutput(CardController card)
    {
        if (出力する値 == DimensionHpValue.最大HP)
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

    public enum DimensionHpValue
    {
        現在HP,
        最大HP,
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is ToDimensionSkill dimension)
        {
            dimension.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}
