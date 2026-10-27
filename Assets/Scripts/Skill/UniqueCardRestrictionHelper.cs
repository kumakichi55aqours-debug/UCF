using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 「自分ゾーンに同じカードが1体しか存在できない」制限をチェックするヘルパークラス
/// </summary>
public static class UniqueCardRestrictionHelper
{
    /// <summary>
    /// カードが同一カード制限スキルを持っているかチェック
    /// </summary>
    public static bool HasUniqueCardRestriction(CardController card)
    {
        if (card == null || card.skills == null)
            return false;

        return card.skills.Any(s => s is UniqueCardRestrictionSkill);
    }

    /// <summary>
    /// 指定されたカードと同じcardIDのカードが、自分の場に既に存在するかチェック
    /// </summary>
    /// <param name="card">チェック対象のカード</param>
    /// <returns>同じcardIDのカードが自分の場に存在する場合true</returns>
    public static bool IsSameCardAlreadyOnField(CardController card)
    {
        if (card == null)
            return false;

        // 自分の場のカードを取得（Bゾーン、Tゾーン、Fゾーン）
        var myFieldCards = GetMyFieldCards(card.isMyCard);

        // 同じcardIDかつ同じcardKindのカードが存在するかチェック
        return myFieldCards.Any(c => c.cardID == card.cardID && c.cardKind == card.cardKind);
    }

    /// <summary>
    /// 指定されたカードが、同一カード制限により場に出せないかチェック
    /// </summary>
    /// <param name="card">チェック対象のカード</param>
    /// <returns>場に出せない場合true</returns>
    public static bool IsRestrictedByUniqueCardRule(CardController card)
    {
        if (card == null)
            return false;

        // カードが同一カード制限スキルを持っているかチェック
        if (!HasUniqueCardRestriction(card))
            return false;

        // 同じcardIDのカードが既に自分の場に存在するかチェック
        return IsSameCardAlreadyOnField(card);
    }

    /// <summary>
    /// 自分の場のカードを取得（Bゾーン、Tゾーン、Fゾーン）
    /// </summary>
    private static List<CardController> GetMyFieldCards(bool isMyCard)
    {
        var fieldCards = new List<CardController>();

        Transform[] zones =
        {
            CardZones.Of(CardZoneType.プレイヤーBゾーン, isMyCard),
            CardZones.Of(CardZoneType.プレイヤーTゾーン, isMyCard),
            CardZones.Of(CardZoneType.プレイヤーFゾーン, isMyCard)
        };

        foreach (var zone in zones)
        {
            if (zone != null)
            {
                fieldCards.AddRange(zone.GetComponentsInChildren<CardController>());
            }
        }

        return fieldCards;
    }

    /// <summary>
    /// デッキカードが同一カード制限により場に出せないかチェック
    /// </summary>
    /// <param name="deckCard">チェック対象のデッキカード</param>
    /// <param name="isMyCard">自分のカードかどうか</param>
    /// <returns>場に出せない場合true</returns>
    public static bool IsDeckCardRestrictedByUniqueCardRule(DeckCard deckCard, bool isMyCard)
    {
        if (deckCard == null)
            return false;

        // 自分の場のカードを取得
        var myFieldCards = GetMyFieldCards(isMyCard);

        // 同じcardIDかつ同じcardKindのカードが存在するかチェック
        var existingCard = myFieldCards.FirstOrDefault(c => c.cardID == deckCard.カードID && c.cardKind == deckCard.カード種類);
        
        if (existingCard == null)
            return false;

        // 既存のカードが同一カード制限スキルを持っているかチェック
        return HasUniqueCardRestriction(existingCard);
    }
}




