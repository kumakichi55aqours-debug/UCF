using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 墓地一覧と、スキルによる墓地送りの同期
/// </summary>
public partial class OnlineSyncManager
{
    public List<DeckCard> GetOpponentGrave() => opponentGrave;

    // 自分の墓地一覧を相手へ送信
    public void SendGraveInfoToOpponent()
    {
        try
        {
            var graveInfo = new List<object>();
            CardController[] graveCards = BattleManager.Instance.pGraveZone.GetComponentsInChildren<CardController>();

            foreach (var card in graveCards)
            {
                if (card == null)
                    continue;

                graveInfo.Add(card.cardKind.ToString());
                graveInfo.Add(card.cardID);
                graveInfo.Add(card.cost);
            }

            // Photonは可変長引数として展開するため、配列は1引数にラップする
            SendRPC("ReceiveOpponentGraveInfo", new object[] { graveInfo.ToArray() });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"墓地情報送信でエラー: {ex.Message}");
        }
    }

    [PunRPC]
    private void ReceiveOpponentGraveInfo(object[] graveInfo)
    {
        try
        {
            opponentGrave.Clear();
            ReadCardTriples(graveInfo, (first, second, third) =>
            {
                if (!(first is string cardKindStr) || !(second is int cardID) || !(third is int cost))
                    return;

                AddParsedDeckCard(opponentGrave, cardKindStr, cardID, cost);
            });

            BattleManager.Instance.UpdateEnemyGraveyardUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"相手の墓地情報受信でエラー: {ex.Message}");
        }
    }

    // 墓地に送るカードを送信
    public void SyncCardToGrave(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        SendAnimatedRpc("ReceiveCardToGrave", cardKindStr, cardID, zoneName, zoneIndex);
    }

    // 複数対象の墓地送りを、動かす前のゾーン番号でまとめて同期
    public void SyncAreaGrave(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        if (!HasCardAddresses(cardKinds, cardIds, zoneNames, indices))
            return;

        SendAnimatedRpc("ReceiveAreaGrave", cardKinds, cardIds, zoneNames, indices);
    }

    [PunRPC]
    private void ReceiveCardToGrave(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        ReceiveAnimatedCardAction(cardKindStr, cardID, zoneName, zoneIndex, "墓地送り受信でエラー", card =>
        {
            if (card.isMyCard)
                _ = DiscardOwnCard(card);
            else
                _ = DiscardEnemyCard(card);
        });
    }

    [PunRPC]
    private void ReceiveAreaGrave(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        try
        {
            var cards = new List<CardController>();
            CollectResolvedCards(cardKinds, cardIds, zoneNames, indices, null, cards, null);
            _ = DiscardCards(cards);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 複数墓地送り受信でエラー: {ex.Message}");
            SendOpponentAnimationComplete();
        }
    }

    private async Task DiscardEnemyCard(CardController card, bool notifyComplete = true)
    {
        if (await TryVanishDiscardedToken(card, notifyComplete))
            return;

        try
        {
            await PlayDefeat(card);
            ResetCardStatusForGrave(card);
            card.UpdateDisplay();
            await MoveToGraveAndRetireGrants(card, BattleManager.Instance.eGraveZone);

            card.UpdateDisplay();
            card.battleCardview?.UpdateCanUseMovePanel();
            PersistentEffectManager.Instance?.RecalculateImmediately();
            RememberEnemyGraveCard(card);
            BattleManager.Instance.UpdateEnemyGraveyardUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 相手カードの墓地送りでエラー: {ex.Message}");
        }
        finally
        {
            if (notifyComplete)
                SendOpponentAnimationComplete();
        }
    }

    private async Task DiscardOwnCard(CardController card, bool notifyComplete = true)
    {
        if (await TryVanishDiscardedToken(card, notifyComplete))
            return;

        try
        {
            await PlayDefeat(card);
            ResetCurHpToMax(card);
            card.UpdateDisplay();
            card.RemoveAllPassiveEffects();
            card.ClearStatusAilments(BattleManager.Instance != null && BattleManager.Instance.IsStunResolvePending(card));

            Transform graveZone = card.isMyCard ? BattleManager.Instance.pGraveZone : BattleManager.Instance.eGraveZone;
            await MoveToGraveAndRetireGrants(card, graveZone);
            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 自分カードの墓地送りでエラー: {ex.Message}");
        }
        finally
        {
            if (notifyComplete)
            {
                // 送り元は自分の画面で動かし終えるまで一覧を当てない。完了通知より先に送る。
                SendGraveInfoToOpponent();
                SendOpponentAnimationComplete();
            }
        }
    }

    private async Task DiscardCards(List<CardController> cards)
    {
        try
        {
            foreach (var card in cards)
            {
                if (card == null)
                    continue;

                if (card.isMyCard)
                    await DiscardOwnCard(card, false);
                else
                    await DiscardEnemyCard(card, false);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 複数墓地送りでエラー: {ex.Message}");
        }
        finally
        {
            SendGraveInfoToOpponent();
            SendOpponentAnimationComplete();
        }
    }

    /// <summary>
    /// 墓地・異次元に置く相手のカードを、場にいたときの状態から戻す。HPは最大に戻す。
    /// </summary>
    public static void ResetCardStatusForGrave(CardController card)
    {
        if (card == null)
            return;

        ResetCurHpToMax(card);
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
        card.ClearStatusAilments(BattleManager.Instance != null && BattleManager.Instance.IsStunResolvePending(card));
        card.ResetBonuses();
        card.ResetMoveChanges();
        card.ResetMoveElements();
        card.RecalculateModifiedPowers();
    }

    static async Task PlayDefeat(CardController card)
    {
        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.PlayDefeatExplosion(card);
    }

    async Task<bool> TryVanishDiscardedToken(CardController card, bool notifyComplete)
    {
        if (card == null || !card.トークン)
            return false;

        try
        {
            await PlayDefeat(card);
            TokenExit.VanishLocal(card);
        }
        finally
        {
            if (notifyComplete)
                SendOpponentAnimationComplete();
        }

        return true;
    }

    async Task MoveToGraveAndRetireGrants(CardController card, Transform graveZone)
    {
        await AnimateToGrave(card, graveZone);
        await RetireGrantSources(card);
    }

    static Task RetireGrantSources(CardController host)
    {
        BattleManager battle = BattleManager.Instance;
        return battle != null ? battle.RetireEnhancementsIfSpent(host) : Task.CompletedTask;
    }

    static async Task AnimateToGrave(CardController card, Transform graveZone)
    {
        RectTransform currentSlot = card.transform.parent as RectTransform;
        RectTransform graveSlot = graveZone as RectTransform;
        await CardAnimationManager.Instance.AnimateDiscard(currentSlot, graveSlot, card);
    }

    static void ResetCurHpToMax(CardController card)
    {
        if (card.cardKind == CardKind.バトラー && card.model is BattlerModel battlerModel)
            card.curHp = battlerModel.maxHp;
        else if (card.cardKind == CardKind.フィールド && card.model is FieldModel fieldModel)
            card.curHp = fieldModel.maxHp;
    }

    /// <summary>
    /// 相手のデッキにある、指定の種類とIDのカードをすべて、相手自身に墓地へ送らせる。
    /// 実際のデッキは相手の端末にしかないので、こちらの写しだけを削らない。
    /// </summary>
    public Task RequestOpponentDeckToOwnGrave(CardKind kind, int id)
    {
        if (!IsOnlineMode)
            return Task.CompletedTask;

        return AwaitOpponentOwnMove(() => SendRPC("ReceiveOpponentDeckToOwnGrave", kind.ToString(), id));
    }

    /// <summary>
    /// 相手デッキから選んだカードを、相手自身に墓地か異次元へ送らせる。
    /// デッキの中の同じ種類・IDのカードは区別しないので、並びは送らない。
    /// </summary>
    public Task RequestOpponentDeckCardsToOwnPile(IReadOnlyList<DeckCard> cards, bool toDimension)
    {
        if (!IsOnlineMode || cards == null || cards.Count == 0)
            return Task.CompletedTask;

        var kinds = new int[cards.Count];
        var ids = new int[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            kinds[i] = cards[i] != null ? (int)cards[i].カード種類 : -1;
            ids[i] = cards[i] != null ? cards[i].カードID : -1;
        }

        return AwaitOpponentOwnMove(() => SendRPC("ReceiveOpponentDeckCardsToOwnPile", kinds, ids, toDimension));
    }

    [PunRPC]
    void ReceiveOpponentDeckCardsToOwnPile(int[] kinds, int[] ids, bool toDimension)
    {
        _ = ResolveOpponentDeckCardsToOwnPile(kinds, ids, toDimension);
    }

    async Task ResolveOpponentDeckCardsToOwnPile(int[] kinds, int[] ids, bool toDimension)
    {
        BeginOperationBlock();
        try
        {
            var deck = BattleManager.Instance != null ? BattleManager.Instance.pBattleDeck : null;
            var picked = new List<DeckCard>();
            int count = kinds != null && ids != null ? Mathf.Min(kinds.Length, ids.Length) : 0;
            for (int i = 0; deck != null && i < count; i++)
            {
                DeckCard match = deck.Find(d => d != null && !picked.Contains(d)
                    && (int)d.カード種類 == kinds[i] && d.カードID == ids[i]);
                if (match != null)
                    picked.Add(match);
            }

            if (toDimension)
                await ToDimensionSkill.SendOwnDeckCardsToDimension(picked);
            else
                await ToGraveSkill.SendOwnDeckCardsToGrave(picked);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 自分のデッキからの送りでエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
            SendRPC("ReceiveOpponentOwnHandFinished");
        }
    }

    [PunRPC]
    void ReceiveOpponentDeckToOwnGrave(string kindStr, int id)
    {
        _ = ResolveOpponentDeckToOwnGrave(kindStr, id);
    }

    async Task ResolveOpponentDeckToOwnGrave(string kindStr, int id)
    {
        BeginOperationBlock();
        try
        {
            if (System.Enum.TryParse(kindStr, out CardKind kind))
                await ToGraveSkill.SendOwnDeckCardsToGrave(kind, id);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 自分のデッキからの墓地送りでエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
            SendRPC("ReceiveOpponentOwnHandFinished");
        }
    }

    // 相手に上からデッキ枚数墓地送る同期
    public Task RequestOpponentTopDeckToGrave(int count)
    {
        if (!IsOnlineMode || count <= 0)
            return Task.CompletedTask;

        return AwaitOpponentOwnMove(
            () => SendRPC("ReceiveOpponentTopDeckToGrave", count));
    }

    [PunRPC]
    void ReceiveOpponentTopDeckToGrave(int count)
    {
        _ = ResolveOpponentTopDeckToGrave(count);
    }

    async Task ResolveOpponentTopDeckToGrave(int count)
    {
        BeginOperationBlock();
        try
        {
            var battle = BattleManager.Instance;
            var deck = battle != null ? battle.pBattleDeck : null;

            if (deck == null || count <= 0)
                return;

            var topCards = deck
                .Take(Mathf.Min(count, deck.Count))
                .ToList();

            await ToGraveSkill.SendOwnDeckCardsToGrave(topCards);
        }
        catch (System.Exception ex)
        {
            Debug.LogError(
                $"[OnlineSyncManager] デッキ上部の墓地送りでエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
            SendRPC("ReceiveOpponentOwnHandFinished");
        }
    }

    /// <summary>
    /// こちらの画面で相手のカードを相手墓地へ動かしたあと、相手墓地の一覧に足す。着いていなければ足さない。
    /// </summary>
    public void NoteCardEnteredOpponentGrave(CardController card)
    {
        var battle = BattleManager.Instance;
        if (!IsOnlineMode || card == null || battle == null || card.transform.parent != battle.eGraveZone)
            return;

        RememberEnemyGraveCard(card);
    }

    void RememberEnemyGraveCard(CardController card)
    {
        if (card == null)
            return;

        RememberEnemyGraveCard(card.cardKind, card.cardID);
    }

    void RememberEnemyGraveCard(CardKind kind, int id)
    {
        var graveCard = new DeckCard(kind, id, 1);
        graveCard.PopulateDerivedData();
        opponentGrave.Add(graveCard);
    }
}
