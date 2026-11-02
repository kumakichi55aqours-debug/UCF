using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// CardDBList/トークン のバトラーを、デッキを減らさずにBゾーンへ出す。
/// コストは払わない。Bゾーンが5体のときは出さない。
/// </summary>
[System.Serializable]
[SkillDisplayName("トークン")]
public class TokenSummonSkill : SkillDB
{
    const int BattlerZoneLimit = 5;

    [Tooltip("CardDBList/トークン にあるカードID。バトラーと同じIDは使わない。")]
    public int カードID;

    public override async Task ActivateEffect(SkillContext context)
    {
        BattleManager battle = BattleManager.Instance;
        if (battle == null || カードID <= 0)
            return;

        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
        Transform zone = ownerIsLocal ? battle.pBattlerZone : battle.eBattlerZone;
        if (zone == null || zone.childCount >= BattlerZoneLimit)
        {
            Debug.Log("TokenSummonSkill: Bゾーンに空きがないためトークンを出しません。");
            return;
        }

        Transform parent = battle.mainCanvas != null ? battle.mainCanvas.transform : zone;
        CardController card = battle.CreateCard(CardKind.バトラー, カードID, parent);
        if (card == null || !card.トークン)
        {
            if (card != null)
                Object.Destroy(card.gameObject);
            Debug.LogWarning($"TokenSummonSkill: トークンが見つかりません。カードID={カードID}");
            return;
        }

        if (UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card))
        {
            Debug.LogWarning($"TokenSummonSkill: {card.cardName}は同一カード制限により場に出せません。");
            Object.Destroy(card.gameObject);
            return;
        }

        if (battle.IsOnlineMode && ownerIsLocal && OnlineSyncManager.Instance != null)
        {
            string zoneName = battle.GetZoneName(zone);
            OnlineSyncManager.Instance.SyncTokenSummon(カードID, BattleManager.CovertOpponentZoneName(zoneName));
        }

        await battle.SummonRoutine(card, zone, playVoice: true, summonSource: SummonSource.生成, specialSummon: true);
        context?.SetProcessedCards(new[] { card });
    }
}
