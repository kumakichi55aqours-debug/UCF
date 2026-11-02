using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class BattleManager
{
    public const int MaxTacticsZoneCount = 3;

    readonly HashSet<int> retiringEnhancements = new HashSet<int>();

    public bool IsBattlerZone(Transform zone)
    {
        return zone != null && (zone == pBattlerZone || zone == eBattlerZone);
    }

    public bool IsTacticsZone(Transform zone)
    {
        return zone != null && (zone == pTacticsZone || zone == eTacticsZone);
    }

    public bool CanCommitHandTactics(CardController card)
    {
        return CanPayHandTactics(card) && card != null && TacticsZoneHasRoom(card.isPlayerCard);
    }

    public bool TacticsZoneHasRoom(bool playerZone)
    {
        Transform zone = playerZone ? pTacticsZone : eTacticsZone;
        return zone != null && zone.childCount < MaxTacticsZoneCount;
    }

    /// <summary>
    /// 強化作戦は、対象へ付与が残っているあいだ作戦ゾーンに置く。
    /// </summary>
    public bool KeepsTacticsOnField(CardController card)
    {
        return card != null
            && card.cardKind == CardKind.作戦
            && card.type == CardTypeList.強化
            && HasLivingGrant(card);
    }

    public bool HasLivingGrant(CardController source)
    {
        if (source == null)
            return false;

        foreach (CardController card in EnumerateZoneCards())
        {
            if (card?.passiveEffects == null)
                continue;
            foreach (ActivePassiveEffect effect in card.passiveEffects)
            {
                if (effect != null && effect.SourceCard == source && effect.RemainingTurns != 0)
                    return true;
            }
        }

        return false;
    }

    public void ClearGrantsFrom(CardController source)
    {
        if (source == null)
            return;

        foreach (CardController card in EnumerateZoneCards())
            card?.RemovePassivesFromSource(source);
    }

    public void OnBattlerLeftZone(CardController battler, Transform leftZone)
    {
        if (!isActiveAndEnabled || !gameObject.scene.isLoaded)
            return;
        if (battler == null || battler.cardKind != CardKind.バトラー || !IsBattlerZone(leftZone))
            return;
        if (battler.passiveEffects == null)
            return;

        var sources = new List<CardController>();
        foreach (ActivePassiveEffect effect in battler.passiveEffects)
        {
            CardController source = effect?.SourceCard;
            if (source == null || source.type != CardTypeList.強化 || !IsTacticsZone(source.transform.parent))
                continue;
            if (!sources.Contains(source))
                sources.Add(source);
        }

        foreach (CardController source in sources)
            _ = RetireEnhancement(source);
    }

    public async Task RetireSpentEnhancements()
    {
        var spent = new List<CardController>();
        CollectSpentEnhancements(pTacticsZone, spent);
        CollectSpentEnhancements(eTacticsZone, spent);
        foreach (CardController card in spent)
            await RetireEnhancement(card);
    }

    public async Task RetireEnhancement(CardController card)
    {
        if (card == null || card.type != CardTypeList.強化 || !IsTacticsZone(card.transform.parent))
            return;
        if (IsOnlineMode && !card.isMyCard)
            return;
        if (!retiringEnhancements.Add(card.GetInstanceID()))
            return;

        try
        {
            ClearGrantsFrom(card);
            if (card.isMyCard || !IsOnlineMode)
                await UseTacticsComplete(card);
            else if (OnlineSyncManager.Instance != null)
                await OnlineSyncManager.Instance.ExecuteTacticsToGraveyard(card);

            if (card.isMyCard && IsOnlineMode && OnlineSyncManager.Instance != null)
                OnlineSyncManager.Instance.SyncTacticsSkillCompleted(card);
        }
        finally
        {
            retiringEnhancements.Remove(card.GetInstanceID());
        }
    }

    void CollectSpentEnhancements(Transform zone, List<CardController> spent)
    {
        if (zone == null)
            return;

        foreach (CardController card in zone.GetComponentsInChildren<CardController>())
        {
            if (card != null && card.type == CardTypeList.強化 && !HasLivingGrant(card))
                spent.Add(card);
        }
    }

    IEnumerable<CardController> EnumerateZoneCards()
    {
        Transform[] zones =
        {
            pHandZone, pBattlerZone, pTacticsZone, pFieldZone, pGraveZone, pDeckZone, pDimensionZone,
            eHandZone, eBattlerZone, eTacticsZone, eFieldZone, eGraveZone, eDeckZone, eDimensionZone
        };

        foreach (Transform zone in zones)
        {
            if (zone == null)
                continue;
            foreach (CardController card in zone.GetComponentsInChildren<CardController>())
            {
                if (card != null)
                    yield return card;
            }
        }
    }
}
