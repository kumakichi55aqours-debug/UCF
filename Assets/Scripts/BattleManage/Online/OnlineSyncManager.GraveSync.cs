using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
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
        try
        {
            await PlayDefeat(card);
            ResetCardStatusForGrave(card);
            card.UpdateDisplay();
            await AnimateToGrave(card, BattleManager.Instance.eGraveZone);

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
        try
        {
            await PlayDefeat(card);
            ResetCurHpToMax(card);
            card.UpdateDisplay();
            card.RemoveAllPassiveEffects();
            card.ClearStatusAilments(BattleManager.Instance != null && BattleManager.Instance.IsStunResolvePending(card));

            Transform graveZone = card.isMyCard ? BattleManager.Instance.pGraveZone : BattleManager.Instance.eGraveZone;
            await AnimateToGrave(card, graveZone);
            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 自分カードの墓地送りでエラー: {ex.Message}");
        }
        finally
        {
            if (notifyComplete)
                SendOpponentAnimationComplete();
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
            SendOpponentAnimationComplete();
        }
    }

    private void ResetCardStatusForGrave(CardController card)
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
        card.ResetMoveElements();
        card.RecalculateModifiedPowers();
    }

    static async Task PlayDefeat(CardController card)
    {
        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.PlayDefeatExplosion(card);
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

    void RememberEnemyGraveCard(CardController card)
    {
        var graveCard = new DeckCard(card.cardKind, card.cardID, 1);
        graveCard.PopulateDerivedData();
        opponentGrave.Add(graveCard);
    }
}
