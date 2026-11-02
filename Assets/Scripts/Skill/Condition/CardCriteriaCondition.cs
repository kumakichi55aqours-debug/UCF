using System;
using UnityEngine;

/// <summary>
/// コンテキスト上の1枚が、検索条件を満たすか。
/// 発動条件では見るカードの役割を使い、カードだけを渡された判定では発動カードのときだけそのカードを見る。
/// 検索条件が空なら、そのカードがいるだけで成立する。
/// </summary>
[Serializable]
public class CardCriteriaCondition : ISkillCondition
{
    [Tooltip("発動条件では、この役割のカードを見る。既定は発動カード。攻撃者にすると、相手の技使用時やダメージ計算の攻撃者を見る。")]
    [SerializeField] private ContextCardRole 見るカード = ContextCardRole.発動カード;
    [SerializeField] private SearchCriteria searchCriteria = new SearchCriteria();

    public bool IsConditionMet(CardController card)
    {
        if (見るカード != ContextCardRole.発動カード)
            return false;
        return Matches(card, card);
    }

    public bool IsConditionMet(SkillContext context)
    {
        CardController card = ContextCardRoles.Resolve(見るカード, context);
        CardController source = context != null ? context.発動カード : null;
        return Matches(card, source);
    }

    bool Matches(CardController card, CardController source)
    {
        if (card == null)
            return false;
        if (searchCriteria == null || !searchCriteria.HasCondition())
            return true;
        return searchCriteria.MatchesCard(card, source, checkSource: true, partialName: true);
    }
}
