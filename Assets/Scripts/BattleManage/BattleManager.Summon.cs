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
    /// 自分の手札にある作戦で、チェーン中の発動として場に出す対象か。
    /// </summary>
    public bool IsHandTacticsAwaitingAppearance(CardController card)
    {
        return card != null
            && card.isMyCard
            && card.cardKind == CardKind.作戦
            && card.GetCurrentActiveLocation() == ActiveLocation.手札;
    }

    /// <summary>
    /// 発動すると決めた手札の作戦を、作戦ゾーンへ出してコストを払う。
    /// カード使用時のチェーンは開かない。場に出す対象でなければ何もせず成功とする。
    /// 払えないときは false。
    /// </summary>
    public async Task<bool> TryCommitHandTacticsAppearance(CardController card)
    {
        if (!IsHandTacticsAwaitingAppearance(card))
            return true;
        if (!CanPayHandTactics(card))
            return false;

        int pay = card.GetModifiedCost();
        int handIndex = GetCardIndexInZone(card, pHandZone);
        if (pay > 0)
            pResources.Cost.TakeCost(pay);

        if (IsOnlineMode && OnlineSyncManager.Instance != null)
        {
            if (pay > 0)
                OnlineSyncManager.Instance.SyncOpponentCostConsumption(pay);
            OnlineSyncManager.Instance.SyncAppearTacticsDuringChain(card.cardKind.ToString(), card.cardID, handIndex);
        }

        await SummonRoutine(card, pTacticsZone, false, SummonSource.手札, openUseChain: false);
        if (card == null || card.transform.parent != pTacticsZone)
        {
            if (pay > 0 && pResources != null && pResources.Cost != null)
                pResources.Cost.AddCost(pay);
            return false;
        }

        SkillChainManager.Instance?.RegisterChainTactics(card);
        return true;
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
            RectTransform fromZone;
            if (summonSource.HasValue)
            {
                // 指定された召喚元を使用
                fromZone = GetSummonSourceZone(summonSource.Value, card.isPlayerCard);
                if (fromZone != null) card.transform.position = fromZone.position;
            }
            else
            {
                // 手札からの召喚を前提（従来の動作）
                fromZone = card.transform.parent as RectTransform;
            }
            
            // 召喚先のゾーン指定
            RectTransform targetSlot = targetZone as RectTransform;

            // 相手手札のバトラーは裏面のまま飛んでいる。オンラインと同じく、出す前に表面にする。
            if (card.cardKind == CardKind.バトラー)
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
                // 相手カード用。チェーン中の手札発動は、カード使用時の窓を開かない。
                if (openUseChain && !card.isMyCard)
                {
                    EventBus.Publish(new UseCardFromHandEvent(card));
                }
            }
            // 召喚完了後にhasUsedMoveを明示的にfalseにリセット
            else
            {
                await Task.Delay(300);
                EventBus.Publish(new SummonCompletedEvent(card, specialSummon));
                if (card.cardKind == CardKind.バトラー && card.isMyCard)
                {
                    card.hasUsedMove = false;
                }
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
        await SummonCard(card, pTacticsZone, false, true);
        // 自分カード用
        EventBus.Publish(new UseCardFromHandEvent(card));
    }

    public async Task UseTacticsComplete(CardController card)
    {
        var tacticsZone = card.isMyCard ? pTacticsZone : eTacticsZone;
        var graveZone = card.isMyCard ? pGraveZone : eGraveZone;

        RectTransform tacticsSlot = tacticsZone as RectTransform;
        RectTransform graveSlot = graveZone as RectTransform;

        await CardAnimationManager.Instance.AnimateDiscard(tacticsSlot, graveSlot, card);

        card.UpdateDisplay();
        PersistentEffectManager.Instance?.RecalculateImmediately();
    }
    #endregion
}
