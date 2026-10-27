using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゾーン条件が数える単位。先頭は従来の枚数なので、この項目がない既存データはそのまま動く。
/// 種類を増やすときは DistinctKey に分岐を足す。
/// </summary>
public enum ZoneCountUnit
{
    カード枚数,
    異なるタイプ,
    異なるカード,
    異なるカテゴリの組,
}

[Serializable]
public class CardExistsCondition : ISkillCondition
{
    [SerializeField] private List<CardZoneType> 対象ゾーンリスト;
    [SerializeField] private SearchCriteria searchCriteria;
    [Tooltip("合致したカードをどう数えるか。異なるタイプは力・速などの種類の数で、同じタイプは1つ。タイプなしは数えない。異なるカードはカード種類とカードIDが同じものを1種類とする。異なるカテゴリの組は、カードが持つカテゴリの組み合わせの種類の数。")]
    [SerializeField] private ZoneCountUnit 数え方 = ZoneCountUnit.カード枚数;
    [Tooltip("数え方で集めた数と比べる基準。")]
    [SerializeField] private int 基準枚数 = 1;
    [SerializeField] private MatchType 比較タイプ = MatchType.以上;

    public CardExistsCondition() 
    { 
        対象ゾーンリスト = new List<CardZoneType> { CardZoneType.プレイヤーBゾーン };
        searchCriteria = new SearchCriteria();
        数え方 = ZoneCountUnit.カード枚数;
        基準枚数 = 1;
        比較タイプ = MatchType.以上;
    }

    public CardExistsCondition(string cardName)
    {
        対象ゾーンリスト = new List<CardZoneType> { CardZoneType.プレイヤーBゾーン };
        searchCriteria = new SearchCriteria();
        searchCriteria.フィルタ.Add(new CardSearchFilter { サーチワード = cardName });
        数え方 = ZoneCountUnit.カード枚数;
        基準枚数 = 1;
        比較タイプ = MatchType.以上;
    }

    public bool IsConditionMet(CardController card)
    {
        return 比較タイプ.Matches(CountMatchingCards(card), 基準枚数);
    }

    int CountMatchingCards(CardController sourceCard)
    {
        if (対象ゾーンリスト == null || BattleManager.Instance == null)
            return 0;

        bool isMyCard = sourceCard != null && sourceCard.isMyCard;
        var seen = new HashSet<CardController>();
        var distinct = new HashSet<string>();
        int count = 0;

        foreach (var zoneType in 対象ゾーンリスト)
        {
            if (zoneType == CardZoneType.プレイヤーデッキ || zoneType == CardZoneType.相手デッキ)
            {
                AccumulateDeck(zoneType, isMyCard, sourceCard, ref count, distinct);
                continue;
            }

            Transform zone = CardZones.Of(zoneType, isMyCard);
            if (zone == null)
                continue;

            foreach (var fc in zone.GetComponentsInChildren<CardController>())
            {
                if (fc == null || !seen.Add(fc))
                    continue;
                if (MatchesCondition(fc, sourceCard))
                    AddMatch(ref count, distinct, DistinctKey(fc));
            }
        }

        return 数え方 == ZoneCountUnit.カード枚数 ? count : distinct.Count;
    }

    void AccumulateDeck(CardZoneType zoneType, bool isMyCard, CardController sourceCard, ref int count, HashSet<string> distinct)
    {
        var deck = CardZones.Deck(zoneType, isMyCard);
        if (deck == null)
            return;

        foreach (var deckCard in deck)
        {
            if (searchCriteria != null && !searchCriteria.MatchesDeck(
                deckCard,
                sourceCard,
                checkSource: true,
                partialName: true,
                selfMatchesSameCard: false,
                nullSourcePassesExclude: true))
                continue;

            AddMatch(ref count, distinct, DistinctKey(deckCard));
        }
    }

    void AddMatch(ref int count, HashSet<string> distinct, string key)
    {
        if (数え方 == ZoneCountUnit.カード枚数)
        {
            count++;
            return;
        }

        if (key != null)
            distinct.Add(key);
    }

    string DistinctKey(CardController card)
    {
        switch (数え方)
        {
            case ZoneCountUnit.異なるタイプ:
                CardTypeList type = ResolveType(card);
                return type == CardTypeList.None ? null : ((int)type).ToString();
            case ZoneCountUnit.異なるカード:
                return ((int)card.cardKind) + ":" + card.cardID;
            case ZoneCountUnit.異なるカテゴリの組:
                return CategoryKey(card.category);
            default:
                return null;
        }
    }

    string DistinctKey(DeckCard deck)
    {
        switch (数え方)
        {
            case ZoneCountUnit.異なるタイプ:
                CardTypeList type = ResolveType(deck);
                return type == CardTypeList.None ? null : ((int)type).ToString();
            case ZoneCountUnit.異なるカード:
                return ((int)deck.カード種類) + ":" + deck.カードID;
            case ZoneCountUnit.異なるカテゴリの組:
                if (deck.カテゴリ == null || deck.カテゴリ.Count == 0)
                    deck.PopulateDerivedData();
                return CategoryKey(deck.カテゴリ);
            default:
                return null;
        }
    }

    static CardTypeList ResolveType(CardController card)
    {
        if (card.type != CardTypeList.None)
            return card.type;
        if (card.model is BattlerModel battler)
            return battler.type;
        return CardTypeList.None;
    }

    static CardTypeList ResolveType(DeckCard deck)
    {
        if (deck.カードタイプ == CardTypeList.None)
            deck.PopulateDerivedData();
        return deck.カードタイプ;
    }

    static string CategoryKey(List<CardCategoryList> categories)
    {
        if (categories == null || categories.Count == 0)
            return string.Empty;

        var ids = new int[categories.Count];
        for (int i = 0; i < categories.Count; i++)
            ids[i] = (int)categories[i];
        Array.Sort(ids);
        return string.Join(",", ids);
    }

    private bool MatchesCondition(CardController card, CardController sourceCard)
    {
        if (searchCriteria == null) return true;
        return searchCriteria.MatchesCard(card, sourceCard, checkSource: true, partialName: true);
    }
    
    public bool IsConditionMet(SkillContext context)
    {
        // SkillContextの場合はSourceCardをチェック
        return IsConditionMet(context.発動カード);
    }
}
