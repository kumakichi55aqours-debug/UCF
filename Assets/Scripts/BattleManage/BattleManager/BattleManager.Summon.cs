using System;
using System.Threading.Tasks;
using UnityEngine;

public partial class BattleManager
{
    #region 召喚関連

    public async Task SummonCard(CardController card, Transform targetZone, bool playVoice = true, bool deductCost = true, SummonSource? summonSource = null, bool specialSummon = false)
    {
        if (deductCost)
        {
            int pay = card.GetModifiedCost();
            pResources.Cost.TakeCost(pay);
            if (IsOnlineMode) OnlineSyncManager.Instance.SyncOpponentCostChange(pay);
        }

        SummonSource actualSummonSource = summonSource ?? SummonSource.手札;
        bool leftOwnDimension = card != null && card.transform.parent == pDimensionZone;
        OnlineSyncManager.Instance?.NoteCardLeavingOpponentPile(card);

        if (IsOnlineMode)
        {
            string opponentZoneName = GetOpponentZoneName(targetZone);
            int sourceIndex = GetCardIndexInZone(card, card.transform.parent);
            OnlineSyncManager.Instance.SyncSummonCard(card.cardKind.ToString(), card.cardID, sourceIndex, opponentZoneName, actualSummonSource, specialSummon);
        }

        await SummonRoutine(card, targetZone, playVoice, actualSummonSource, specialSummon: specialSummon);

        if (IsOnlineMode && leftOwnDimension)
            OnlineSyncManager.Instance?.SendDimensionInfoToOpponent();
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
        return resources?.Cost != null && card.GetModifiedCost() <= resources.Cost.currentCost;
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
    /// すでにチェーン上の登場リンクなので、新しい作戦登場窓は開かない。
    /// 場に出す対象でなければ何もせず成功とする。払えないときは false。
    /// </summary>
    public async Task<bool> TryCommitHandTacticsAppearance(CardController card)
    {
        if (!IsHandTacticsAwaitingAppearance(card))
            return true;
        if (!CanCommitHandTactics(card))
            return false;

        bool playerCard = card.isPlayerCard;
        var cost = (playerCard ? pResources : eResources)?.Cost;
        var handZone = playerCard ? pHandZone : eHandZone;
        var tacticsZone = playerCard ? pTacticsZone : eTacticsZone;

        int pay = card.GetModifiedCost();
        int handIndex = GetCardIndexInZone(card, handZone);
        if (pay > 0 && cost != null)
            cost.TakeCost(pay);

        DeckCard removedFromEnemyHand = playerCard ? null : TakeEnemyHandEntry(card, handIndex);

        if (playerCard && IsOnlineMode && OnlineSyncManager.Instance != null)
        {
            if (pay > 0)
                OnlineSyncManager.Instance.SyncOpponentCostChange(pay);
            OnlineSyncManager.Instance.SyncAppearTacticsDuringChain(card.cardKind.ToString(), card.cardID, handIndex);
        }

        // openAppearanceChain: false → 自身の登場時は再開せず、他カード反応とチェーン終了後の墓地送りだけ残す
        await SummonRoutine(card, tacticsZone, false, SummonSource.手札, openAppearanceChain: false);
        if (card == null || card.transform.parent != tacticsZone)
        {
            if (pay > 0 && cost != null)
                cost.AddCost(pay);
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
    /// オフラインCPUの手札から場へ出すとき、表示と同じ1枚を enemyHand から外す。
    /// リストに残ったままだと、手札へ戻したときに作り直して2枚になる。
    /// </summary>
    public DeckCard ConsumeOfflineEnemyHand(CardController card)
    {
        if (IsOnlineMode || card == null || card.isPlayerCard || eHandZone == null)
            return null;
        if (card.transform.parent != eHandZone)
            return null;

        return TakeEnemyHandEntry(card, GetCardIndexInZone(card, eHandZone));
    }

    /// <summary>
    /// CPU手札の実体から、場に出す1枚を外す。表示だけ動かすと、次の手札更新で戻る。
    /// </summary>
    DeckCard TakeEnemyHandEntry(CardController card, int handIndex)
    {
        if (card == null || enemyHand == null)
            return null;

        bool Matches(DeckCard deck) =>
            deck != null && deck.カード種類 == card.cardKind && deck.カードID == card.cardID;

        if (handIndex >= 0 && handIndex < enemyHand.Count && Matches(enemyHand[handIndex]))
        {
            DeckCard atIndex = enemyHand[handIndex];
            enemyHand.RemoveAt(handIndex);
            return atIndex;
        }

        for (int i = 0; i < enemyHand.Count; i++)
        {
            if (!Matches(enemyHand[i]))
                continue;

            DeckCard deck = enemyHand[i];
            enemyHand.RemoveAt(i);
            return deck;
        }

        return null;
    }

    /// <summary>
    /// 召喚元に応じたゾーンを取得。
    /// </summary>
    private RectTransform GetSummonSourceZone(SummonSource summonSource, bool isPlayerCard)
    {
        Transform zone = summonSource switch
        {
            SummonSource.手札 => isPlayerCard ? pHandZone : eHandZone,
            SummonSource.墓地 => isPlayerCard ? pGraveZone : eGraveZone,
            SummonSource.デッキ => isPlayerCard ? pDeckZone : eDeckZone,
            SummonSource.異次元 => isPlayerCard ? pDimensionZone : eDimensionZone,
            _ => null
        };
        return zone != null ? zone.GetComponent<RectTransform>() : null;
    }

    /// <summary>
    /// 統一的な召喚ルーチン。
    /// バトラー／フィールドは SummonCompletedEvent → 登場時。
    /// 作戦は UseCardFromHandEvent → カード使用時（作戦登場の内部トリガー。スキルは登場時）。
    /// openAppearanceChain が false のときは、すでにチェーン上の作戦登場なので自身の窓は開かず、
    /// 他のカードが登場した時だけ通す。
    /// </summary>
    public async Task SummonRoutine(CardController card, Transform targetZone, bool playVoice = true, SummonSource? summonSource = null, bool openAppearanceChain = true, bool specialSummon = false)
    {
        bool ownerIsPlayer = card.isPlayerCard;
        CardZoneType fromLocation = summonSource.HasValue
            ? CardController.ZoneFromSummonSource(summonSource.Value, ownerIsPlayer)
            : CardZoneType.None;
        if (fromLocation == CardZoneType.None)
            fromLocation = CardController.ZoneTypeFromTransform(card.transform.parent);

        RectTransform fromZone = summonSource.HasValue
            ? GetSummonSourceZone(summonSource.Value, ownerIsPlayer)
            : card.transform.parent as RectTransform;
        if (summonSource.HasValue && fromZone != null)
            card.transform.position = fromZone.position;

        RectTransform targetSlot = targetZone as RectTransform;

        // 相手手札のバトラーと作戦は裏面のまま飛んでいる。出す前に表面にする。
        if (card.cardKind == CardKind.バトラー || card.cardKind == CardKind.作戦)
        {
            var face = card.GetComponent<BattleCardView>();
            if (face != null && face.IsBack)
                card.UpdateDisplay();
        }

        if (playVoice && card.model is BattlerModel battler && battler.summonVoice != null)
            AudioManager.Instance.PlaySE(battler.summonVoice, 1f);

        // バトラーは移動と同時にカットインを出す。登場時スキルは両方が終わってから。
        Task summonCutIn = card.cardKind == CardKind.バトラー
            ? CardAnimationManager.Instance.PlayBattlerSummonCutIn(card)
            : Task.CompletedTask;

        await CardAnimationManager.Instance.AnimateSummon(fromZone, targetSlot, card);
        await summonCutIn;

        if (card == null || card.transform == null)
            return;

        // ゾーンに着いてから、登場時スキルより先に永続効果を付け直す
        PersistentEffectManager.Instance?.RecalculateImmediately();

        if (card.cardKind != CardKind.作戦)
        {
            await Task.Delay(250);
            EventBus.Publish(new SummonCompletedEvent(card, specialSummon, fromLocation));

            if (card != null && card.cardKind == CardKind.バトラー)
            {
                card.hasUsedMove = false;
                card.GetComponent<BattleCardView>().UpdateCanUseMovePanel();
            }
            return;
        }

        // 作戦登場は1系統。強化でも SummonCompletedEvent は出さない（登場時との二重発火を防ぐ）。
        if (openAppearanceChain)
        {
            EventBus.Publish(new UseCardFromHandEvent(card, fromLocation, specialSummon));
        }
        else
        {
            EventBus.Publish(new SkillTriggerEvent(SkillTrigger.他のカードが登場した時, card, card, null, fromLocation)
            {
                SpecialSummon = specialSummon
            });
        }
    }

    /// <summary>
    /// 手札からの通常登場。バトラーはBゾーン、フィールドはFゾーンへ出す。
    /// </summary>
    public async Task SummonBattler(CardController card)
    {
        if (CannotNormalSummonSkill.Blocks(card))
            return;

        Transform zone = card.cardKind switch
        {
            CardKind.バトラー => pBattlerZone,
            CardKind.フィールド => pFieldZone,
            _ => null
        };
        if (zone != null)
            await SummonCard(card, zone, true, true);
    }

    /// <summary>
    /// 手札からの作戦登場。Tゾーンへ出し、登場時スキル解決後に強化以外は墓地へ送る。
    /// </summary>
    public async Task UseTactics(CardController card)
    {
        if (!TacticsZoneHasRoom(true)) return;

        await SummonCard(card, pTacticsZone, false, true);
    }

    /// <summary>
    /// 作戦登場チェーン完了後の墓地送り。強化で付与が残っているときは呼ばない側で抑止する。
    /// </summary>
    public async Task UseTacticsComplete(CardController card)
    {
        await AnimateTacticsToGrave(card, suppressSkillTrigger: false);
    }
    #endregion
}
