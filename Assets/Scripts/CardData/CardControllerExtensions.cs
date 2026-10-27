using UnityEngine;

public static class CardControllerExtensions
{
    public static bool MeetsCriteria(this CardController card, SearchCriteria criteria)
    {
        if (criteria == null) return true;
        return criteria.MatchesCard(card, null, checkSource: false);
    }

    public static bool MeetsCriteria(this CardController card, SearchCriteria criteria, SkillContext context)
    {
        if (criteria == null) return true;
        return criteria.MatchesCard(
            card,
            context != null ? context.発動カード : null,
            checkSource: context != null,
            processedCards: context != null ? context.処理したカード : null);
    }
}
