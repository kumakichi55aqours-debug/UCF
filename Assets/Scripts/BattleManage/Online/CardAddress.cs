using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 盤面にいる1枚の住所。ゾーン名は送信者から見た名前。
/// 受信側は並び順でカードを取り、種類とIDが一致したときだけ使う。
/// </summary>
public struct CardAddress
{
    public CardKind Kind;
    public int Id;
    public string ZoneName;
    public int Index;

    public bool IsValid => Index >= 0 && !string.IsNullOrEmpty(ZoneName);

    public static bool TryFrom(CardController card, out CardAddress address)
    {
        address = new CardAddress { Index = -1, ZoneName = string.Empty };
        if (card == null || BattleManager.Instance == null)
            return false;
        if (!BattleManager.Instance.TryGetCardAddress(card, out string zoneName, out int index))
            return false;

        address = new CardAddress
        {
            Kind = card.cardKind,
            Id = card.cardID,
            ZoneName = zoneName,
            Index = index
        };
        return true;
    }

    public static bool TryAdd(CardController card, List<string> kinds, List<int> ids, List<string> zones, List<int> indices)
    {
        if (!TryFrom(card, out CardAddress address))
            return false;

        kinds.Add(address.Kind.ToString());
        ids.Add(address.Id);
        zones.Add(address.ZoneName);
        indices.Add(address.Index);
        return true;
    }

    /// <summary>
    /// 送信者のゾーン名を受信者の盤面へ読み替えて、種類とIDが一致するカードを返す。
    /// </summary>
    public static CardController Resolve(string kindStr, int id, string zoneName, int index)
    {
        if (!System.Enum.TryParse(kindStr, out CardKind kind))
            return null;
        if (BattleManager.Instance == null || string.IsNullOrEmpty(zoneName) || index < 0)
            return null;

        CardController card = BattleManager.Instance.ResolveCardFromSender(zoneName, index);
        if (card == null || card.cardKind != kind || card.cardID != id)
            return null;
        return card;
    }

    /// <summary>
    /// ゾーン名を読み替えずに、この端末の盤面から取る。チェーンで自分のカードが戻ってきたときに使う。
    /// </summary>
    public static CardController ResolveLocal(string kindStr, int id, string zoneName, int index)
    {
        if (!System.Enum.TryParse(kindStr, out CardKind kind))
            return null;
        BattleManager battle = BattleManager.Instance;
        if (battle == null || string.IsNullOrEmpty(zoneName) || index < 0)
            return null;

        CardController card = battle.GetCardAtZoneIndex(battle.GetZoneByName(zoneName), index);
        if (card == null || card.cardKind != kind || card.cardID != id)
            return null;
        return card;
    }

    public static int Count(string[] kinds, int[] ids, string[] zones, int[] indices)
    {
        if (kinds == null || ids == null || zones == null || indices == null)
            return 0;

        int count = kinds.Length;
        if (ids.Length < count) count = ids.Length;
        if (zones.Length < count) count = zones.Length;
        if (indices.Length < count) count = indices.Length;
        return count;
    }

    public static CardController ResolveAt(string[] kinds, int[] ids, string[] zones, int[] indices, int index)
    {
        if (kinds == null || ids == null || zones == null || indices == null)
            return null;
        if (index < 0 || index >= kinds.Length || index >= ids.Length || index >= zones.Length || index >= indices.Length)
            return null;
        return Resolve(kinds[index], ids[index], zones[index], indices[index]);
    }
}
