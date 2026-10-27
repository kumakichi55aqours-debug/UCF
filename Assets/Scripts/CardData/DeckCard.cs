using System.Collections.Generic;
using UnityEngine;

public class DeckCard
{
    static readonly Dictionary<long, string> cardNameCache = new Dictionary<long, string>();

    public CardKind カード種類;
    public int カードID;
    public int コスト;
    public List<CardCategoryList> カテゴリ;
    public CardTypeList カードタイプ;

    public DeckCard(CardKind cardKind, int cardID, int cost = 0, List<CardCategoryList> category = null, CardTypeList cardType = CardTypeList.None)
    {
        カード種類 = cardKind;
        カードID = cardID;
        コスト = cost;
        カテゴリ = category ?? new List<CardCategoryList>();
        カードタイプ = cardType;
    }

    public void PopulateDerivedData()
    {
        // バトラーカードの場合はタイプやカテゴリ情報を補完
        if (カード種類 == CardKind.バトラー)
        {
            if (カードタイプ == CardTypeList.None || カテゴリ == null || カテゴリ.Count == 0)
            {
                var battlerDb = Resources.Load<BattlerDB>($"CardDBList/バトラー/{カードID}");
                if (battlerDb != null)
                {
                    if (カードタイプ == CardTypeList.None)
                    {
                        カードタイプ = battlerDb.type;
                    }

                    if (カテゴリ == null || カテゴリ.Count == 0)
                    {
                        カテゴリ = battlerDb.category != null
                            ? new List<CardCategoryList>(battlerDb.category)
                            : new List<CardCategoryList>();
                    }
                }
            }
        }
        else
        {
            if (カテゴリ == null)
            {
                カテゴリ = new List<CardCategoryList>();
            }
        }
    }

    /// <summary>
    /// カードデータの名前を引く。DeckCard自体は名前を持たない。
    /// </summary>
    public string GetCardName()
    {
        long key = ((long)カード種類 << 32) | (uint)カードID;
        if (cardNameCache.TryGetValue(key, out string cached))
            return cached;

        string name = null;
        switch (カード種類)
        {
            case CardKind.バトラー:
                name = Resources.Load<BattlerDB>("CardDBList/バトラー/" + カードID)?.cardName;
                break;
            case CardKind.作戦:
                name = Resources.Load<TacticsDB>("CardDBList/作戦/" + カードID)?.cardName;
                break;
            case CardKind.フィールド:
                name = Resources.Load<FieldDB>("CardDBList/フィールド/" + カードID)?.cardName;
                break;
        }

        cardNameCache[key] = name;
        return name;
    }

    public bool MatchesSearchWord(string searchWord, bool exactMatch)
    {
        if (string.IsNullOrEmpty(searchWord))
            return true;

        string name = GetCardName();
        if (string.IsNullOrEmpty(name))
            return false;

        return exactMatch ? name == searchWord : name.Contains(searchWord);
    }
}
