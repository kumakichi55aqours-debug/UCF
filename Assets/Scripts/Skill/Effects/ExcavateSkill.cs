using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public enum ExcavateRemainder
{
    デッキに戻す,
    墓地へ送る
}

/// <summary>
/// デッキの上から指定枚数をめくり、条件に合うカードを選択数まで手札に加える。
/// 残りのカードはデッキに戻してシャッフルするか、墓地へ送る。
/// </summary>
[System.Serializable]
[SkillDisplayName("めくり")]
public class ExcavateSkill : SkillDB
{
    public int めくり枚数 = 3;
    public SearchCriteria 対象条件;
    public int 選択数 = 1;
    public ExcavateRemainder 残りの処理 = ExcavateRemainder.墓地へ送る;

    public override async Task ActivateEffect(SkillContext context)
    {
        var addedCards = new List<CardController>();
        BattleManager battle = null;
        bool forPlayer = true;
        bool reservedDepartures = false;
        try
        {
        forPlayer = context?.発動カード == null || context.発動カード.isMyCard;
        battle = BattleManager.Instance;
        if (battle == null)
            return;

        if (!forPlayer && battle.IsOnlineMode)
            return;

        var deck = forPlayer ? battle.pBattleDeck : battle.eBattleDeck;
        if (deck == null || deck.Count == 0 || めくり枚数 <= 0)
            return;

        int take = Mathf.Min(めくり枚数, deck.Count);
        var revealed = new List<DeckCard>(take);
        for (int i = 0; i < take; i++)
        {
            revealed.Add(deck[0]);
            deck.RemoveAt(0);
        }
        // 中身は先に抜く。枚数と裏側は、1枚ずつ動き終わってから減らす。
        battle.ReserveDeckDepartures(forPlayer, take);
        reservedDepartures = true;

        var selectable = new List<bool>(revealed.Count);
        int matchCount = 0;
        for (int i = 0; i < revealed.Count; i++)
        {
            bool match = Matches(revealed[i], context);
            selectable.Add(match);
            if (match)
                matchCount++;
        }

        int selectCount = Mathf.Max(0, 選択数);
        var chosen = await ChooseCards(revealed, selectable, matchCount, selectCount, forPlayer);

        var chosenSet = new HashSet<DeckCard>(chosen);
        var remainder = new List<DeckCard>();
        for (int i = 0; i < revealed.Count; i++)
        {
            if (!chosenSet.Contains(revealed[i]))
                remainder.Add(revealed[i]);
        }

        foreach (var card in chosen)
        {
            if (forPlayer)
            {
                var added = await AddToPlayerHand(card);
                if (added != null)
                    addedCards.Add(added);
            }
            else
                await AddToCpuHand(card);
        }

        if (残りの処理 == ExcavateRemainder.墓地へ送る)
        {
            foreach (var card in remainder)
            {
                if (forPlayer)
                    await SendPlayerToGrave(card);
                else
                    await SendCpuToGrave(card);
            }
        }
        else if (remainder.Count > 0)
        {
            deck.AddRange(remainder);
            battle.CancelDeckDepartures(forPlayer, remainder.Count);
            if (forPlayer)
                battle.ShuffleDeck();
            else
                battle.ShuffleEnemyDeck();
        }

        battle.UpdateDeckUI();

        var sync = OnlineSyncManager.Instance;
        if (forPlayer && sync != null)
        {
            sync.SyncDeckCountToOpponent();
            sync.SendDetailedHandInfoToOpponent();
            if (残りの処理 == ExcavateRemainder.墓地へ送る && remainder.Count > 0)
                sync.SendGraveInfoToOpponent();
        }
        }
        finally
        {
            if (reservedDepartures && battle != null)
                battle.CancelDeckDepartures(forPlayer, int.MaxValue);
            context?.SetProcessedCards(addedCards);
        }
    }

    async Task<List<DeckCard>> ChooseCards(
        List<DeckCard> revealed,
        List<bool> selectable,
        int matchCount,
        int selectCount,
        bool forPlayer)
    {
        var matches = new List<DeckCard>();
        for (int i = 0; i < revealed.Count; i++)
        {
            if (selectable[i])
                matches.Add(revealed[i]);
        }

        if (matchCount == 0 || selectCount <= 0)
        {
            if (forPlayer && CardSelectionUI.Instance != null && !SkillContext.IsOfflineCpuResolving)
            {
                await CardSelectionUI.Instance.ShowOrderedDeckAsync(revealed, selectable, 0, false);
            }
            return new List<DeckCard>();
        }

        bool playerChooses = forPlayer
            && CardSelectionUI.Instance != null
            && !SkillContext.IsOfflineCpuResolving;
        if (!playerChooses)
        {
            if (SkillContext.IsOfflineCpuResolving && CardSelectionUI.Instance != null && matchCount > selectCount)
            {
                var cpuPicks = await CardSelectionUI.Instance.ShowOrderedDeckAsync(
                    revealed,
                    selectable,
                    selectCount,
                    true);
                return CardsFromSelections(cpuPicks, matches, selectCount);
            }
            return matches.Take(selectCount).ToList();
        }

        // 候補が選択数以下でも、選ばずに確定はできない。
        var selections = await CardSelectionUI.Instance.ShowOrderedDeckAsync(
            revealed,
            selectable,
            selectCount,
            true);
        return CardsFromSelections(selections, matches, selectCount);
    }

    List<DeckCard> CardsFromSelections(List<SelectionItem> selections, List<DeckCard> matches, int selectCount)
    {
        var chosen = new List<DeckCard>();
        if (selections != null)
        {
            foreach (var selection in selections)
            {
                if (selection?.deckData != null && matches.Contains(selection.deckData))
                    chosen.Add(selection.deckData);
            }
        }

        if (chosen.Count > selectCount)
            chosen.RemoveRange(selectCount, chosen.Count - selectCount);
        return chosen;
    }

    async Task<CardController> AddToPlayerHand(DeckCard deckData)
    {
        var battle = BattleManager.Instance;
        CardController card = null;
        try
        {
            card = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.mainCanvas.transform);
            var view = card.GetComponent<BattleCardView>();
            if (view != null)
                view.ShowCardBack();

            var syncBeforeMove = OnlineSyncManager.Instance;
            if (syncBeforeMove != null)
            {
                syncBeforeMove.SyncAddToHandAnimation(
                    deckData.カード種類.ToString(),
                    deckData.カードID,
                    (int)SummonSource.墓地);
            }

            await CardAnimationManager.Instance.AnimateDraw(
                battle.pDeckZone as RectTransform,
                battle.pHandZone as RectTransform,
                card);
        }
        finally
        {
            battle.CompleteDeckDeparture(true);
        }
        card.transform.SetParent(battle.pHandZone, false);
        // 残りのカードがまだ動く前に、減りきった枚数を相手へ送らない。
        await PresentAddedHandCard(card, syncDeckCount: false);

        var sync = OnlineSyncManager.Instance;
        if (sync != null)
            sync.SendDetailedHandInfoToOpponent();

        return card;
    }

    async Task PresentAddedHandCard(CardController card, bool syncDeckCount)
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

    async Task AddToCpuHand(DeckCard deckData)
    {
        var battle = BattleManager.Instance;
        CardController card = null;
        try
        {
            card = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.mainCanvas.transform);
            var view = card.GetComponent<BattleCardView>();
            if (view != null)
                view.ShowCardBack();

            await CardAnimationManager.Instance.AnimateDraw(
                battle.eDeckZone as RectTransform,
                battle.eHandZone as RectTransform,
                card);
        }
        finally
        {
            battle.CompleteDeckDeparture(false);
        }

        if (card != null)
            Object.Destroy(card.gameObject);

        battle.enemyHand.Add(deckData);
        if (!battle.IsOnlineMode)
            battle.UpdateEnemyHandUI();

        await Task.Delay(200);
    }

    async Task SendPlayerToGrave(DeckCard deckData)
    {
        var battle = BattleManager.Instance;
        var card = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.pDeckZone);
        var sync = OnlineSyncManager.Instance;
        if (sync != null)
            sync.SyncDeckToGraveyard(card.cardKind.ToString(), card.cardID);

        // ドローと同じく、デッキを離れた瞬間に枚数を減らす。
        // 枚数が0になると裏側を消すので、飛んでいるカードはデッキの子から外す。
        if (battle.mainCanvas != null)
            card.transform.SetParent(battle.mainCanvas.transform, true);
        battle.CompleteDeckDeparture(true);

        await CardAnimationManager.Instance.AnimateDiscard(
            battle.pDeckZone as RectTransform,
            battle.pGraveZone as RectTransform,
            card);

        if (sync != null)
            await sync.WaitForOpponentAnimation();
    }

    async Task SendCpuToGrave(DeckCard deckData)
    {
        var battle = BattleManager.Instance;
        var card = battle.CreateCard(deckData.カード種類, deckData.カードID, battle.eDeckZone);
        if (battle.mainCanvas != null)
            card.transform.SetParent(battle.mainCanvas.transform, true);
        battle.CompleteDeckDeparture(false);

        await CardAnimationManager.Instance.AnimateDiscard(
            battle.eDeckZone as RectTransform,
            battle.eGraveZone as RectTransform,
            card);
    }

    bool Matches(DeckCard deckCard, SkillContext context)
    {
        if (対象条件 == null)
            return true;
        return 対象条件.MatchesDeck(
            deckCard,
            context != null ? context.発動カード : null,
            checkSource: context != null);
    }
}
