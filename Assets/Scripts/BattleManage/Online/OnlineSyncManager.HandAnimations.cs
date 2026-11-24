using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    /// <summary>
    /// 相手に「デッキ/墓地/場から手札へ加えた」アニメーションを同期。
    /// emphasize が真のときは移動だけ再生して待ち、SyncContinueAddToHandEmphasis のあと強調を再生する。
    /// mirrorIndex が 0 以上のときは、受信側のその並びのカードを動かす。場なら mirrorZone も見る。
    /// </summary>
    public void SyncAddToHandAnimation(string cardKindStr, int cardID, int sourceInt, bool emphasize = true, int mirrorIndex = -1, string mirrorZone = null)
    {
        if (!IsOnlineMode)
            return;

        // 共有の相手アニメFIFOとは別チャネル。残件解放で遅延ACKが食い違うのを防ぐ。
        int requestId = BeginAddToHandMoveWait();
        activeAddToHandRequestId = requestId;
        SendRPC(
            "ReceiveAddToHandAnimation",
            requestId,
            cardKindStr,
            cardID,
            sourceInt,
            emphasize,
            mirrorIndex,
            mirrorZone ?? string.Empty);
    }

    /// <summary>
    /// 相手の手札追加を、移動の次の強調演出へ進める。
    /// 呼ぶ前に移動完了を待ち、この中で強調完了の待ちを登録する。
    /// </summary>
    public void SyncContinueAddToHandEmphasis()
    {
        if (!IsOnlineMode)
            return;

        int requestId = activeAddToHandRequestId;
        if (requestId < 0)
            return;

        BeginAddToHandEmphasisWait(requestId);
        SendRPC("ReceiveContinueAddToHandEmphasis", requestId);
    }

    /// <summary>
    /// 手札追加の移動アニメ完了を待つ。
    /// </summary>
    public Task WaitForAddToHandMove()
    {
        return WaitForAddToHandAck(activeAddToHandRequestId, movePhase: true);
    }

    /// <summary>
    /// 手札追加の強調アニメ完了を待つ。
    /// </summary>
    public Task WaitForAddToHandEmphasis()
    {
        return WaitForAddToHandAck(activeAddToHandRequestId, movePhase: false);
    }

    [PunRPC]
    private void ReceiveContinueAddToHandEmphasis(int requestId)
    {
        TaskCompletionSource<bool> continueTcs = null;
        lock (addToHandAckLock)
        {
            if (!addToHandEmphasisContinues.TryGetValue(requestId, out continueTcs))
                addToHandEmphasisContinuePending.Add(requestId);
        }

        continueTcs?.TrySetResult(true);
    }

    /// <summary>
    /// 「デッキ/墓地/場から手札へ加えた」アニメーション受信
    /// </summary>
    [PunRPC]
    private void ReceiveAddToHandAnimation(int requestId, string cardKindStr, int cardID, int sourceInt, bool emphasize, int mirrorIndex, string mirrorZone)
    {
        try
        {
            if (!System.Enum.TryParse<CardKind>(cardKindStr, out CardKind cardKind))
            {
                BeginAddToHandFailureAck(requestId, emphasize);
                return;
            }

            SummonSource source = (SummonSource)sourceInt;

            if (source == SummonSource.墓地)
            {
                RemoveCardFromOpponentGraveyard(cardKind, cardID, mirrorIndex);
                BattleManager.Instance.UpdateEnemyGraveyardUI();
            }
            else if (source == SummonSource.異次元)
            {
                RemoveCardFromOpponentDimension(cardKind, cardID, mirrorIndex);
                BattleManager.Instance.UpdateEnemyDimensionUI();
            }

            BattleManager.Instance.UpdateDeckUI();
            _ = ExecuteAddToHandAnimation(requestId, cardKind, cardID, source, emphasize, mirrorIndex, mirrorZone);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveAddToHandAnimationでエラー: {ex.Message}");
            BeginAddToHandFailureAck(requestId, emphasize);
        }
    }

    /// <summary>
    /// 相手側の画面で「デッキ/墓地/場→手札」アニメーションを実行。
    /// emphasize が真のときは移動後に完了を返し、強調の合図を待ってから拡縮する。
    /// </summary>
    private async Task ExecuteAddToHandAnimation(int requestId, CardKind cardKind, int cardID, SummonSource source, bool emphasize, int mirrorIndex, string mirrorZone)
    {
        CardController tempCard = null;
        bool isFromField = false;
        bool phase1Sent = false;
        TaskCompletionSource<bool> continueTcs = null;
        try
        {
            if (emphasize)
                continueTcs = BeginAddToHandEmphasisContinue(requestId);

            if (source == SummonSource.場)
            {
                isFromField = true;
                tempCard = FindMirroredFieldCard(cardKind, cardID, mirrorZone, mirrorIndex);

                if (tempCard != null && tempCard.トークン)
                {
                    TokenExit.VanishLocal(tempCard);
                    tempCard = null;
                }
                else if (tempCard != null)
                {
                    RectTransform from = tempCard.transform.parent as RectTransform;
                    RectTransform to = BattleManager.Instance.eHandZone as RectTransform;

                    var view = tempCard.GetComponent<BattleCardView>();
                    if (view != null)
                        view.ShowCardBack();

                    await CardAnimationManager.Instance.AnimateReturnToHand(tempCard, from, to);

                    if (tempCard != null && tempCard.gameObject != null)
                    {
                        tempCard.transform.SetParent(to, false);
                        ResetCardStatusForReturnToHand(tempCard);
                        await RetireGrantSources(tempCard);
                        tempCard.UpdateDisplay();
                        var fieldView = tempCard.GetComponent<BattleCardView>();
                        if (fieldView != null)
                            fieldView.ShowCardBack();
                    }
                }
            }
            else
            {
                Transform fromZone = source == SummonSource.デッキ
                    ? BattleManager.Instance.eDeckZone
                    : source == SummonSource.異次元
                        ? BattleManager.Instance.eDimensionZone
                        : BattleManager.Instance.eGraveZone;

                tempCard = BattleManager.Instance.CreateCard(cardKind, cardID, fromZone);
                if (tempCard != null && tempCard.トークン)
                {
                    TokenExit.VanishLocal(tempCard);
                    tempCard = null;
                }
                else
                {
                    var view = tempCard != null ? tempCard.GetComponent<BattleCardView>() : null;
                    if (view != null)
                        view.ShowCardBack();

                    RectTransform from = fromZone as RectTransform;
                    RectTransform to = BattleManager.Instance.eHandZone as RectTransform;

                    if (source == SummonSource.デッキ)
                    {
                        await CardAnimationManager.Instance.AnimateDraw(from, to, tempCard);
                        if (tempCard != null && tempCard.gameObject != null && to != null)
                        {
                            tempCard.transform.SetParent(to, false);
                            tempCard.transform.localPosition = Vector3.zero;
                        }
                        // 移動が終わってから相手デッキの表示を1枚減らす。
                        RemoveOpponentDeckCardForDisplay(cardKind, cardID, mirrorIndex);
                    }
                    else
                    {
                        await CardAnimationManager.Instance.AnimateReturnToHand(tempCard, from, to);
                    }
                }
            }

            if (!emphasize)
                return;

            SendAddToHandMoveComplete(requestId);
            phase1Sent = true;

            if (continueTcs != null)
                await continueTcs.Task;

            await PlayAddToHandEmphasis(tempCard);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ExecuteAddToHandAnimationでエラー: {ex.Message}");
        }
        finally
        {
            try
            {
                if (emphasize && !phase1Sent)
                {
                    SendAddToHandMoveComplete(requestId);
                    phase1Sent = true;
                    if (continueTcs != null)
                        await continueTcs.Task;
                }
            }
            finally
            {
                EndAddToHandEmphasisContinue(requestId, continueTcs);

                if (!isFromField && tempCard != null && tempCard.gameObject != null)
                {
                    try
                    {
                        Destroy(tempCard.gameObject);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (emphasize)
                    SendAddToHandEmphasisComplete(requestId);
                else
                    SendAddToHandMoveComplete(requestId);
            }
        }
    }

    private async Task PlayAddToHandEmphasis(CardController card)
    {
        if (card == null || card.gameObject == null || CardAnimationManager.Instance == null)
            return;

        var viewAfter = card.GetComponent<BattleCardView>();
        if (viewAfter != null && viewAfter.gameObject != null)
            viewAfter.Show(card.model);
        try
        {
            await CardAnimationManager.Instance.AnimateSearchedCard(card, 1.2f, 1.1f, 0.2f);
        }
        finally
        {
            if (viewAfter != null && viewAfter.gameObject != null)
                viewAfter.ShowCardBack();
        }
    }

    int BeginAddToHandMoveWait()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestId;
        lock (addToHandAckLock)
        {
            requestId = nextAddToHandRequestId++;
            addToHandMoveAcks[requestId] = tcs;
        }
        return requestId;
    }

    void BeginAddToHandEmphasisWait(int requestId)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (addToHandAckLock)
            addToHandEmphasisAcks[requestId] = tcs;
    }

    async Task WaitForAddToHandAck(int requestId, bool movePhase)
    {
        if (!IsOnlineMode || requestId < 0)
            return;

        TaskCompletionSource<bool> tcs;
        lock (addToHandAckLock)
        {
            var map = movePhase ? addToHandMoveAcks : addToHandEmphasisAcks;
            if (!map.TryGetValue(requestId, out tcs))
                return;
        }

        string label = movePhase ? "AddToHandMove" : "AddToHandEmphasis";
        try
        {
            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(AddToHandAckTimeoutMs));
            if (completed != tcs.Task)
            {
                Debug.LogWarning($"[OnlineSyncManager] {label} の完了待ちがタイムアウトしました: requestId={requestId}");
                tcs.TrySetResult(true);
            }
            else
                await tcs.Task;
        }
        finally
        {
            lock (addToHandAckLock)
            {
                var map = movePhase ? addToHandMoveAcks : addToHandEmphasisAcks;
                map.Remove(requestId);
            }
        }
    }

    void SendAddToHandMoveComplete(int requestId)
    {
        SendRPC("ReceiveAddToHandMoveComplete", requestId);
    }

    void SendAddToHandEmphasisComplete(int requestId)
    {
        SendRPC("ReceiveAddToHandEmphasisComplete", requestId);
    }

    [PunRPC]
    void ReceiveAddToHandMoveComplete(int requestId)
    {
        CompleteAddToHandAck(requestId, movePhase: true);
    }

    [PunRPC]
    void ReceiveAddToHandEmphasisComplete(int requestId)
    {
        CompleteAddToHandAck(requestId, movePhase: false);
    }

    void CompleteAddToHandAck(int requestId, bool movePhase)
    {
        TaskCompletionSource<bool> tcs = null;
        lock (addToHandAckLock)
        {
            var map = movePhase ? addToHandMoveAcks : addToHandEmphasisAcks;
            if (map.TryGetValue(requestId, out tcs))
            {
                // 待っている側が Remove する。ここでは完了だけ通知する。
            }
        }

        if (tcs == null)
        {
            Debug.LogWarning($"[OnlineSyncManager] 手札追加ACKに対応する待ちがありません: phase={(movePhase ? "move" : "emphasis")}, requestId={requestId}");
            return;
        }

        tcs.TrySetResult(true);
    }

    private TaskCompletionSource<bool> BeginAddToHandEmphasisContinue(int requestId)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (addToHandAckLock)
        {
            addToHandEmphasisContinues[requestId] = tcs;
            if (addToHandEmphasisContinuePending.Remove(requestId))
                tcs.TrySetResult(true);
        }
        return tcs;
    }

    private void EndAddToHandEmphasisContinue(int requestId, TaskCompletionSource<bool> tcs)
    {
        lock (addToHandAckLock)
        {
            if (tcs != null
                && addToHandEmphasisContinues.TryGetValue(requestId, out var current)
                && current == tcs)
            {
                addToHandEmphasisContinues.Remove(requestId);
            }

            addToHandEmphasisContinuePending.Remove(requestId);
        }
    }

    /// <summary>
    /// 移動アニメに入れなかったときも、強調待ちの完了通知を必要な分返す。
    /// </summary>
    private void BeginAddToHandFailureAck(int requestId, bool emphasize)
    {
        if (!emphasize)
        {
            SendAddToHandMoveComplete(requestId);
            return;
        }

        _ = CompleteFailedAddToHandEmphasis(requestId);
    }

    private async Task CompleteFailedAddToHandEmphasis(int requestId)
    {
        var continueTcs = BeginAddToHandEmphasisContinue(requestId);
        try
        {
            SendAddToHandMoveComplete(requestId);
            await continueTcs.Task;
        }
        finally
        {
            EndAddToHandEmphasisContinue(requestId, continueTcs);
            SendAddToHandEmphasisComplete(requestId);
        }
    }

    CardController FindMirroredFieldCard(CardKind cardKind, int cardID, string mirrorZone, int mirrorIndex)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return null;

        if (!string.IsNullOrEmpty(mirrorZone) && mirrorIndex >= 0)
        {
            CardController addressed = battle.GetCardAtZoneIndex(battle.GetZoneByName(mirrorZone), mirrorIndex);
            if (addressed != null && addressed.cardKind == cardKind && addressed.cardID == cardID)
                return addressed;
        }

        CardController Match(Transform zone)
        {
            if (zone == null)
                return null;
            foreach (var card in zone.GetComponentsInChildren<CardController>())
            {
                if (card != null && card.cardKind == cardKind && card.cardID == cardID)
                    return card;
            }
            return null;
        }

        return Match(battle.eBattlerZone) ?? Match(battle.eTacticsZone) ?? Match(battle.eFieldZone);
    }

    // デッキから手札へ着いたあと、表示枚数だけを1つ減らす。リスト枚数では上書きしない。
    // mirrorIndex があるときは、その並びの同じカードを抜く。
    private void RemoveOpponentDeckCardForDisplay(CardKind cardKind, int cardID, int mirrorIndex = -1)
    {
        var enemyDeck = BattleManager.Instance.eBattleDeck;
        if (enemyDeck != null)
        {
            DeckCard removed = null;
            if (mirrorIndex >= 0 && mirrorIndex < enemyDeck.Count)
            {
                var at = enemyDeck[mirrorIndex];
                if (at != null && at.カード種類 == cardKind && at.カードID == cardID)
                    removed = at;
            }

            if (removed == null)
                removed = enemyDeck.FirstOrDefault(c => c != null && c.カード種類 == cardKind && c.カードID == cardID);
            if (removed != null)
                enemyDeck.Remove(removed);
        }

        if (BattleManager.Instance.eDeckCount > 0)
            BattleManager.Instance.eDeckCount = Mathf.Max(0, BattleManager.Instance.eDeckCount - 1);
        BattleManager.Instance.UpdateDeckUI();
    }

    private DeckCard RemoveCardFromOpponentDeck(CardKind cardKind, int cardID, bool refreshUi = true)
    {
        DeckCard removedCard = null;

        var enemyDeck = BattleManager.Instance.eBattleDeck;
        if (enemyDeck != null && enemyDeck.Count > 0)
        {
            removedCard = enemyDeck.FirstOrDefault(c => c.カード種類 == cardKind && c.カードID == cardID);
            if (removedCard != null)
                enemyDeck.Remove(removedCard);
        }

        if (removedCard == null)
        {
            if (BattleManager.Instance.eDeckCount > 0)
                BattleManager.Instance.eDeckCount = Mathf.Max(0, BattleManager.Instance.eDeckCount - 1);
        }
        else
            BattleManager.Instance.eDeckCount = enemyDeck.Count;

        if (refreshUi)
            BattleManager.Instance.UpdateDeckUI();
        return removedCard;
    }

    private void RemoveCardFromOpponentHand(CardKind cardKind, int cardID, int index = -1)
        => RemoveFromOpponentPile(opponentHand, cardKind, cardID, index);

    private void RemoveCardFromOpponentGraveyard(CardKind cardKind, int cardID, int index = -1)
        => RemoveFromOpponentPile(opponentGrave, cardKind, cardID, index);

    /// <summary>
    /// 相手側の一覧から、住所が一致するならその位置、そうでなければ種類とIDで1枚取り除く。
    /// </summary>
    void RemoveFromOpponentPile(List<DeckCard> pile, CardKind cardKind, int cardID, int index = -1)
    {
        if (pile == null)
            return;

        if (index >= 0 && index < pile.Count)
        {
            var addressed = pile[index];
            if (addressed != null && addressed.カード種類 == cardKind && addressed.カードID == cardID)
            {
                pile.RemoveAt(index);
                return;
            }
        }

        var cardToRemove = pile.Find(c => c != null && c.カード種類 == cardKind && c.カードID == cardID);
        if (cardToRemove != null)
            pile.Remove(cardToRemove);
    }

    private void ResetCardStatusForReturnToHand(CardController card)
    {
        if (card == null) return;

        card.RemoveAllPassiveEffects();
        card.ClearStatusAilments();
        card.ResetBonuses();
        card.ResetMoveChanges();
        card.ResetMoveElements();
        card.RecalculateModifiedPowers();
        card.hasUsedMove = false;
        card.RemoveAllTurnRestrictions();
        card.ClearInvalidElements();
        card.damageBonus = 0;
        card.hitRate = 0;
        card.hpProtection1 = false;
        card.hpProtectionTargetElement1 = ElementList.None;
        card.hpProtection2 = false;
        card.hpProtectionTargetElement2 = ElementList.None;

    }

    /// <summary>
    /// 相手の実カードを、相手の手札へ移させる。
    /// deckIndex が 0 以上なら相手デッキのその並び。それ以外は送信者から見た場の住所。
    /// </summary>
    public Task RequestOpponentCardToOwnHand(string cardKind, int cardId, string zoneName, int zoneIndex, int deckIndex)
    {
        if (!IsOnlineMode)
            return Task.CompletedTask;

        return AwaitOpponentOwnMove(() => SendRPC(
            "ReceiveOpponentCardToOwnHand",
            cardKind ?? string.Empty,
            cardId,
            zoneName ?? string.Empty,
            zoneIndex,
            deckIndex));
    }

    /// <summary>
    /// 相手の手札へ加えたあとに、相手側で手札を混ぜて一覧を返させる。
    /// </summary>
    public Task RequestOpponentShuffleAddedHand()
    {
        if (!IsOnlineMode)
            return Task.CompletedTask;

        return AwaitOpponentOwnMove(() => SendRPC("ReceiveOpponentShuffleAddedHand"));
    }

    /// <summary>
    /// 相手に相手自身のカードを動かすよう頼み、終わりの合図 ReceiveOpponentOwnHandFinished まで待つ。
    /// </summary>
    Task AwaitOpponentOwnMove(System.Action send)
    {
        return AwaitRemoteAction(opponentOwnHandFinished, send);
    }

    [PunRPC]
    void ReceiveOpponentCardToOwnHand(string cardKind, int cardId, string zoneName, int zoneIndex, int deckIndex)
    {
        _ = ResolveOpponentCardToOwnHand(cardKind, cardId, zoneName, zoneIndex, deckIndex);
    }

    async Task ResolveOpponentCardToOwnHand(string cardKind, int cardId, string zoneName, int zoneIndex, int deckIndex)
    {
        BeginOperationBlock();
        try
        {
            if (deckIndex >= 0)
                await ToHandSkill.AddOpponentDeckCardToOwnHand(cardKind, cardId, deckIndex);
            else
            {
                CardController card = CardAddress.Resolve(cardKind, cardId, zoneName, zoneIndex);
                if (card != null)
                    await ToHandSkill.MoveOwnExistingCardToHand(card, zoneName, zoneIndex);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 相手手札への移動でエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
            SendRPC("ReceiveOpponentOwnHandFinished");
        }
    }

    [PunRPC]
    void ReceiveOpponentShuffleAddedHand()
    {
        try
        {
            if (BattleManager.Instance != null)
                BattleManager.Instance.ShuffleHandAfterAddition(true, false);
        }
        finally
        {
            SendRPC("ReceiveOpponentOwnHandFinished");
        }
    }

    [PunRPC]
    void ReceiveOpponentOwnHandFinished()
    {
        opponentOwnHandFinished.SetComplete();
    }
}
