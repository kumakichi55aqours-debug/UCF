using System;
using System.Threading.Tasks;
using UnityEngine;

public partial class BattleManager
{
    #region 召喚関連

    // 統一的なカード召喚処理導入
    public async Task SummonCard(CardController card, Transform targetZone, bool playVoice = true, bool deductCost = true, SummonSource? summonSource = null, bool specialSummon = false)
    {
        // コスト消費
        if (deductCost)
        {
            int pay = card.GetModifiedCost();
            pResources.Cost.TakeCost(pay);
            if (IsOnlineMode) OnlineSyncManager.Instance.SyncOpponentCostConsumption(pay);
        }
        
        // 召喚元の決定（デフォルトは手札）
        SummonSource actualSummonSource = summonSource ?? SummonSource.手札;
        bool leftOwnDimension = card != null && card.transform.parent == pDimensionZone;
        OnlineSyncManager.Instance?.NoteCardLeavingOpponentPile(card);
        
        // オンライン対戦時はRPCで同期
        if (IsOnlineMode)
        {
            // 相手側の視点でのゾーン名を取得
            string opponentZoneName = GetOpponentZoneName(targetZone);
            
            // 相手に召喚情報を送信
            int sourceIndex = GetCardIndexInZone(card, card.transform.parent);
            OnlineSyncManager.Instance.SyncSummonCard(card.cardKind.ToString(), card.cardID, sourceIndex, opponentZoneName, actualSummonSource, specialSummon);
            
            // 自分のカードは直接召喚処理を実行
            await SummonRoutine(card, targetZone, playVoice, actualSummonSource, specialSummon: specialSummon);
            if (leftOwnDimension)
                OnlineSyncManager.Instance?.SendDimensionInfoToOpponent();
            return;
        }

        await SummonRoutine(card, targetZone, playVoice, actualSummonSource, specialSummon: specialSummon);
    }

    /// <summary>
    /// 手札に残っている作戦を、今のチェーンへ出すときに払えるか。
    /// 支払いコストは通常の登場と同じ補正を使う。
    /// </summary>
    public bool CanPayHandTactics(CardController card)
    {
        if (card == null || card.cardKind != CardKind.作戦)
            return false;
        if (UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card))
            return false;

        var resources = card.isPlayerCard ? pResources : eResources;
        if (resources == null || resources.Cost == null)
            return false;
        return card.GetModifiedCost() <= resources.Cost.currentCost;
    }

    /// <summary>
    /// 手札にある作戦で、チェーン中の発動として場に出す対象か。
    /// オンラインの相手手札は、持ち主の側で場に出す。
    /// </summary>
    public bool IsHandTacticsAwaitingAppearance(CardController card)
    {
        if (card == null || card.cardKind != CardKind.作戦)
            return false;
        if (card.GetCurrentActiveLocation() != ActiveLocation.手札)
            return false;
        if (IsOnlineMode && !card.isMyCard)
            return false;

        return card.isMyCard || card.transform.parent == eHandZone;
    }

    /// <summary>
    /// 発動すると決めた手札の作戦を、作戦ゾーンへ出してコストを払う。
    /// 自分のカードは自分の作戦ゾーン、CPUのカードは相手の作戦ゾーンへ出す。
    /// カード使用時のチェーンは開かない。場に出す対象でなければ何もせず成功とする。
    /// 払えないときは false。
    /// </summary>
    public async Task<bool> TryCommitHandTacticsAppearance(CardController card)
    {
        if (!IsHandTacticsAwaitingAppearance(card))
            return true;
        if (!CanCommitHandTactics(card))
            return false;

        bool playerCard = card.isPlayerCard;
        var resources = playerCard ? pResources : eResources;
        var handZone = playerCard ? pHandZone : eHandZone;
        var tacticsZone = playerCard ? pTacticsZone : eTacticsZone;

        int pay = card.GetModifiedCost();
        int handIndex = GetCardIndexInZone(card, handZone);
        if (pay > 0 && resources != null && resources.Cost != null)
            resources.Cost.TakeCost(pay);

        DeckCard removedFromEnemyHand = null;
        if (!playerCard)
            removedFromEnemyHand = TakeEnemyHandEntry(card, handIndex);

        if (playerCard && IsOnlineMode && OnlineSyncManager.Instance != null)
        {
            if (pay > 0)
                OnlineSyncManager.Instance.SyncOpponentCostConsumption(pay);
            OnlineSyncManager.Instance.SyncAppearTacticsDuringChain(card.cardKind.ToString(), card.cardID, handIndex);
        }

        await SummonRoutine(card, tacticsZone, false, SummonSource.手札, openUseChain: false);
        if (card == null || card.transform.parent != tacticsZone)
        {
            if (pay > 0 && resources != null && resources.Cost != null)
                resources.Cost.AddCost(pay);
            if (removedFromEnemyHand != null && card != null && card.transform.parent == handZone)
            {
                int index = handIndex < 0 ? enemyHand.Count : Mathf.Clamp(handIndex, 0, enemyHand.Count);
                enemyHand.Insert(index, removedFromEnemyHand);
            }
            return false;
        }

        SkillChainManager.Instance?.RegisterChainTactics(card);
        return true;
    }

    /// <summary>
    /// CPU手札の実体から、場に出す1枚を外す。表示だけ動かすと、次の手札更新で戻る。
    /// </summary>
    DeckCard TakeEnemyHandEntry(CardController card, int handIndex)
    {
        if (card == null || enemyHand == null)
            return null;

        if (handIndex >= 0 && handIndex < enemyHand.Count)
        {
            DeckCard atIndex = enemyHand[handIndex];
            if (atIndex != null && atIndex.カード種類 == card.cardKind && atIndex.カードID == card.cardID)
            {
                enemyHand.RemoveAt(handIndex);
                return atIndex;
            }
        }

        for (int i = 0; i < enemyHand.Count; i++)
        {
            DeckCard deck = enemyHand[i];
            if (deck != null && deck.カード種類 == card.cardKind && deck.カードID == card.cardID)
            {
                enemyHand.RemoveAt(i);
                return deck;
            }
        }

        return null;
    }

    /// <summary>
    /// 召喚元に応じたゾーンを取得
    /// </summary>
    private RectTransform GetSummonSourceZone(SummonSource summonSource, bool isPlayerCard)
    {
        switch (summonSource)
        {
            case SummonSource.手札:
                return isPlayerCard ? pHandZone.GetComponent<RectTransform>() : eHandZone.GetComponent<RectTransform>();
            case SummonSource.墓地:
                return isPlayerCard ? pDeckZone.GetComponent<RectTransform>() : eDeckZone.GetComponent<RectTransform>();
            case SummonSource.デッキ:
                return isPlayerCard ? pGraveZone.GetComponent<RectTransform>() : eGraveZone.GetComponent<RectTransform>();
            case SummonSource.異次元:
                return isPlayerCard ? pDimensionZone.GetComponent<RectTransform>() : eDimensionZone.GetComponent<RectTransform>();
            default:
                return null;
        }
    }

    /// <summary>
    /// 統一的な召喚ルーチン
    /// </summary>
    public async Task SummonRoutine(CardController card, Transform targetZone, bool playVoice = true, SummonSource? summonSource = null, bool openUseChain = true, bool specialSummon = false)
    {
        try
        {
            bool ownerIsPlayer = card.isPlayerCard;
            CardZoneType fromLocation = summonSource.HasValue
                ? CardController.ZoneFromSummonSource(summonSource.Value, ownerIsPlayer)
                : CardZoneType.None;
            if (fromLocation == CardZoneType.None)
                fromLocation = CardController.ZoneTypeFromTransform(card.transform.parent);

            RectTransform fromZone;
            if (summonSource.HasValue)
            {
                // 指定された召喚元を使用
                fromZone = GetSummonSourceZone(summonSource.Value, ownerIsPlayer);
                if (fromZone != null) card.transform.position = fromZone.position;
            }
            else
            {
                // 手札からの召喚を前提（従来の動作）
                fromZone = card.transform.parent as RectTransform;
            }
            
            // 召喚先のゾーン指定
            RectTransform targetSlot = targetZone as RectTransform;

            // 相手手札のバトラーと作戦は裏面のまま飛んでいる。出す前に表面にする。
            if (card.cardKind == CardKind.バトラー || card.cardKind == CardKind.作戦)
            {
                var face = card.GetComponent<BattleCardView>();
                if (face != null && face.IsBack)
                    card.UpdateDisplay();
            }

            // 召喚ボイス再生
            if (playVoice && card.model is BattlerModel battler && battler.summonVoice != null)
                AudioManager.Instance.PlaySE(battler.summonVoice, 1f);

            // バトラーは移動と同時にカットインを出す。登場時スキルは両方が終わってから。
            Task summonCutIn = card.cardKind == CardKind.バトラー
                ? CardAnimationManager.Instance.PlayBattlerSummonCutIn(card)
                : Task.CompletedTask;

            // アニメ再生
            await CardAnimationManager.Instance.AnimateSummon(fromZone, targetSlot, card);
            await summonCutIn;
            if (card == null || card.transform == null)
                return;

            // 移動後のUI更新
            card.GetComponent<BattleCardView>().UpdateCanUseMovePanel();

            // ゾーンに着いてから、登場時スキルより先に永続効果を付け直す
            PersistentEffectManager.Instance?.RecalculateImmediately();

            if (card.cardKind == CardKind.作戦)
            {
                // チェーン中の手札発動は、その作戦自身のカード使用時を開かない。
                // 他のカードの登場反応だけは、着地後に回す。
                if (openUseChain)
                    EventBus.Publish(new UseCardFromHandEvent(card, fromLocation, specialSummon));
                else
                    EventBus.Publish(new SkillTriggerEvent(SkillTrigger.他カードが登場した時, card, card, null, fromLocation)
                    {
                        SpecialSummon = specialSummon
                    });
            }
            // 召喚完了後にhasUsedMoveを明示的にfalseにリセット
            else
            {
                await Task.Delay(400);
                EventBus.Publish(new SummonCompletedEvent(card, specialSummon, fromLocation));
                if (card != null && card.cardKind == CardKind.バトラー)
                    card.hasUsedMove = false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"SummonRoutine でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// バトラー召喚
    /// </summary>
    public async Task SummonBattler(CardController card)
    {
        if (CannotNormalSummonSkill.Blocks(card))
            return;
        if (card.cardKind == CardKind.バトラー)
            await SummonCard(card, pBattlerZone, true, true);
        if (card.cardKind == CardKind.フィールド)
            await SummonCard(card, pFieldZone, true, true);
    }

    /// <summary>
    /// タクティクス使用
    /// </summary>
    public async Task UseTactics(CardController card)
    {
        if (!TacticsZoneHasRoom(true))
        {
            MessageManager.Instance?.ShowMessage("作戦ゾーンはいっぱいです");
            return;
        }

        await SummonCard(card, pTacticsZone, false, true);
    }

    public async Task UseTacticsComplete(CardController card)
    {
        await AnimateTacticsToGrave(card, suppressSkillTrigger: false);
    }
    #endregion
}
