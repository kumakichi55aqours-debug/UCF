using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class BattleManager
{
    // まとめて抜いたあと、移動アニメが終わるまで枚数表示に残す枚数。
    int pDeckDepartureReserve;
    int eDeckDepartureReserve;

    ref int DeckDepartureReserve(bool playerSide)
    {
        if (playerSide)
            return ref pDeckDepartureReserve;
        return ref eDeckDepartureReserve;
    }

    private void LoadDeck()
    {
        if (!PhotonNetwork.OfflineMode)
            BattleLaunchContext.ClearBattleSettings();

        pDeckDepartureReserve = 0;
        if (!BattleLaunchContext.TryCopyPlayerDeck(out pBattleDeck))
            pBattleDeck = ReadDeckFile(deckFilePath);
        if (!BattleLaunchContext.DisableDeckShuffle)
            ShuffleDeck();
    }

    private void InitializeEnemyDeck()
    {
        eDeckDepartureReserve = 0;
        if (IsOnlineMode)
        {
            eBattleDeck = new List<DeckCard>();
            eDeckCount = pBattleDeck.Count;
            UpdateDeckUI();
        }
        else
        {
            if (!BattleLaunchContext.TryCopyEnemyDeck(out eBattleDeck))
                eBattleDeck = ReadDeckFile(enemyDeckFilePath);
            eDeckCount = eBattleDeck.Count;
            if (!BattleLaunchContext.DisableDeckShuffle)
                ShuffleEnemyDeck();
        }
    }

    private List<DeckCard> ReadDeckFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new List<DeckCard>();

        string json = File.ReadAllText(path);
        List<DeckCard> deck = JsonConvert.DeserializeObject<List<DeckCard>>(json);
        if (deck == null)
            deck = new List<DeckCard>();

        foreach (var deckCard in deck)
        {
            deckCard?.PopulateDerivedData();
        }

        return deck;
    }

    void Shuffle(List<DeckCard> deck)
    {
        var rng = new System.Random();
        for (int n = deck.Count; n > 1; n--)
        {
            int k = rng.Next(n);
            (deck[k], deck[n - 1]) = (deck[n - 1], deck[k]);
        }
    }

    public void ShuffleDeck() => Shuffle(pBattleDeck);

    public void ShuffleEnemyDeck() => Shuffle(eBattleDeck);

    /// <summary>
    /// 効果で手札に加えたあとに呼ぶ。通常のドローでは呼ばない。
    /// 加わった側の手札をシャッフルする。自分の手札を動かしたときは、オンラインならその並びを相手へ送る。
    /// </summary>
    public void ShuffleHandAfterAddition(bool shufflePlayerHand, bool shuffleEnemyHand)
    {
        if (shufflePlayerHand)
            ShuffleHandZone(pHandZone);

        if (shuffleEnemyHand)
        {
            Shuffle(enemyHand);
            UpdateEnemyHandUI();
        }

        if (shufflePlayerHand && OnlineSyncManager.Instance != null && IsOnlineMode)
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
    }

    void ShuffleHandZone(Transform zone)
    {
        if (zone == null || zone.childCount <= 1)
            return;

        var children = new List<Transform>(zone.childCount);
        for (int i = 0; i < zone.childCount; i++)
            children.Add(zone.GetChild(i));

        for (int n = children.Count; n > 1; n--)
        {
            int k = Random.Range(0, n);
            (children[k], children[n - 1]) = (children[n - 1], children[k]);
        }

        for (int i = 0; i < children.Count; i++)
            children[i].SetSiblingIndex(i);

        if (zone is RectTransform rect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    private async Task StartGameAsync()
    {
        if (IsOnlineMode)
        {
            await OnlineSyncManager.Instance.SyncDecks();

            await Task.Delay(500);

            OnlineSyncManager.Instance.InitializeDrawSync();
        }

        if (JankenManager.Instance != null)
            await JankenManager.Instance.ResolveFirstPlayerAsync();

        await Task.Yield();

        for (int i = 0; i < 4; i++)
            await Task.WhenAll(DrawCardAsync(pHandZone), EnemyDrawCardAsync());

        if (IsOnlineMode)
        {
            await SyncInitialDrawCompletion();
            await OnlineSyncManager.Instance.SyncInitialHands();
        }

        await ResetAllUsedCounts();

        PhaseManager.Instance.StartTurn();
    }

    private async Task SyncInitialDrawCompletion()
    {
        if (IsHostPlayer)
        {
            await OnlineSyncManager.Instance.WaitForClientInitialDrawCompletion();
        }
        else
        {
            await Task.Yield();
            UpdateDeckUI();

            OnlineSyncManager.Instance.NotifyInitialDrawCompletion();
        }
    }

    public async Task ExecuteDrawPhase()
    {
        if (IsOnlineMode)
        {
            int turnNumber = IsActiveSideTurn ? ++curPTurn : ++curETurn;
            pResources.Cost.ResetCost(turnNumber);
            pResources.Energy.ResetEnergy(turnNumber);
            OnResourcesReset(true);
            OnlineSyncManager.Instance.SyncOpponentResourcesReset(turnNumber);

            if (curTurn > 1)
                await DrawCardAsync(pHandZone);
        }
        else if (IsActiveSideTurn)
        {
            curPTurn++;
            pResources.Cost.ResetCost(curPTurn);
            pResources.Energy.ResetEnergy(curPTurn);
            OnResourcesReset(true);

            if (curTurn > 1)
                await DrawCardAsync(pHandZone);
        }
        else
        {
            curETurn++;
            eResources.Cost.ResetCost(curETurn);
            eResources.Energy.ResetEnergy(curETurn);
            OnResourcesReset(false);

            if (curTurn > 1)
                await EnemyDrawCardAsync();
        }

        await ResetAllUsedCounts();

        await Task.Delay(200);
    }

    async Task ApplyPoisonSlipDamage()
    {
        var battlers = new List<CardController>();
        if (pBattlerZone != null)
            battlers.AddRange(pBattlerZone.GetComponentsInChildren<CardController>());
        if (eBattlerZone != null)
            battlers.AddRange(eBattlerZone.GetComponentsInChildren<CardController>());

        foreach (var card in battlers)
        {
            if (card == null || card.cardKind != CardKind.バトラー)
                continue;

            int damage = card.ApplyPoisonDamage();
            if (damage <= 0)
                continue;

            card.UpdateDisplay();
            await ShowEffect(card.transform.position, damage, new Color(0.35f, 0.75f, 0.25f));
        }
    }

    public async Task ExecuteEndPhase()
    {
        await ApplyPoisonSlipDamage();

        CardController[] zonePBattlers = pBattlerZone.GetComponentsInChildren<CardController>();
        CardController[] zoneEBattlers = eBattlerZone.GetComponentsInChildren<CardController>();
        CardController[] zonePTactics = pTacticsZone.GetComponentsInChildren<CardController>();
        CardController[] zoneETactics = eTacticsZone.GetComponentsInChildren<CardController>();
        CardController[] zonePFields = pFieldZone.GetComponentsInChildren<CardController>();
        CardController[] zoneEFields = eFieldZone.GetComponentsInChildren<CardController>();

        void AddSide(List<CardController> list, bool playerSide)
        {
            list.AddRange(playerSide ? zonePBattlers : zoneEBattlers);
            list.AddRange(playerSide ? zonePTactics : zoneETactics);
            list.AddRange(playerSide ? zonePFields : zoneEFields);
        }

        var allZoneCards = new List<CardController>();
        AddSide(allZoneCards, true);
        AddSide(allZoneCards, false);

        foreach (var card in allZoneCards)
        {
            card.TickPassiveEffects();
            card.TickTurnRestrictions();
            card.TickStatusAilments();
        }

        var currentTurnCards = new List<CardController>();
        if (IsOnlineMode)
        {
            if (IsMyTurn)
                AddSide(currentTurnCards, true);
        }
        else
        {
            AddSide(currentTurnCards, IsActiveSideTurn);
        }

        foreach (var card in currentTurnCards)
        {
            card.hasUsedMove = true;
            card.GetComponent<BattleCardView>().UpdateCanUseMovePanel();
        }

        RefreshCanUseMovePanels();

        RemovePowerBonuses();

        await Task.Yield();

        PersistentEffectManager.Instance?.RecalculateImmediately();

        if (PhaseManager.Instance != null && PhaseManager.Instance.OwnsCurrentPhase)
            await ProcessOwnPhaseTiming(SkillTrigger.自分のフェイズ終了時, TurnPhase.エンドフェイズ);
    }

    /// <summary>
    /// 自分のフェイズに入った、または出たときに、そのフェイズのスキルを確認して完了まで待つ。
    /// </summary>
    public async Task ProcessOwnPhaseTiming(SkillTrigger trigger, TurnPhase phase)
    {
        if (!SkillManager.IsOwnPhaseTrigger(trigger))
            return;

        var context = new SkillContext(null, null, null);
        var collected = SkillManager.CollectSkillsForTrigger(trigger, null, null, context, phase: phase);
        bool hasSkills = collected.TPForcedSkills.Count > 0
            || collected.TPOptionalSkills.Count > 0
            || collected.NonTPForcedSkills.Count > 0
            || collected.NonTPOptionalSkills.Count > 0;
        if (!hasSkills)
            return;

        context.ProcessingCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventBus.Publish(new SkillTriggerEvent(trigger, null, null, context) { Phase = phase });

        await context.ProcessingCompleteTcs.Task;
    }

    public void IncrementTurn()
    {
        curTurn++;
        RemovePowerBonuses();
        PowerBonusUI.Instance?.UpdateUI();
    }

    async Task<CardController> AnimateDeckDraw(DeckCard drawn, Transform deckZone, Transform handZone)
    {
        CardController card = Instantiate(cardPrefab, mainCanvas.transform);
        card.Init(drawn.カード種類, drawn.カードID);
        card.GetComponent<BattleCardView>().ShowCardBack();

        await Task.Yield();

        await CardAnimationManager.Instance.AnimateDraw(
            deckZone.GetComponent<RectTransform>(),
            handZone.GetComponent<RectTransform>(),
            card);
        return card;
    }

    public async Task DrawCardAsync(Transform hand)
    {
        if (pBattleDeck.Count == 0) return;

        DeckCard drawn = pBattleDeck[0];
        pBattleDeck.RemoveAt(0);
        if (IsOnlineMode && OnlineSyncManager.Instance != null)
            OnlineSyncManager.Instance.SyncDeckCountToOpponent();

        CardController card = await AnimateDeckDraw(drawn, pDeckZone, hand);

        card.transform.SetParent(hand, false);
        card.UpdateDisplay();

        if (IsOnlineMode)
            OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
    }

    public async Task EnemyDrawCardAsync()
    {
        if (eBattleDeck.Count == 0) return;

        DeckCard drawn = eBattleDeck[0];
        eBattleDeck.RemoveAt(0);

        CardController card = await AnimateDeckDraw(drawn, eDeckZone, eHandZone);

        Destroy(card.gameObject);
        enemyHand.Add(drawn);

        if (!IsOnlineMode)
            UpdateEnemyHandUI();

        if (IsOnlineMode && curTurn != 1)
            await OnlineSyncManager.Instance.WaitForOpponentHandInfo();
    }

    /// <summary>
    /// デッキからまとめて抜いた枚数を、1枚ずつ動き終わるまで表示に残す。
    /// </summary>
    public void ReserveDeckDepartures(bool playerSide, int count)
    {
        if (count <= 0)
            return;
        DeckDepartureReserve(playerSide) += count;
    }

    /// <summary>
    /// 1枚の移動が終わったので、表示上のデッキ枚数を1つ減らす。
    /// </summary>
    public void CompleteDeckDeparture(bool playerSide)
    {
        CancelDeckDepartures(playerSide, 1);
    }

    /// <summary>
    /// デッキへ戻すなど、動かさずに保留を取り消す。
    /// </summary>
    public void CancelDeckDepartures(bool playerSide, int count)
    {
        if (count <= 0)
            return;
        ref int reserve = ref DeckDepartureReserve(playerSide);
        reserve = Mathf.Max(0, reserve - count);
        UpdateDeckUI();
    }

    public void UpdateDeckUI()
    {
        int playerDeckCount = pBattleDeck.Count + pDeckDepartureReserve;
        int enemyDeckCount = (IsOnlineMode ? eDeckCount : eBattleDeck.Count) + eDeckDepartureReserve;

        SyncDeckZone(pDeckZone, playerDeckCount, pDeckCountText);
        SyncDeckZone(eDeckZone, enemyDeckCount, eDeckCountText);
    }

    void SyncDeckZone(Transform zone, int count, TextMeshProUGUI countText)
    {
        if (count > 0 && zone.childCount == 0)
            Instantiate(cardBackPrefab, zone);

        if (count == 0 && zone.childCount != 0)
        {
            foreach (Transform child in zone)
                Destroy(child.gameObject);
        }

        countText.text = count.ToString();
    }

    public void UpdateEnemyHandUI()
    {
        foreach (Transform child in eHandZone)
            Destroy(child.gameObject);

        List<DeckCard> cardsToShow = IsOnlineMode ? OnlineSyncManager.Instance.GetOpponentHand() : enemyHand;
        foreach (DeckCard deckCard in cardsToShow)
        {
            CardController cc = Instantiate(cardPrefab, eHandZone);
            cc.Init(deckCard.カード種類, deckCard.カードID);
            var view = cc.GetComponent<BattleCardView>();
            if (view != null)
                view.ShowCardBack();
        }

        PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    public void UpdateEnemyGraveyardUI()
    {
        foreach (Transform child in eGraveZone)
            Destroy(child.gameObject);

        foreach (DeckCard deckCard in OnlineSyncManager.Instance.GetOpponentGrave())
        {
            CardController cc = Instantiate(cardPrefab, eGraveZone);
            cc.Init(deckCard.カード種類, deckCard.カードID);
        }
    }
}
