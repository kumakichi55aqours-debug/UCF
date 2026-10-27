using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum HandCountTarget
{
    自分,
    相手
}

[Serializable]
public class HandCountCondition : ISkillCondition
{
    [SerializeField] private HandCountTarget 対象手札 = HandCountTarget.自分;
    [SerializeField] private int 基準枚数;
    [SerializeField] private MatchType 比較タイプ = MatchType.以下;

    public HandCountCondition() 
    { 
        対象手札 = HandCountTarget.自分;
        基準枚数 = 0;
        比較タイプ = MatchType.以下;
    }

    public HandCountCondition(int maxCount, MatchType comparisonType = MatchType.以下)
    {
        対象手札 = HandCountTarget.自分;
        基準枚数 = maxCount;
        比較タイプ = comparisonType;
    }

    public bool IsConditionMet(CardController card)
    {
        if (BattleManager.Instance == null)
            return false;

        // スキルを持つカードから見た自分、または相手の手札
        bool ownerIsPlayer = card != null && card.isMyCard;
        bool countPlayerHand = 対象手札 == HandCountTarget.自分 ? ownerIsPlayer : !ownerIsPlayer;
        Transform handZone = countPlayerHand ? BattleManager.Instance.pHandZone : BattleManager.Instance.eHandZone;
        if (handZone == null)
            return false;

        int handCount = handZone.childCount;
        return 比較タイプ.Matches(handCount, 基準枚数);
    }
    
    public bool IsConditionMet(SkillContext context)
    {
        // SkillContextの場合はSourceCardをチェック
        return IsConditionMet(context.発動カード);
    }
}
