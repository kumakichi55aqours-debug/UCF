using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 条件に合うバトラー1体と、そのバトラーに付いている強化作戦を、持ち主の手札へ戻す。
/// 強化は先に戻す。バトラーが場を離れたときの墓地送りには乗せない。
/// </summary>
[System.Serializable]
[SkillDisplayName("付与ごと手札へ")]
public class ReturnWithGrantsSkill : SkillDB
{
    public CardZoneType 参照ゾーン = CardZoneType.プレイヤーBゾーン;
    public SearchCriteria 対象条件;

    public bool HasReturnCandidate(CardController source)
    {
        var context = new SkillContext(source, source, source != null ? source.transform : null);
        return FindCandidates(context).Count > 0;
    }

    public override async Task ActivateEffect(SkillContext context)
    {
        List<CardController> candidates = FindCandidates(context);
        if (candidates.Count == 0)
        {
            Debug.Log("ReturnWithGrantsSkill: 戻すバトラーがいません。");
            return;
        }

        CardController battler = await Choose(candidates);
        if (battler == null)
            return;

        var battle = BattleManager.Instance;
        List<CardController> grants = battle != null
            ? battle.EnhancementSourcesOf(battler)
            : new List<CardController>();

        bool addedToPlayer = false;
        bool addedToEnemy = false;
        bool addedRemoteEnemy = false;

        foreach (CardController grant in grants)
        {
            if (grant == null)
                continue;
            await ReturnOne(grant, result =>
            {
                addedToPlayer |= result.player;
                addedToEnemy |= result.enemy;
                addedRemoteEnemy |= result.remote;
            });
        }

        await ReturnOne(battler, result =>
        {
            addedToPlayer |= result.player;
            addedToEnemy |= result.enemy;
            addedRemoteEnemy |= result.remote;
        });

        if (addedRemoteEnemy && OnlineSyncManager.Instance != null)
            await OnlineSyncManager.Instance.RequestOpponentShuffleAddedHand();

        bool shuffleEnemyLocally = addedToEnemy && !addedRemoteEnemy;
        if ((addedToPlayer || shuffleEnemyLocally) && battle != null)
            battle.ShuffleHandAfterAddition(addedToPlayer, shuffleEnemyLocally);
    }

    List<CardController> FindCandidates(SkillContext context)
    {
        var found = new List<CardController>();
        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
        Transform zone = CardZones.Of(参照ゾーン, ownerIsLocal);
        if (zone == null)
            return found;

        foreach (CardController card in zone.GetComponentsInChildren<CardController>())
        {
            if (card != null && card.MeetsCriteria(対象条件, context))
                found.Add(card);
        }

        return found;
    }

    static async Task<CardController> Choose(List<CardController> candidates)
    {
        if (candidates.Count == 1 || SkillContext.IsOfflineCpuResolving)
            return candidates[Random.Range(0, candidates.Count)];
        if (CardSelectionUI.Instance == null)
            return candidates[0];

        var selections = await CardSelectionUI.Instance.ShowAsync(
            candidates,
            new List<DeckCard>(),
            1,
            letCpuChoose: false);
        if (selections == null || selections.Count == 0)
            return null;
        return selections[0].existingCard;
    }

    struct ReturnFlags
    {
        public bool player;
        public bool enemy;
        public bool remote;
    }

    static async Task ReturnOne(CardController card, System.Action<ReturnFlags> report)
    {
        if (card == null)
            return;

        var battle = BattleManager.Instance;
        if (card.isPlayerCard)
        {
            if (await ToHandSkill.MoveOwnExistingCardToHand(card) != null)
                report(new ReturnFlags { player = true });
            return;
        }

        if (battle != null && battle.IsOnlineMode && OnlineSyncManager.Instance != null
            && CardAddress.TryFrom(card, out CardAddress address))
        {
            await OnlineSyncManager.Instance.RequestOpponentCardToOwnHand(
                address.Kind.ToString(),
                address.Id,
                address.ZoneName,
                address.Index,
                -1);
            report(new ReturnFlags { enemy = true, remote = true });
            return;
        }

        if (battle == null || !battle.IsOnlineMode)
        {
            await ToHandSkill.MoveMirrorCardToEnemyHand(card);
            report(new ReturnFlags { enemy = true });
        }
    }
}
