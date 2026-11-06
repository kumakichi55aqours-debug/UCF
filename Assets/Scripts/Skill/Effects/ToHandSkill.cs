using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public enum SelectCountMode
{
    ちょうど,
    // 1枚以上、選択数以下で選べる。
    まで,
}

[System.Serializable]
[SkillDisplayName("手札へ")]
public class ToHandSkill : SkillDB
{
    [SerializeField] public SearchCriteria 参照条件; // 検索条件
    [SerializeField] public List<CardZoneType> 参照場所 = new List<CardZoneType>(); // 追加元のゾーン複数
    [SerializeField] public int 選択数 = 1; // 選択数
    [SerializeField] public SelectCountMode 選択数の扱い = SelectCountMode.ちょうど;
    [SerializeField] public PlayerType 選択プレイヤー = PlayerType.プレイヤー; // どちらが選択するか
    [SerializeField] public bool ランダム = false;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context != null && context.TryConsumeSelection(out var selected))
        {
            await MovePassedSelectionToHand(selected, context);
            await Task.Delay(300);
            return;
        }

        await SendCardToHand(参照条件, 参照場所, 選択数, 選択プレイヤー, context);
        
        await Task.Delay(300);
    }

    // --- 複数ゾーン対応のメイン実装 ---
    public async Task SendCardToHand(SearchCriteria criteria, List<CardZoneType> zones, int count, PlayerType selectBy, SkillContext context = null)
    {
        var addedCards = new List<CardController>();
        try
        {
            if (zones == null || zones.Count == 0)
                return;

            var existingCandidates = new List<CardController>();
            var deckCandidates = new List<DeckCard>();
            var cond = criteria;
            bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;

            foreach (var zone in zones)
            {
                if (CardZones.IsDeck(zone))
                {
                    var deck = CardZones.Deck(zone, ownerIsLocal);
                    if (deck != null)
                        deckCandidates.AddRange(deck.Where(d => SearchCriteria.AllowsDeck(cond, d, context)));
                    continue;
                }

                var zoneTransform = CardZones.Of(zone, ownerIsLocal);
                if (zoneTransform == null)
                    continue;

                existingCandidates.AddRange(
                    zoneTransform.GetComponentsInChildren<CardController>()
                        .Where(c => context != null ? c.MeetsCriteria(cond, context) : c.MeetsCriteria(cond))
                );
            }

            if (existingCandidates.Count == 0 && deckCandidates.Count == 0)
                return;

            List<SelectionItem> selections;
            int totalCandidates = existingCandidates.Count + deckCandidates.Count;
            bool upTo = 選択数の扱い == SelectCountMode.まで;
            bool chooseCount = upTo && totalCandidates > 1;

            if (ランダム)
            {
                selections = SelectionItem.FromCandidates(existingCandidates, deckCandidates)
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(Mathf.Min(count, totalCandidates))
                    .ToList();
            }
            else if (totalCandidates <= count && !chooseCount)
            {
                selections = SelectionItem.FromCandidates(existingCandidates, deckCandidates);
            }
            else if (ChooserIsLocal(selectBy, ownerIsLocal))
            {
                selections = await CardSelectionUI.Instance.ShowAsync(
                    existingCandidates,
                    deckCandidates,
                    count,
                    rejectSameKind: false,
                    letCpuChoose: false,
                    allowFewer: upTo);
                if (selections == null || selections.Count == 0)
                    return;
            }
            else
            {
                var pool = SelectionItem.FromCandidates(existingCandidates, deckCandidates);
                selections = await ChooseRemote(pool, count, context, ownerIsLocal, upTo);
                if (selections == null || selections.Count == 0)
                    return;
            }

            await MoveSelectionsToHand(selections, addedCards);
        }
        finally
        {
            context?.SetProcessedCards(addedCards);
        }
    }

    async Task MovePassedSelectionToHand(List<SelectionItem> selections, SkillContext context)
    {
        var addedCards = new List<CardController>();
        try
        {
            if (selections != null && selections.Count > 0)
                await MoveSelectionsToHand(selections, addedCards);
        }
        finally
        {
            context?.SetProcessedCards(addedCards);
        }
    }

    async Task MoveSelectionsToHand(List<SelectionItem> selections, List<CardController> addedCards)
    {
        if (selections == null)
            return;

        bool addedToPlayer = false;
        bool addedToEnemy = false;
        bool addedRemoteEnemy = false;

        foreach (var sel in selections)
        {
            if (sel == null)
                continue;

            if (sel.source == SelectionItem.SourceType.Existing)
            {
                var card = sel.existingCard;
                if (card == null)
                    continue;

                if (card.isPlayerCard)
                {
                    var added = await MoveOwnExistingCardToHand(card);
                    if (added == null)
                        continue;
                    addedCards.Add(added);
                    addedToPlayer = true;
                }
                else if (BattleManager.Instance.IsOnlineMode && OnlineSyncManager.Instance != null
                    && CardAddress.TryFrom(card, out CardAddress address))
                {
                    await OnlineSyncManager.Instance.RequestOpponentCardToOwnHand(
                        address.Kind.ToString(),
                        address.Id,
                        address.ZoneName,
                        address.Index,
                        -1);
                    addedToEnemy = true;
                    addedRemoteEnemy = true;
                }
                else if (BattleManager.Instance == null || !BattleManager.Instance.IsOnlineMode)
                {
                    await MoveMirrorCardToEnemyHand(card);
                    addedToEnemy = true;
                }
                else
                {
                    Debug.LogWarning($"[手札へ] 相手カードの住所が取れないため、手札へ移せません: {card.cardName}");
                }
            }
            else
            {
                var deckData = sel.deckData;
                if (deckData == null)
                    continue;

                var battle = BattleManager.Instance;
                if (battle.pBattleDeck.Contains(deckData))
                {
                    var added = await AddOwnDeckCardToHand(deckData, -1);
                    if (added == null)
                        continue;
                    addedCards.Add(added);
                    addedToPlayer = true;
                }
                else if (battle.eBattleDeck.Contains(deckData))
                {
                    if (battle.IsOnlineMode && OnlineSyncManager.Instance != null)
                    {
                        int deckIndex = battle.eBattleDeck.IndexOf(deckData);
                        await OnlineSyncManager.Instance.RequestOpponentCardToOwnHand(
                            deckData.カード種類.ToString(),
                            deckData.カードID,
                            string.Empty,
                            -1,
                            deckIndex);
                        addedToEnemy = true;
                        addedRemoteEnemy = true;
                    }
                    else if (battle.eBattleDeck.Remove(deckData))
                    {
                        await AddCpuDeckCardToHand(deckData);
                        addedToEnemy = true;
                    }
                }
            }
        }

        if (addedRemoteEnemy && OnlineSyncManager.Instance != null)
            await OnlineSyncManager.Instance.RequestOpponentShuffleAddedHand();

        bool shuffleEnemyLocally = addedToEnemy && !addedRemoteEnemy;
        if (addedToPlayer || shuffleEnemyLocally)
            BattleManager.Instance.ShuffleHandAfterAddition(addedToPlayer, shuffleEnemyLocally);
    }

    /// <summary>
    /// この端末のカードを、この端末の手札へ戻す。
    /// mirrorZone は、相手側の表示を動かすときの送信者から見たゾーン。通常の自分のカードでは空。
    /// </summary>
    public static async Task<CardController> MoveOwnExistingCardToHand(CardController card, string mirrorZone = null, int mirrorIndex = -1)
    {
        if (card == null || BattleManager.Instance == null)
            return null;

        if (card.トークン)
        {
            TokenExit.VanishSynced(card);
            return null;
        }

        // 相手の画面で同じ種類・IDの別のカードを動かさないよう、受信者から見た住所を付ける
        if (string.IsNullOrEmpty(mirrorZone) && CardAddress.TryFrom(card, out CardAddress ownAddress))
        {
            mirrorZone = BattleManager.CovertOpponentZoneName(ownAddress.ZoneName);
            mirrorIndex = ownAddress.Index;
        }

        var to = (RectTransform)BattleManager.Instance.pHandZone;
        var from = (RectTransform)card.transform.parent;
        bool fromOwnDimension = from != null && from == BattleManager.Instance.pDimensionZone;
        OnlineSyncManager.Instance?.NoteCardLeavingOpponentPile(card);
        SummonSource source = GetSummonSourceFromZone(from);
        CardZoneType fromLocation = CardController.ZoneTypeFromTransform(from);
        bool returnedIsMyCard = card.isMyCard;
        ResetCardStatusForReturnToHand(card);

        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncAddToHandAnimation(
                card.cardKind.ToString(),
                card.cardID,
                (int)source,
                true,
                mirrorIndex,
                mirrorZone);
        }

        var view = card.GetComponent<BattleCardView>();
        if (view != null)
            view.ShowCardBack();

        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.AnimateReturnToHand(card, from, to);

        await SyncSearchPresentation(card, syncDeckCount: false);
        if (fromOwnDimension)
            OnlineSyncManager.Instance?.SendDimensionInfoToOpponent();
        SkillChainManager.Instance?.ScheduleCardReturnedToHand(fromLocation, returnedIsMyCard, notifyOpponent: true);
        await BattleManager.Instance.RetireEnhancementsIfSpent(card);
        return card;
    }

    /// <summary>
    /// 相手デッキの指定位置を、相手の手札へ加える。位置が一致しなければ同じ種類の先頭を使う。
    /// </summary>
    public static async Task AddOpponentDeckCardToOwnHand(string cardKindStr, int cardId, int deckIndex)
    {
        var battle = BattleManager.Instance;
        if (battle == null || !Enum.TryParse(cardKindStr, out CardKind kind))
            return;

        var deck = battle.pBattleDeck;
        if (deck == null)
            return;

        DeckCard deckData = null;
        int echoIndex = -1;
        if (deckIndex >= 0 && deckIndex < deck.Count)
        {
            var at = deck[deckIndex];
            if (at != null && at.カード種類 == kind && at.カードID == cardId)
            {
                deckData = at;
                echoIndex = deckIndex;
            }
        }

        if (deckData == null)
            deckData = deck.FirstOrDefault(d => d != null && d.カード種類 == kind && d.カードID == cardId);
        if (deckData == null)
            return;

        await AddOwnDeckCardToHand(deckData, echoIndex);
    }

    /// <summary>
    /// この端末のデッキから、この端末の手札へ加える。
    /// mirrorDeckIndex は相手側のデッキ表示から同じ位置を抜くときだけ使う。
    /// </summary>
    public static async Task<CardController> AddOwnDeckCardToHand(DeckCard deckData, int mirrorDeckIndex)
    {
        var battle = BattleManager.Instance;
        if (battle == null || deckData == null || !battle.pBattleDeck.Remove(deckData))
            return null;

        var tempCard = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.mainCanvas.transform);
        tempCard.Init(deckData.カード種類, deckData.カードID);
        var tempView = tempCard.GetComponent<BattleCardView>();
        if (tempView != null)
            tempView.ShowCardBack();

        if (OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncAddToHandAnimation(
                deckData.カード種類.ToString(),
                deckData.カードID,
                (int)SummonSource.墓地,
                true,
                mirrorDeckIndex,
                null);
        }

        if (CardAnimationManager.Instance != null)
        {
            await CardAnimationManager.Instance.AnimateDraw(
                battle.pDeckZone as RectTransform,
                battle.pHandZone as RectTransform,
                tempCard);
        }

        tempCard.transform.SetParent(battle.pHandZone, false);
        battle.UpdateDeckUI();
        await SyncSearchPresentation(tempCard, syncDeckCount: true);
        return tempCard;
    }

    /// <summary>
    /// CPUのデッキからCPUの手札へ加える。
    /// デッキから手札へ飛ばし、表を一度見せてから、手札の裏札として並べる。
    /// </summary>
    static async Task AddCpuDeckCardToHand(DeckCard deckData)
    {
        var battle = BattleManager.Instance;
        if (battle == null || deckData == null)
            return;

        deckData.PopulateDerivedData();
        // リストからは先に抜く。表示上の枚数は、手札へ着いてから減らす。
        battle.ReserveDeckDepartures(false, 1);

        CardController card = null;
        bool deckCountReleased = false;
        try
        {
            card = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.mainCanvas.transform);
            var view = card.GetComponent<BattleCardView>();
            if (view != null)
                view.ShowCardBack();

            var animation = CardAnimationManager.Instance;
            if (animation != null)
            {
                await animation.AnimateDraw(
                    battle.eDeckZone as RectTransform,
                    battle.eHandZone as RectTransform,
                    card);

                if (battle.eHandZone != null)
                {
                    card.transform.SetParent(battle.eHandZone, false);
                    card.transform.localPosition = Vector3.zero;
                }
            }

            battle.CompleteDeckDeparture(false);
            deckCountReleased = true;

            if (animation != null)
            {
                if (view != null)
                    view.Show(card.model);
                await animation.AnimateSearchedCard(card, 1.2f, 1.1f, 0.2f);
                if (view != null)
                    view.ShowCardBack();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[手札へ] CPUの手札追加アニメでエラー: {ex.Message}");
        }
        finally
        {
            if (!deckCountReleased)
                battle.CompleteDeckDeparture(false);

            if (card != null)
            {
                if (battle.mainCanvas != null)
                    card.transform.SetParent(battle.mainCanvas.transform, false);
                card.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(card.gameObject);
            }

            battle.enemyHand.Add(new DeckCard(
                deckData.カード種類,
                deckData.カードID,
                deckData.コスト,
                deckData.カテゴリ,
                deckData.カードタイプ));
            if (!battle.IsOnlineMode)
                battle.UpdateEnemyHandUI();
        }
    }

    public static async Task MoveMirrorCardToEnemyHand(CardController card)
    {
        var battle = BattleManager.Instance;
        if (card == null || battle == null)
            return;

        if (card.トークン)
        {
            TokenExit.VanishSynced(card);
            return;
        }

        var to = (RectTransform)battle.eHandZone;
        var from = (RectTransform)card.transform.parent;
        OnlineSyncManager.Instance?.NoteCardLeavingOpponentPile(card);
        CardZoneType fromLocation = CardController.ZoneTypeFromTransform(from);
        bool returnedIsMyCard = card.isMyCard;
        ResetCardStatusForReturnToHand(card);

        CardKind kind = card.cardKind;
        int id = card.cardID;
        int cost = card.cost;
        List<CardCategoryList> categories = card.category;
        CardTypeList cardType = card.type;

        var enemyView = card.GetComponent<BattleCardView>();
        if (enemyView != null)
            enemyView.ShowCardBack();

        if (CardAnimationManager.Instance != null)
        {
            await CardAnimationManager.Instance.AnimateReturnToHand(card, from, to);
            if (enemyView != null)
                enemyView.Show(card.model);
            await CardAnimationManager.Instance.AnimateSearchedCard(card, 1.2f, 1.1f, 0.2f);
        }

        SkillChainManager.Instance?.ScheduleCardReturnedToHand(fromLocation, returnedIsMyCard, notifyOpponent: true);
        await battle.RetireEnhancementsIfSpent(card);
        battle.enemyHand.Add(new DeckCard(kind, id, cost, categories, cardType));
        UnityEngine.Object.Destroy(card.gameObject);
    }

    /// <summary>
    /// 移動のあと、相手の移動完了を待ってから互いの強調を重ねる。
    /// オンライン時は移動RPCを先に送ってある前提。
    /// </summary>
    static async Task SyncSearchPresentation(CardController card, bool syncDeckCount)
    {
        var sync = OnlineSyncManager.Instance;
        bool online = sync != null && sync.IsOnlineMode;

        if (online)
            await sync.WaitForOpponentAnimation();

        if (syncDeckCount && sync != null)
            sync.SyncDeckCountToOpponent();

        if (online)
            sync.SyncContinueAddToHandEmphasis();

        try
        {
            if (card != null)
            {
                card.UpdateDisplay();
                if (CardAnimationManager.Instance != null)
                    await CardAnimationManager.Instance.AnimateSearchedCard(card, 1.2f, 1.1f, 0.2f);
            }
        }
        finally
        {
            if (online)
                await sync.WaitForOpponentAnimation();
        }
    }

    /// <summary>
    /// ゾーンからSummonSourceを判定。墓地とデッキの対応は召喚元の名前と逆。
    /// </summary>
    static SummonSource GetSummonSourceFromZone(Transform zone)
    {
        switch (CardController.ZoneTypeFromTransform(zone))
        {
            case CardZoneType.プレイヤー墓地:
            case CardZoneType.相手墓地:
                return SummonSource.デッキ;
            case CardZoneType.プレイヤーデッキ:
            case CardZoneType.相手デッキ:
                return SummonSource.墓地;
            case CardZoneType.プレイヤー手札:
            case CardZoneType.相手手札:
                return SummonSource.手札;
            case CardZoneType.プレイヤーBゾーン:
            case CardZoneType.相手Bゾーン:
            case CardZoneType.プレイヤーTゾーン:
            case CardZoneType.相手Tゾーン:
            case CardZoneType.プレイヤーFゾーン:
            case CardZoneType.相手Fゾーン:
                return SummonSource.場;
            case CardZoneType.プレイヤー異次元:
            case CardZoneType.相手異次元:
                return SummonSource.異次元;
            default:
                return SummonSource.デッキ;
        }
    }

    /// <summary>
    /// カードを手札に戻す際、バトルゾーンで付与された効果をリセット
    /// </summary>
    static void ResetCardStatusForReturnToHand(CardController card)
    {
        if (card == null) return;

        if (card.cardKind == CardKind.バトラー) 
        {
            BattlerDB cardDB = BattlerAssets.Load(card.cardID, out _);
            if (cardDB != null)
                card.curHp = cardDB.hp;
        }
        // PassiveEffectをクリア
        card.RemoveAllPassiveEffects();
        card.ClearStatusAilments();
        card.ResetMoveElements();
        card.RecalculateModifiedPowers();

        // hasUsedMoveをリセット
        card.hasUsedMove = true;
        card.GetComponent<BattleCardView>().UpdateCanUseMovePanel();

        // ターン制限効果をクリア
        card.RemoveAllTurnRestrictions();

        // 属性無効化効果をクリア
        card.ClearInvalidElements();

        // ダメージボーナスと命中率をリセット
        card.damageBonus = 0;
        card.hitRate = 0;

        // HP保護フラグをリセット
        card.hpProtection1 = false;
        card.hpProtectionTargetElement1 = ElementList.None;
        card.hpProtection2 = false;
        card.hpProtectionTargetElement2 = ElementList.None;
    }

    static bool ChooserIsLocal(PlayerType selectBy, bool ownerIsLocal)
    {
        bool chooserIsOwner = selectBy != PlayerType.相手;
        return chooserIsOwner ? ownerIsLocal : !ownerIsLocal;
    }

    static async Task<List<SelectionItem>> ChooseRemote(List<SelectionItem> pool, int count, SkillContext context, bool ownerIsLocal, bool upTo)
    {
        if (pool == null || pool.Count == 0)
            return null;

        int take = Mathf.Min(count, pool.Count);
        bool online = BattleManager.Instance != null
            && BattleManager.Instance.IsOnlineMode
            && ownerIsLocal
            && context?.発動カード != null
            && OnlineSyncManager.Instance != null;

        if (!online)
            return pool.OrderBy(_ => UnityEngine.Random.value).Take(take).ToList();

        var kinds = new int[pool.Count];
        var ids = new int[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null && pool[i].TryGetIdentity(out CardKind kind, out int id))
            {
                kinds[i] = (int)kind;
                ids[i] = id;
            }
        }

        int[] picked = await OnlineSyncManager.Instance.RequestOpponentCardPick(context.発動カード, kinds, ids, take, upTo);
        return TakeByIndices(pool, picked, take);
    }

    static List<SelectionItem> TakeByIndices(List<SelectionItem> pool, int[] indices, int count)
    {
        var picked = new List<SelectionItem>();
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
}


