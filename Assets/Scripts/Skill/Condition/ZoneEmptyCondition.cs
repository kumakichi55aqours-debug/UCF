using System;
using UnityEngine;

[Serializable]
public class ZoneEmptyCondition : ISkillCondition
{
    [SerializeField] private CardZoneType zone;

    public ZoneEmptyCondition() 
    { 
        zone = CardZoneType.プレイヤーBゾーン; // デフォルト値
    }

    public ZoneEmptyCondition(CardZoneType zone)
    {
        this.zone = zone;
    }

    public bool IsConditionMet(CardController card)
    {
        bool ownerIsLocal = card != null && card.isMyCard;
        Transform zoneTransform = CardZones.Of(zone, ownerIsLocal);
        if (zoneTransform == null) return false;
        return zoneTransform.childCount == 0;
    }

    public bool IsConditionMet(SkillContext context)
    {
        return IsConditionMet(context.発動カード);
    }
}