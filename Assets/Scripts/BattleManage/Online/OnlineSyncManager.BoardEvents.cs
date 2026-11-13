using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    /// <summary>
    /// 相手側のリソースリセット処理を同期
    /// </summary>
    public void SyncOpponentResourcesReset(int turnNumber)
    {
        SendRPC("ReceiveOpponentResourcesReset", turnNumber);
    }

    /// <summary>
    /// 相手側のリソースリセット処理を受信
    /// </summary>
    [PunRPC]
    private void ReceiveOpponentResourcesReset(int turnNumber)
    {
        BattleManager.Instance.eResources.Cost.ResetCost(turnNumber);
        BattleManager.Instance.eResources.Energy.ResetEnergy(turnNumber);
        BattleManager.Instance.OnResourcesReset(false);
    }

    /// <summary>
    /// 相手側のコスト変更を同期
    /// </summary>
    public void SyncOpponentCostChange(int costAmount)
    {
        SendRPC("ReceiveOpponentCostChange", costAmount);
    }

    /// <summary>
    /// 相手側のコスト変更を受信
    /// </summary>
    [PunRPC]
    private void ReceiveOpponentCostChange(int costAmount)
    {
        BattleManager.Instance.eResources.Cost.TakeCost(costAmount);
    }

    /// <summary>
    /// 相手のエネルギー変更を同期
    /// </summary>
    public void SyncOpponentEnergyChange(int energyChange)
    {
        SendRPC("ReceiveOpponentEnergyChange", energyChange);
    }

    /// <summary>
    /// 相手のエネルギー変更を受信
    /// </summary>
    [PunRPC]
    private void ReceiveOpponentEnergyChange(int energyChange)
    {
        BattleManager.Instance.eResources.Energy.AddEnegry(energyChange);
    }

    /// <summary>
    /// 相手の手札から墓地へ送ったあと、両者で「墓地に送られた時」の窓に入る。
    /// 住所は送信者から見た墓地のカード。fromZone は送る前のゾーンで、トリガー用。
    /// </summary>
    public void SyncSentToGrave(string cardKindStr, int cardID, string zoneName, int zoneIndex, int fromZone)
    {
        SendRPC("ReceiveSentToGrave", cardKindStr, cardID, zoneName, zoneIndex, fromZone);
    }

    /// <summary>
    /// 受信者自身の墓地にある実カードで、「墓地に送られた時」を現在のチェーンのあとへ回す。
    /// </summary>
    [PunRPC]
    private void ReceiveSentToGrave(string cardKindStr, int cardID, string zoneName, int zoneIndex, int fromZone)
    {
        if (!IsOnlineMode)
            return;
        CardZoneType senderZone = (CardZoneType)fromZone;
        CardZoneType localZone = CardController.ToOwnerPerspective(senderZone, false);
        CardController card = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
        SkillChainManager.Instance?.ScheduleSentToGraveyard(card, localZone);
    }

    /// <summary>
    /// カードが手札へ戻った事実を相手へ送る。中身は送らず、移動前のゾーンと、送信者から見た持ち主だけ。
    /// </summary>
    public void SyncCardReturnedToHand(int fromZone, bool returnedCardIsMine)
    {
        SendRPC("ReceiveCardReturnedToHand", fromZone, returnedCardIsMine);
    }

    /// <summary>
    /// 受信者の視点に直して、「他のカードが手札に戻った時」を現在のチェーンのあとへ回す。
    /// </summary>
    [PunRPC]
    private void ReceiveCardReturnedToHand(int fromZone, bool senderCardIsMine)
    {
        if (!IsOnlineMode)
            return;

        CardZoneType localZone = CardController.ToOwnerPerspective((CardZoneType)fromZone, false);
        SkillChainManager.Instance?.ScheduleCardReturnedToHand(localZone, !senderCardIsMine, notifyOpponent: false);
    }

    /// <summary>
    /// 手札から墓地へのアニメーションを同期する。
    /// 住所が受信者の手札なら、その実カードを墓地へ動かす。
    /// </summary>
    public void SyncDiscardAnimation(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        if (IsOnlineMode)
        {
            RegisterOpponentAnimationWait();
            SendRPC("ReceiveDiscardAnimation", cardKindStr, cardID, zoneName, zoneIndex);
        }
    }

    /// <summary>
    /// 相手のデッキから墓地へのアニメーションを同期
    /// </summary>
    public void SyncDeckToGraveyard(string cardKindStr, int cardID)
    {
        if (IsOnlineMode)
        {
            RegisterOpponentAnimationWait();
            SendRPC("ReceiveDeckToGraveyard", cardKindStr, cardID);
        }
    }

    /// <summary>
    /// 手札から墓地へのアニメーションを受信
    /// </summary>
    [PunRPC]
    private void ReceiveDiscardAnimation(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        if (IsOnlineMode)
        {
            try
            {
                _ = ExecuteDiscardAnimation(cardKindStr, cardID, zoneName, zoneIndex);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"ReceiveDiscardAnimationでエラー: {ex.Message}");
                SendOpponentAnimationComplete();
            }
        }
    }

    /// <summary>
    /// 相手のデッキから墓地へのアニメーションを受信
    /// </summary>
    [PunRPC]
    private void ReceiveDeckToGraveyard(string cardKindStr, int cardID)
    {
        if (IsOnlineMode)
        {
            try
            {
                if (System.Enum.TryParse<CardKind>(cardKindStr, out CardKind cardKind))
                {
                    _ = ExecuteDeckToGraveyard(cardKind, cardID);
                }
                else
                {
                    Debug.LogError($"ReceiveDeckToGraveyard: 無効なカード種類 {cardKindStr}");
                    SendOpponentAnimationComplete();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"ReceiveDeckToGraveyardでエラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 手札・異次元から墓地へのアニメーションを実行
    /// </summary>
    private async Task ExecuteDiscardAnimation(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        var battle = BattleManager.Instance;
        CardController addressed = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
        if (addressed != null && addressed.isMyCard && addressed.transform.parent != battle.pGraveZone)
        {
            await ExecuteOwnCardDiscard(addressed);
            return;
        }
        if (addressed != null && !addressed.isMyCard && addressed.transform.parent == battle.eDimensionZone)
        {
            await ExecuteOpponentDimensionDiscard(addressed);
            return;
        }
        CardController tempCard = null;
        try
        {
            // カード種類をパース
            if (!System.Enum.TryParse(cardKindStr, out CardKind cardKind))
            {
                Debug.LogError($"無効なカード種類: {cardKindStr}");
                return;
            }

            // 新しいカードオブジェクトを作成
            tempCard = BattleManager.Instance.CreateCard(cardKind, cardID, BattleManager.Instance.eHandZone);
            if (tempCard == null || tempCard.gameObject == null || tempCard.transform == null)
            {
                Debug.LogError($"カードオブジェクトの作成に失敗: {cardKindStr} ID:{cardID}");
                return;
            }

            // アニメーション用のスロットを取得
            RectTransform handSlot = BattleManager.Instance.eHandZone as RectTransform;
            RectTransform graveSlot = BattleManager.Instance.eGraveZone as RectTransform;
            await CardAnimationManager.Instance.AnimateDiscard(handSlot, graveSlot, tempCard);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteDiscardAnimationでエラー: {ex.Message}");
        }
        finally
        {
            // 墓地へ着いたカードは、相手から届く墓地一覧の同期でそのまま使う。着かなかったときだけ破棄する。
            bool arrived = tempCard != null && tempCard.transform.parent == BattleManager.Instance.eGraveZone;
            if (tempCard != null && !arrived)
            {
                try
                {
                    // DOTweenアニメーションを強制終了（残っている場合）
                    DOTween.Kill(tempCard.transform);
                    // 一覧の同期が、破棄待ちのカードを使い回さないよう先に隠す
                    tempCard.gameObject.SetActive(false);
                    Destroy(tempCard.gameObject);
                }
                catch (System.Exception)
                {
                    // 破棄に失敗しても完了通知は送る
                }
            }
            SendOpponentAnimationComplete();
        }
    }

    /// <summary>
    /// 受信者自身の手札・異次元にあるカードを、自分の墓地へ動かす。
    /// 終わったら元のゾーンと墓地の一覧を送り、発動側の相手表示を合わせる。
    /// </summary>
    private async Task ExecuteOwnCardDiscard(CardController card)
    {
        try
        {
            if (card == null)
                return;
            bool fromHand = card.transform.parent == BattleManager.Instance.pHandZone;
            if (card.トークン)
            {
                TokenExit.VanishLocal(card);
                if (fromHand)
                    SendDetailedHandInfoToOpponent();
                else
                    SendDimensionInfoToOpponent();
                return;
            }
            ResetCardStatusForGrave(card);
            RectTransform fromSlot = card.transform.parent as RectTransform;
            RectTransform graveSlot = BattleManager.Instance.pGraveZone as RectTransform;
            // 手札からの「墓地に送られた時」は、発動側が両者の窓をそろえて出す
            await CardAnimationManager.Instance.AnimateDiscard(fromSlot, graveSlot, card, suppressSkillTrigger: fromHand);
            card.UpdateDisplay();
            card.battleCardview?.UpdateCanUseMovePanel();
            if (fromHand)
                SendDetailedHandInfoToOpponent();
            else
                SendDimensionInfoToOpponent();
            SendGraveInfoToOpponent();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteOwnCardDiscardでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    /// <summary>
    /// 相手異次元にある表示カードを、相手墓地へ動かす。相手が自分の異次元のカードを墓地へ送ったとき。
    /// </summary>
    private async Task ExecuteOpponentDimensionDiscard(CardController card)
    {
        var battle = BattleManager.Instance;
        try
        {
            if (card.トークン)
            {
                NoteCardLeavingOpponentPile(card);
                TokenExit.VanishLocal(card);
                return;
            }

            RectTransform fromSlot = card.transform.parent as RectTransform;
            NoteCardLeavingOpponentPile(card);
            ResetCardStatusForGrave(card);
            card.battleCardview?.Show(card.model);

            battle.BeginEnemyGraveArrival();
            try
            {
                await CardAnimationManager.Instance.AnimateDiscard(fromSlot, battle.eGraveZone as RectTransform, card);
                NoteCardEnteredOpponentGrave(card);
            }
            finally
            {
                battle.EndEnemyGraveArrival();
            }

            card.UpdateDisplay();
            battle.UpdateEnemyDimensionUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteOpponentDimensionDiscardでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    /// <summary>
    /// デッキから墓地へのアニメーションを実行
    /// </summary>
    private async Task ExecuteDeckToGraveyard(CardKind cardKind, int cardID)
    {
        CardController tempCard = null;
        try
        {
            Transform deckZone = BattleManager.Instance.eDeckZone;
            Transform graveZone = BattleManager.Instance.eGraveZone;
            if (deckZone == null || graveZone == null)
            {
                return;
            }
            DeckCard removedDeckCard = RemoveCardFromOpponentDeck(cardKind, cardID, refreshUi: false);
            RectTransform deckSlot = deckZone as RectTransform;
            RectTransform graveSlot = graveZone as RectTransform;
            if (deckSlot == null || graveSlot == null)
            {
                BattleManager.Instance.UpdateDeckUI();
                return;
            }
            tempCard = BattleManager.Instance.CreateCard(cardKind, cardID, deckZone);
            if (tempCard == null || tempCard.transform == null)
            {
                BattleManager.Instance.UpdateDeckUI();
                return;
            }
            if (tempCard.トークン)
            {
                Destroy(tempCard.gameObject);
                tempCard = null;
                BattleManager.Instance.UpdateDeckUI();
                return;
            }
            if (BattleManager.Instance.mainCanvas != null)
                tempCard.transform.SetParent(BattleManager.Instance.mainCanvas.transform, true);
            BattleManager.Instance.UpdateDeckUI();
            await CardAnimationManager.Instance.AnimateDiscard(deckSlot, graveSlot, tempCard);
            DeckCard graveyardCard;
            if (removedDeckCard != null)
            {
                List<CardCategoryList> categoryCopy = removedDeckCard.カテゴリ != null
                    ? new List<CardCategoryList>(removedDeckCard.カテゴリ)
                    : null;
                graveyardCard = new DeckCard(
                    removedDeckCard.カード種類,
                    removedDeckCard.カードID,
                    removedDeckCard.コスト,
                    categoryCopy,
                    removedDeckCard.カードタイプ);
            }
            else
            {
                graveyardCard = new DeckCard(cardKind, cardID, 1);
                graveyardCard.PopulateDerivedData();
            }
            opponentGrave.Add(graveyardCard);
            BattleManager.Instance.UpdateEnemyGraveyardUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteDeckToGraveyardでエラー: {ex.Message}");
        }
        finally
        {
            BattleManager.Instance.UpdateDeckUI();
            SendOpponentAnimationComplete();
        }
    }
    #region パッシブ効果同期
    /// <summary>
    /// パッシブ効果の同期
    /// </summary>
    public void SyncPassiveEffect(string targetCardKindStr, int targetCardID, string zoneName, int zoneIndex,
                                 string effectType, int value, int duration)
    {
        SendRPC("ReceivePassiveEffect", targetCardKindStr, targetCardID, zoneName, zoneIndex, effectType, value, duration);
    }

    [PunRPC]
    private void ReceivePassiveEffect(string targetCardKindStr, int targetCardID, string zoneName, int zoneIndex,
                                     string effectType, int value, int duration)
    {
        ApplyReceivedCardEffect(targetCardKindStr, targetCardID, zoneName, zoneIndex, "パッシブ効果受信", targetCard =>
        {
            return ExecutePassiveEffectOnOpponent(targetCard, effectType, value, duration);
        });
    }

    private void ApplyReceivedCardEffect(string targetCardKindStr, int targetCardID, string zoneName, int zoneIndex, string errorLabel, System.Func<CardController, Task> apply)
    {
        try
        {
            CardController targetCard = CardAddress.Resolve(targetCardKindStr, targetCardID, zoneName, zoneIndex);
            if (targetCard != null)
                _ = apply(targetCard);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] {errorLabel}でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 相手側でパッシブ効果を実行
    /// </summary>
    private Task ExecutePassiveEffectOnOpponent(CardController targetCard, string effectType, int value, int duration)
    {
        try
        {
            BeginOperationBlock();
            // パッシブ効果の実装（効果タイプに応じて処理を分岐）
            // ここでは基本的な実装例を示します
            var passiveEffect = new ActivePassiveEffect(null, duration);
            targetCard.AddPassiveEffect(passiveEffect);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 相手側パッシブ効果実行でエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
        }
        return Task.CompletedTask;
    }

    #endregion
}
