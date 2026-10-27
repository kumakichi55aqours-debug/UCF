using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ゾーンの候補から選択数だけ選ばせ、その中から残す枚数だけを無作為に残して次のスキルへ渡す。
/// 手札や墓地への移動はしない。複合スキルの次に「手札へ」や「墓地へ」を置く。
/// </summary>
[System.Serializable]
[SkillDisplayName("ランダム選択")]
public class RandomSelectSkill : SkillDB
{
    [SerializeField] public List<CardZoneType> 参照場所 = new List<CardZoneType> { CardZoneType.プレイヤーデッキ };
    [SerializeField] public SearchCriteria 対象条件;
    [SerializeField] public int 選択数 = 1;
    // プレイヤーが選んだ束から、無作為に残す枚数。
    [SerializeField] public int 残す枚数 = 1;
    // 真のとき、同じカード（種類とIDが同じもの）は1枚しか選べない。
    [SerializeField] public bool 同種類のカードは選択不可;

    public override async Task ActivateEffect(SkillContext context)
    {
        int pickCount = Mathf.Max(0, 選択数);
        int keepCount = Mathf.Max(0, 残す枚数);
        if (context == null || pickCount == 0 || keepCount == 0 || 参照場所 == null || 参照場所.Count == 0)
        {
            context?.SetSelection(null);
            return;
        }

        var existing = new List<CardController>();
        var deckCards = new List<DeckCard>();
        CollectCandidates(context, existing, deckCards);
        existing = existing.Distinct().ToList();
        deckCards = deckCards.Distinct().ToList();

        if (existing.Count == 0 && deckCards.Count == 0)
        {
            context.SetSelection(null);
            return;
        }

        List<SelectionItem> chosen;
        int total = existing.Count + deckCards.Count;
        bool limitSameCard = 同種類のカードは選択不可;
        int choosable = limitSameCard ? CountUnique(existing, deckCards) : total;
        if (choosable <= pickCount)
        {
            chosen = SelectionItem.FromCandidates(existing, deckCards);
            if (limitSameCard)
                chosen = TakeOneOfEach(chosen);
        }
        else
        {
            if (CardSelectionUI.Instance == null)
            {
                context.SetSelection(null);
                return;
            }

            chosen = await CardSelectionUI.Instance.ShowAsync(existing, deckCards, pickCount, limitSameCard);
            if (chosen == null || chosen.Count == 0)
            {
                context.SetSelection(null);
                return;
            }

            if (limitSameCard)
                chosen = TakeOneOfEach(chosen);
        }

        int keep = Mathf.Min(keepCount, chosen.Count);
        var kept = chosen.OrderBy(_ => Random.value).Take(keep).ToList();
        context.SetSelection(kept);
    }

    void CollectCandidates(SkillContext context, List<CardController> existing, List<DeckCard> deck)
    {
        bool isMyCard = context.発動カード == null || context.発動カード.isMyCard;
        var criteria = 対象条件;
        if (BattleManager.Instance == null)
            return;

        foreach (var zone in 参照場所)
        {
            if (CardZones.IsDeck(zone))
            {
                AddDeck(CardZones.Deck(zone, isMyCard), criteria, context, deck);
                continue;
            }

            var zoneTransform = CardZones.Of(zone, isMyCard);
            if (zoneTransform == null)
                continue;

            existing.AddRange(
                zoneTransform.GetComponentsInChildren<CardController>()
                    .Where(c => c != null && (criteria == null || c.MeetsCriteria(criteria, context)))
            );
        }
    }

    static void AddDeck(List<DeckCard> source, SearchCriteria criteria, SkillContext context, List<DeckCard> destination)
    {
        if (source == null)
            return;

        destination.AddRange(source.Where(d =>
            d != null && (criteria == null || criteria.MatchesDeck(d, context.発動カード, checkSource: true))));
    }

    static int CountUnique(List<CardController> existing, List<DeckCard> deck)
    {
        var seen = new HashSet<long>();
        foreach (var card in existing)
        {
            if (card != null)
                seen.Add(IdentityKey(card.cardKind, card.cardID));
        }
        foreach (var deckCard in deck)
        {
            if (deckCard != null)
                seen.Add(IdentityKey(deckCard.カード種類, deckCard.カードID));
        }
        return seen.Count;
    }

    static List<SelectionItem> TakeOneOfEach(List<SelectionItem> items)
    {
        var kept = new List<SelectionItem>();
        var seen = new HashSet<long>();
        foreach (var item in items)
        {
            if (item == null || !item.TryGetIdentity(out CardKind kind, out int id))
                continue;
            if (!seen.Add(IdentityKey(kind, id)))
                continue;
            kept.Add(item);
        }
        return kept;
    }

    static long IdentityKey(CardKind kind, int id)
    {
        return ((long)kind << 32) | (uint)id;
    }
}
