using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    // emphasize 付きの手札追加は、移動完了と強調完了で完了通知を2回返す。
    // 2回目は SyncContinueAddToHandEmphasis を受けてから送る。
    private TaskCompletionSource<bool> addToHandEmphasisContinue;
    private bool addToHandEmphasisContinuePending;
    TaskCompletionSource<bool> opponentOwnHandFinished;

    /// <summary>
    /// 相手に「デッキ/墓地/場から手札へ加えた」アニメーションを同期。
    /// emphasize が真のときは移動だけ再生して待ち、SyncContinueAddToHandEmphasis のあと強調を再生する。
    /// mirrorIndex が 0 以上のときは、受信側のその並びのカードを動かす。場なら mirrorZone も見る。
    /// </summary>
    public void SyncAddToHandAnimation(string cardKindStr, int cardID, int sourceInt, bool emphasize = true, int mirrorIndex = -1, string mirrorZone = null)
    {
        if (IsOnlineMode)
        {
            RegisterOpponentAnimationWait();
            SendRPC("ReceiveAddToHandAnimation", cardKindStr, cardID, sourceInt, emphasize, mirrorIndex, mirrorZone ?? string.Empty);
        }
    }

    /// <summary>
    /// 相手の手札追加を、移動の次の強調演出へ進める。
    /// 呼ぶ前に移動完了を待ち、この中で強調完了の待ちを登録する。
    /// </summary>
    public void SyncContinueAddToHandEmphasis()
    {
        if (!IsOnlineMode)
            return;

        RegisterOpponentAnimationWait();
        SendRPC("ReceiveContinueAddToHandEmphasis");
    }

    [PunRPC]
    private void ReceiveContinueAddToHandEmphasis()
    {
        if (addToHandEmphasisContinue != null)
            addToHandEmphasisContinue.TrySetResult(true);
        else
            addToHandEmphasisContinuePending = true;
    }

    /// <summary>
    /// 「デッキ/墓地/場から手札へ加えた」アニメーション受信
    /// </summary>
    [PunRPC]
    private void ReceiveAddToHandAnimation(string cardKindStr, int cardID, int sourceInt, bool emphasize, int mirrorIndex, string mirrorZone)
    {
        try
        {
            if (!System.Enum.TryParse<CardKind>(cardKindStr, out CardKind cardKind))
            {
                BeginAddToHandFailureAck(emphasize);
                return;
            }

            SummonSource source = (SummonSource)sourceInt;

            if (source == SummonSource.デッキ)
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
            _ = ExecuteAddToHandAnimation(cardKind, cardID, source, emphasize, mirrorIndex, mirrorZone);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveAddToHandAnimationでエラー: {ex.Message}");
            BeginAddToHandFailureAck(emphasize);
        }
    }

    /// <summary>
    /// 相手側の画面で「デッキ/墓地/場→手札」アニメーションを実行。
    /// emphasize が真のときは移動後に完了を返し、強調の合図を待ってから拡縮する。
    /// </summary>
    private async Task ExecuteAddToHandAnimation(CardKind cardKind, int cardID, SummonSource source, bool emphasize, int mirrorIndex, string mirrorZone)
    {
        CardController tempCard = null;
        bool isFromField = false;
        bool phase1Sent = false;
        TaskCompletionSource<bool> continueTcs = null;
        try
        {
            if (emphasize)
                continueTcs = BeginAddToHandEmphasisWait();

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
                Transform fromZone = source == SummonSource.墓地
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

                    if (source == SummonSource.墓地)
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

            SendOpponentAnimationComplete();
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
                    SendOpponentAnimationComplete();
                    phase1Sent = true;
                    if (continueTcs != null)
                        await continueTcs.Task;
                }
            }
            finally
            {
                EndAddToHandEmphasisWait(continueTcs);

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

                SendOpponentAnimationComplete();
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

    private TaskCompletionSource<bool> BeginAddToHandEmphasisWait()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        addToHandEmphasisContinue = tcs;
        if (addToHandEmphasisContinuePending)
        {
            addToHandEmphasisContinuePending = false;
            tcs.TrySetResult(true);
        }
        return tcs;
    }

    private void EndAddToHandEmphasisWait(TaskCompletionSource<bool> tcs)
    {
        if (tcs != null && addToHandEmphasisContinue == tcs)
            addToHandEmphasisContinue = null;
    }

    /// <summary>
    /// 移動アニメに入れなかったときも、強調待ちの完了通知を2件返す。
    /// </summary>
    private void BeginAddToHandFailureAck(bool emphasize)
    {
        if (!emphasize)
        {
            SendOpponentAnimationComplete();
            return;
        }

        _ = CompleteFailedAddToHandEmphasis();
    }

    private async Task CompleteFailedAddToHandEmphasis()
    {
        var continueTcs = BeginAddToHandEmphasisWait();
        try
        {
            SendOpponentAnimationComplete();
            await continueTcs.Task;
        }
        finally
        {
            EndAddToHandEmphasisWait(continueTcs);
            SendOpponentAnimationComplete();
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
    {
        if (index >= 0 && index < opponentHand.Count)
        {
            opponentHand.RemoveAt(index);
            return;
        }

        var cardToRemove = opponentHand.FirstOrDefault(c => c.カード種類 == cardKind && c.カードID == cardID);
        if (cardToRemove != null)
            opponentHand.Remove(cardToRemove);
    }

    private void RemoveCardFromOpponentGraveyard(CardKind cardKind, int cardID, int index = -1)
    {
        if (index >= 0 && index < opponentGrave.Count)
        {
            var addressed = opponentGrave[index];
            if (addressed != null && addressed.カード種類 == cardKind && addressed.カードID == cardID)
            {
                opponentGrave.RemoveAt(index);
                return;
            }
        }

        var cardToRemove = opponentGrave.FirstOrDefault(c => c.カード種類 == cardKind && c.カードID == cardID);
        if (cardToRemove != null)
            opponentGrave.Remove(cardToRemove);
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
    async Task AwaitOpponentOwnMove(System.Action send)
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        opponentOwnHandFinished = finished;
        BeginOperationBlock();
        try
        {
            send();
            await finished.Task;
        }
        finally
        {
            if (opponentOwnHandFinished == finished)
                opponentOwnHandFinished = null;
            EndOperationBlock();
        }
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
        opponentOwnHandFinished?.TrySetResult(true);
    }
}

