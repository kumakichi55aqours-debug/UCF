using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 異次元一覧と、スキルによる異次元送りの同期。
/// 通常の撃破や墓地送りとは別で、送られたカードは異次元に残る。
/// </summary>
public partial class OnlineSyncManager
{
    public List<DeckCard> GetOpponentDimension() => opponentDimension;

    public void SendDimensionInfoToOpponent()
    {
        var battle = BattleManager.Instance;
        if (!IsOnlineMode || battle == null || battle.pDimensionZone == null)
            return;

        try
        {
            var info = new List<object>();
            // 空のゾーンは非表示でも、残っているカードは送る。
            CardController[] cards = battle.pDimensionZone.GetComponentsInChildren<CardController>(true);
            foreach (var card in cards)
            {
                if (card == null)
                    continue;

                info.Add(card.cardKind.ToString());
                info.Add(card.cardID);
                info.Add(card.cost);
            }

            SendRPC("ReceiveOpponentDimensionInfo", new object[] { info.ToArray() });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"異次元情報送信でエラー: {ex.Message}");
        }
    }

    [PunRPC]
    private void ReceiveOpponentDimensionInfo(object[] dimensionInfo)
    {
        try
        {
            opponentDimension.Clear();
            ReadCardTriples(dimensionInfo, (first, second, third) =>
            {
                if (!(first is string cardKindStr) || !(second is int cardID) || !(third is int cost))
                    return;

                AddParsedDeckCard(opponentDimension, cardKindStr, cardID, cost);
            });

            BattleManager.Instance.UpdateEnemyDimensionUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"相手の異次元情報受信でエラー: {ex.Message}");
        }
    }

    public void SyncCardToDimension(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        SendAnimatedRpc("ReceiveCardToDimension", cardKindStr, cardID, zoneName, zoneIndex);
    }

    public void SyncAreaToDimension(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        if (!HasCardAddresses(cardKinds, cardIds, zoneNames, indices))
            return;

        SendAnimatedRpc("ReceiveAreaToDimension", cardKinds, cardIds, zoneNames, indices);
    }

    public void SyncDeckToDimension(string cardKindStr, int cardID)
    {
        SendAnimatedRpc("ReceiveDeckToDimension", cardKindStr, cardID);
    }

    public void RememberOpponentDimensionCard(CardController card)
    {
        if (card == null)
            return;

        var entry = new DeckCard(card.cardKind, card.cardID, 1);
        entry.PopulateDerivedData();
        opponentDimension.Add(entry);
    }

    /// <summary>
    /// 相手の墓地・異次元にいる表示カードが、別の場所へ移る直前に一覧から外す。
    /// </summary>
    public void NoteCardLeavingOpponentPile(CardController card)
    {
        var battle = BattleManager.Instance;
        Transform zone = card != null ? card.transform.parent : null;
        if (battle == null || zone == null)
            return;

        if (zone == battle.eDimensionZone)
            RemoveCardFromOpponentDimension(card.cardKind, card.cardID, battle.GetCardIndexInZone(card, zone));
        else if (zone == battle.eGraveZone)
            RemoveCardFromOpponentGraveyard(card.cardKind, card.cardID, battle.GetCardIndexInZone(card, zone));
    }

    [PunRPC]
    private void ReceiveCardToDimension(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        ReceiveAnimatedCardAction(cardKindStr, cardID, zoneName, zoneIndex, "異次元送り受信でエラー", card =>
        {
            _ = MoveReceivedCardToDimension(card, true);
        });
    }

    [PunRPC]
    private void ReceiveAreaToDimension(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        try
        {
            _ = MoveReceivedCardsToDimension(CardAddress.ResolveAll(cardKinds, cardIds, zoneNames, indices));
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 複数異次元送り受信でエラー: {ex.Message}");
            SendOpponentAnimationComplete();
        }
    }

    [PunRPC]
    private void ReceiveDeckToDimension(string cardKindStr, int cardID)
    {
        if (!System.Enum.TryParse(cardKindStr, out CardKind cardKind))
        {
            SendOpponentAnimationComplete();
            return;
        }

        _ = ExecuteDeckToDimension(cardKind, cardID);
    }

    async Task MoveReceivedCardsToDimension(List<CardController> cards)
    {
        try
        {
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    if (card == null)
                        continue;
                    await MoveReceivedCardToDimension(card, false);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 複数異次元送りでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    async Task MoveReceivedCardToDimension(CardController card, bool notifyComplete)
    {
        if (card != null && card.トークン)
        {
            try
            {
                TokenExit.VanishLocal(card);
            }
            finally
            {
                if (notifyComplete)
                    SendOpponentAnimationComplete();
            }
            return;
        }

        try
        {
            var battle = BattleManager.Instance;
            if (card == null || battle == null || CardAnimationManager.Instance == null)
                return;

            NoteCardLeavingOpponentPile(card);
            ResetCardStatusForGrave(card);
            Transform destination = card.isMyCard ? battle.pDimensionZone : battle.eDimensionZone;
            RectTransform from = card.transform.parent as RectTransform;
            bool fromOwnHand = from != null && from == battle.pHandZone;
            await CardAnimationManager.Instance.AnimateRelocate(from, destination as RectTransform, card);
            if (card == null)
                return;

            await RetireGrantSources(card);

            card.UpdateDisplay();
            if (fromOwnHand)
                SendDetailedHandInfoToOpponent();
            if (!card.isMyCard)
            {
                RememberOpponentDimensionCard(card);
                battle.UpdateEnemyDimensionUI();
            }

            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 異次元送りでエラー: {ex.Message}");
        }
        finally
        {
            if (notifyComplete)
                SendOpponentAnimationComplete();
        }
    }

    async Task ExecuteDeckToDimension(CardKind cardKind, int cardID)
    {
        CardController tempCard = null;
        try
        {
            var battle = BattleManager.Instance;
            Transform deckZone = battle != null ? battle.eDeckZone : null;
            Transform dimensionZone = battle != null ? battle.eDimensionZone : null;
            if (deckZone == null || dimensionZone == null)
                return;

            RemoveCardFromOpponentDeck(cardKind, cardID, refreshUi: false);
            tempCard = battle.CreateCard(cardKind, cardID, deckZone);
            if (tempCard == null)
            {
                battle.UpdateDeckUI();
                return;
            }

            if (tempCard.トークン)
            {
                Destroy(tempCard.gameObject);
                tempCard = null;
                battle.UpdateDeckUI();
                return;
            }

            if (battle.mainCanvas != null)
                tempCard.transform.SetParent(battle.mainCanvas.transform, true);
            battle.UpdateDeckUI();
            await CardAnimationManager.Instance.AnimateRelocate(
                deckZone as RectTransform,
                dimensionZone as RectTransform,
                tempCard);

            var entry = new DeckCard(cardKind, cardID, 1);
            entry.PopulateDerivedData();
            opponentDimension.Add(entry);
            battle.UpdateEnemyDimensionUI();
            tempCard = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteDeckToDimensionでエラー: {ex.Message}");
        }
        finally
        {
            if (tempCard != null)
                Destroy(tempCard.gameObject);
            if (BattleManager.Instance != null)
                BattleManager.Instance.UpdateDeckUI();
            SendOpponentAnimationComplete();
        }
    }

    void RemoveCardFromOpponentDimension(CardKind cardKind, int cardID, int index = -1)
        => RemoveFromOpponentPile(opponentDimension, cardKind, cardID, index);
}
