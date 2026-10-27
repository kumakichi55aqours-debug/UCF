using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CardZoneType から BattleManager 上のゾーンを引く。
/// 「プレイヤー」はスキルを持つカード側。ownerIsLocalPlayer が false のときはプレイヤーと相手を入れ替える。
/// </summary>
public static class CardZones
{
    public static bool IsDeck(CardZoneType zone)
    {
        return zone == CardZoneType.プレイヤーデッキ || zone == CardZoneType.相手デッキ;
    }

    /// <summary>
    /// ローカルのプレイヤー側を「プレイヤー」としたゾーン。視点の入れ替えはしない。
    /// </summary>
    public static Transform Absolute(CardZoneType zone)
    {
        return Of(zone, true);
    }

    public static Transform Of(CardZoneType zone, bool ownerIsLocalPlayer)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return null;

        bool localSide = BelongsToOwner(zone) == ownerIsLocalPlayer;
        switch (Kind(zone))
        {
            case ZoneKind.Hand:
                return localSide ? battle.pHandZone : battle.eHandZone;
            case ZoneKind.Battler:
                return localSide ? battle.pBattlerZone : battle.eBattlerZone;
            case ZoneKind.Tactics:
                return localSide ? battle.pTacticsZone : battle.eTacticsZone;
            case ZoneKind.Field:
                return localSide ? battle.pFieldZone : battle.eFieldZone;
            case ZoneKind.Grave:
                return localSide ? battle.pGraveZone : battle.eGraveZone;
            case ZoneKind.Deck:
                return localSide ? battle.pDeckZone : battle.eDeckZone;
            default:
                return null;
        }
    }

    public static List<DeckCard> Deck(CardZoneType zone, bool ownerIsLocalPlayer)
    {
        var battle = BattleManager.Instance;
        if (battle == null || !IsDeck(zone))
            return null;

        bool localSide = BelongsToOwner(zone) == ownerIsLocalPlayer;
        return localSide ? battle.pBattleDeck : battle.eBattleDeck;
    }

    /// <summary>
    /// そのゾーンから送る先の墓地。墓地自身や未対応のゾーンは null。
    /// </summary>
    public static Transform GraveFor(CardZoneType sourceZone, bool ownerIsLocalPlayer)
    {
        if (!SendsToGrave(sourceZone))
            return null;

        var battle = BattleManager.Instance;
        if (battle == null)
            return null;

        bool localSide = BelongsToOwner(sourceZone) == ownerIsLocalPlayer;
        return localSide ? battle.pGraveZone : battle.eGraveZone;
    }

    static bool SendsToGrave(CardZoneType zone)
    {
        switch (zone)
        {
            case CardZoneType.プレイヤー手札:
            case CardZoneType.プレイヤーBゾーン:
            case CardZoneType.プレイヤーTゾーン:
            case CardZoneType.プレイヤーFゾーン:
            case CardZoneType.プレイヤーデッキ:
            case CardZoneType.相手手札:
            case CardZoneType.相手Bゾーン:
            case CardZoneType.相手Tゾーン:
            case CardZoneType.相手Fゾーン:
            case CardZoneType.相手デッキ:
                return true;
            default:
                return false;
        }
    }

    static bool BelongsToOwner(CardZoneType zone)
    {
        switch (zone)
        {
            case CardZoneType.プレイヤー手札:
            case CardZoneType.プレイヤーBゾーン:
            case CardZoneType.プレイヤーTゾーン:
            case CardZoneType.プレイヤーFゾーン:
            case CardZoneType.プレイヤー墓地:
            case CardZoneType.プレイヤーデッキ:
                return true;
            default:
                return false;
        }
    }

    enum ZoneKind
    {
        None,
        Hand,
        Battler,
        Tactics,
        Field,
        Grave,
        Deck
    }

    static ZoneKind Kind(CardZoneType zone)
    {
        switch (zone)
        {
            case CardZoneType.プレイヤー手札:
            case CardZoneType.相手手札:
                return ZoneKind.Hand;
            case CardZoneType.プレイヤーBゾーン:
            case CardZoneType.相手Bゾーン:
                return ZoneKind.Battler;
            case CardZoneType.プレイヤーTゾーン:
            case CardZoneType.相手Tゾーン:
                return ZoneKind.Tactics;
            case CardZoneType.プレイヤーFゾーン:
            case CardZoneType.相手Fゾーン:
                return ZoneKind.Field;
            case CardZoneType.プレイヤー墓地:
            case CardZoneType.相手墓地:
                return ZoneKind.Grave;
            case CardZoneType.プレイヤーデッキ:
            case CardZoneType.相手デッキ:
                return ZoneKind.Deck;
            default:
                return ZoneKind.None;
        }
    }
}
