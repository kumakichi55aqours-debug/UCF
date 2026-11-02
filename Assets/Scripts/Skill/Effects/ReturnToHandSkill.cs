using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
[SkillDisplayName("バウンス")]
public class ReturnToHandSkill : SkillDB
{
    [SerializeField] public TargetList 効果対象;
    [SerializeField] public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        var toReturn = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context);
        if (toReturn == null || toReturn.Count == 0)
            return;

        bool addedToPlayer = false;
        bool addedToEnemy = false;
        bool addedRemoteEnemy = false;
        var battle = BattleManager.Instance;

        foreach (var card in toReturn)
        {
            if (card == null)
                continue;

            if (card.トークン)
            {
                TokenExit.VanishSynced(card);
                continue;
            }

            if (card.isPlayerCard)
            {
                if (await ToHandSkill.MoveOwnExistingCardToHand(card) != null)
                    addedToPlayer = true;
            }
            else if (battle != null && battle.IsOnlineMode && OnlineSyncManager.Instance != null
                && CardAddress.TryFrom(card, out CardAddress address))
            {
                await OnlineSyncManager.Instance.RequestOpponentCardToOwnHand(
                    address.Kind.ToString(),
                    address.Id,
                    address.ZoneName,
                    address.Index,
                    -1);
                addedToEnemy = true;
                addedRemoteEnemy = true;
            }
            else if (battle == null || !battle.IsOnlineMode)
            {
                await ToHandSkill.MoveMirrorCardToEnemyHand(card);
                addedToEnemy = true;
            }
            else
            {
                Debug.LogWarning($"[バウンス] 相手カードの住所が取れないため、手札へ移せません: {card.cardName}");
            }
        }

        if (addedRemoteEnemy && OnlineSyncManager.Instance != null)
            await OnlineSyncManager.Instance.RequestOpponentShuffleAddedHand();

        bool shuffleEnemyLocally = addedToEnemy && !addedRemoteEnemy;
        if ((addedToPlayer || shuffleEnemyLocally) && BattleManager.Instance != null)
            BattleManager.Instance.ShuffleHandAfterAddition(addedToPlayer, shuffleEnemyLocally);
    }
}
