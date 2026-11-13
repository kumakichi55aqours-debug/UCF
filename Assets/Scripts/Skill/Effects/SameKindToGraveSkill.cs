using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 指定ゾーンから1枚選び、そのカードと同じ種類・同じIDのカードを、別ゾーンからすべて墓地へ送る。
/// 選んだカード自体は動かさない。
/// </summary>
[System.Serializable]
[SkillDisplayName("同種を墓地へ")]
public class SameKindToGraveSkill : SkillDB
{
    public CardZoneType 選択ゾーン;
    public SearchCriteria 選択条件;
    public List<CardZoneType> 送るゾーンリスト = new List<CardZoneType>();

    public override async Task ActivateEffect(SkillContext context)
    {
        CardController chosen = await ChooseTemplate(context);
        if (chosen == null)
            return;

        if (送るゾーンリスト == null || 送るゾーンリスト.Count == 0)
            return;

        var criteria = new SearchCriteria
        {
            フィルタ = new List<CardSearchFilter>
            {
                new CardSearchFilter
                {
                    カードの種類 = chosen.cardKind,
                    カードID = chosen.cardID,
                    コスト = -1,
                    コスト比較設定済み = true
                }
            }
        };

        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;

        var sender = new ToGraveSkill();
        var normalZones = new List<CardZoneType>();

        foreach (CardZoneType zone in 送るゾーンリスト)
        {
            if (IsRemoteDeck(zone, ownerIsLocal))
            {
                await OnlineSyncManager.Instance.RequestOpponentDeckToOwnGrave(
                    chosen.cardKind,
                    chosen.cardID);
                continue;
            }

            normalZones.Add(zone);
        }

        if (normalZones.Count > 0)
        {
            await sender.SendCardToGrave(
                context,
                criteria,
                normalZones,
                99,
                PlayerType.プレイヤー);
        }
    }

    static bool IsRemoteDeck(CardZoneType zone, bool ownerIsLocal)
    {
        var battle = BattleManager.Instance;
        if (battle == null || !battle.IsOnlineMode || OnlineSyncManager.Instance == null)
            return false;
        if (!CardZones.IsDeck(zone))
            return false;
        return CardZones.Deck(zone, ownerIsLocal) == battle.eBattleDeck;
    }

    async Task<CardController> ChooseTemplate(SkillContext context)
    {
        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
        Transform zone = CardZones.Of(選択ゾーン, ownerIsLocal);
        if (zone == null)
            return null;

        var candidates = new List<CardController>();
        foreach (CardController card in zone.GetComponentsInChildren<CardController>())
        {
            if (card != null && card.MeetsCriteria(選択条件, context))
                candidates.Add(card);
        }

        if (candidates.Count == 0)
            return null;

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
}
