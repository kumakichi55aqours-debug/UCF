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

    /// <summary>
    /// 相手に「デッキ/墓地/場から手札へ加えた」アニメーションを同期。
    /// emphasize が真のときは移動だけ再生して待ち、SyncContinueAddToHandEmphasis のあと強調を再生する。
    /// </summary>
    public void SyncAddToHandAnimation(string cardKindStr, int cardID, int sourceInt, bool emphasize = true)
    {
        if (IsOnlineMode)
        {
            RegisterOpponentAnimationWait();
            SendRPC("ReceiveAddToHandAnimation", cardKindStr, cardID, sourceInt, emphasize);
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
    private void ReceiveAddToHandAnimation(string cardKindStr, int cardID, int sourceInt, bool emphasize)
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
                RemoveCardFromOpponentGraveyard(cardKind, cardID);
                BattleManager.Instance.UpdateEnemyGraveyardUI();
            }

            BattleManager.Instance.UpdateDeckUI();
            _ = ExecuteAddToHandAnimation(cardKind, cardID, source, emphasize);
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
    private async Task ExecuteAddToHandAnimation(CardKind cardKind, int cardID, SummonSource source, bool emphasize)
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

                CardController[] enemyBattlers = BattleManager.Instance.eBattlerZone.GetComponentsInChildren<CardController>();
                CardController[] enemyTactics = BattleManager.Instance.eTacticsZone.GetComponentsInChildren<CardController>();
                CardController[] enemyFields = BattleManager.Instance.eFieldZone.GetComponentsInChildren<CardController>();

                tempCard = enemyBattlers.FirstOrDefault(c => c.cardKind == cardKind && c.cardID == cardID)
                    ?? enemyTactics.FirstOrDefault(c => c.cardKind == cardKind && c.cardID == cardID)
                    ?? enemyFields.FirstOrDefault(c => c.cardKind == cardKind && c.cardID == cardID);

                if (tempCard != null)
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
                    : BattleManager.Instance.eGraveZone;

                tempCard = BattleManager.Instance.CreateCard(cardKind, cardID, fromZone);
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
                    RemoveOpponentDeckCardForDisplay(cardKind, cardID);
                }
                else
                {
                    await CardAnimationManager.Instance.AnimateReturnToHand(tempCard, from, to);
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

    // デッキから手札へ着いたあと、表示枚数だけを1つ減らす。リスト枚数では上書きしない。
    private void RemoveOpponentDeckCardForDisplay(CardKind cardKind, int cardID)
    {
        var enemyDeck = BattleManager.Instance.eBattleDeck;
        if (enemyDeck != null)
        {
            var removed = enemyDeck.FirstOrDefault(c => c.カード種類 == cardKind && c.カードID == cardID);
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
            opponentGrave.RemoveAt(index);
            return;
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
}

