using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("手札へ")]
public class ToHandSkill : SkillDB
{
    [SerializeField] public SearchCriteria 参照条件; // 検索条件
    [SerializeField] public List<CardZoneType> 参照場所 = new List<CardZoneType>(); // 追加元のゾーン複数
    [SerializeField] public int 選択数 = 1; // 選択数
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

            foreach (var zone in zones)
            {
                // 相手デッキの中身は秘匿なので、ここでは対象にしない。
                if (zone == CardZoneType.相手デッキ)
                    continue;

                if (CardZones.IsDeck(zone))
                {
                    deckCandidates.AddRange(CardZones.Deck(zone, true).Where(d => SearchCriteria.AllowsDeck(cond, d, context)));
                    continue;
                }

                var zoneTransform = CardZones.Absolute(zone);
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

            if (ランダム)
            {
                selections = SelectionItem.FromCandidates(existingCandidates, deckCandidates)
                    .OrderBy(_ => Random.value)
                    .Take(Mathf.Min(count, totalCandidates))
                    .ToList();
            }
            else if (totalCandidates <= count)
            {
                selections = SelectionItem.FromCandidates(existingCandidates, deckCandidates);
            }
            else if (selectBy == PlayerType.プレイヤー)
            {
                selections = await CardSelectionUI.Instance.ShowAsync(existingCandidates, deckCandidates, count);
                if (selections == null || selections.Count == 0)
                    return;
            }
            else
                return;

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

        foreach (var sel in selections)
        {
            if (sel == null)
                continue;

            if (sel.source == SelectionItem.SourceType.Existing)
            {
                var card = sel.existingCard;
                if (card == null)
                    continue;

                var to = card.isPlayerCard ? (RectTransform)BattleManager.Instance.pHandZone : (RectTransform)BattleManager.Instance.eHandZone;
                var from = (RectTransform)card.transform.parent;

                SummonSource source = GetSummonSourceFromZone(from);
                ResetCardStatusForReturnToHand(card);

                if (card.isPlayerCard)
                {
                    if (OnlineSyncManager.Instance != null)
                        OnlineSyncManager.Instance.SyncAddToHandAnimation(card.cardKind.ToString(), card.cardID, (int)source);

                    var viewForExisting = card.GetComponent<BattleCardView>();
                    if (viewForExisting != null)
                        viewForExisting.ShowCardBack();

                    await CardAnimationManager.Instance.AnimateReturnToHand(card, from, to);
                    await SyncSearchPresentation(card, syncDeckCount: false);
                    addedCards.Add(card);
                    addedToPlayer = true;
                }
                else
                {
                    CardKind k = card.cardKind;
                    int id = card.cardID;
                    int cost = card.cost;
                    List<CardCategoryList> cats = card.category;
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

                    BattleManager.Instance.enemyHand.Add(new DeckCard(k, id, cost, cats, cardType));
                    Object.Destroy(card.gameObject);
                    addedToEnemy = true;
                }
            }
            else
            {
                var deckData = sel.deckData;
                if (deckData == null)
                    continue;

                var battle = BattleManager.Instance;
                bool fromPlayerDeck = battle.pBattleDeck.Remove(deckData);
                if (!fromPlayerDeck && !battle.eBattleDeck.Remove(deckData))
                    continue;

                if (!fromPlayerDeck)
                {
                    deckData.PopulateDerivedData();
                    battle.enemyHand.Add(new DeckCard(
                        deckData.カード種類,
                        deckData.カードID,
                        deckData.コスト,
                        deckData.カテゴリ,
                        deckData.カードタイプ
                    ));
                    battle.UpdateDeckUI();
                    addedToEnemy = true;
                    continue;
                }

                var tempCard = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.mainCanvas.transform);
                tempCard.Init(deckData.カード種類, deckData.カードID);
                var tempView = tempCard.GetComponent<BattleCardView>();
                if (tempView != null)
                    tempView.ShowCardBack();

                if (OnlineSyncManager.Instance != null)
                    OnlineSyncManager.Instance.SyncAddToHandAnimation(deckData.カード種類.ToString(), deckData.カードID, (int)SummonSource.墓地);

                await CardAnimationManager.Instance.AnimateDraw(
                    battle.pDeckZone as RectTransform,
                    battle.pHandZone as RectTransform,
                    tempCard
                );
                tempCard.transform.SetParent(battle.pHandZone, false);
                battle.UpdateDeckUI();

                await SyncSearchPresentation(tempCard, syncDeckCount: true);
                addedCards.Add(tempCard);
                addedToPlayer = true;
            }
        }

        if (addedToPlayer || addedToEnemy)
            BattleManager.Instance.ShuffleHandAfterAddition(addedToPlayer, addedToEnemy);
    }

    /// <summary>
    /// 移動のあと、相手の移動完了を待ってから互いの強調を重ねる。
    /// オンライン時は移動RPCを先に送ってある前提。
    /// </summary>
    async Task SyncSearchPresentation(CardController card, bool syncDeckCount)
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
    SummonSource GetSummonSourceFromZone(Transform zone)
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
            default:
                return SummonSource.デッキ;
        }
    }

    /// <summary>
    /// カードを手札に戻す際、バトルゾーンで付与された効果をリセット
    /// </summary>
    private void ResetCardStatusForReturnToHand(CardController card)
    {
        if (card == null) return;

        if (card.cardKind == CardKind.バトラー) 
        {
            BattlerDB cardDB = Resources.Load<BattlerDB>("CardDBList/バトラー/" + card.cardID);
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
}


