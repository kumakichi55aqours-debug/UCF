using UnityEngine;

/// <summary>
/// バトラーとトークンの読み込み。カードIDは両方のフォルダで被らせない。
/// 同じIDが両方にあるときはバトラーを使う。
/// </summary>
public static class BattlerAssets
{
    public const string BattlerFolder = "CardDBList/バトラー/";
    public const string TokenFolder = "CardDBList/トークン/";

    public static BattlerDB Load(int cardId, out bool isToken)
    {
        BattlerDB battler = Resources.Load<BattlerDB>(BattlerFolder + cardId);
        if (battler != null)
        {
            isToken = false;
            return battler;
        }

        BattlerDB token = Resources.Load<BattlerDB>(TokenFolder + cardId);
        isToken = token != null;
        return token;
    }

    public static bool IsTokenId(int cardId)
    {
        if (Resources.Load<BattlerDB>(BattlerFolder + cardId) != null)
            return false;
        return Resources.Load<BattlerDB>(TokenFolder + cardId) != null;
    }

    public static bool IsToken(DeckCard card)
    {
        return card != null
            && card.カード種類 == CardKind.バトラー
            && IsTokenId(card.カードID);
    }
}
