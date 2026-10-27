using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 同名カード（同じ所有者・種類・ID）の同一スキルで使用回数を共有する。
/// </summary>
public static class SkillUseCountStore
{
    private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

    public static bool IsSameSkill(SkillDB a, SkillDB b)
    {
        if (a == null || b == null) return false;
        return a.GetType() == b.GetType()
            && a.スキル名 == b.スキル名
            && a.タイミング == b.タイミング
            && a.LocationKey() == b.LocationKey()
            && a.対象フェイズ == b.対象フェイズ;
    }

    public static int Get(CardController card, SkillDB skill)
    {
        if (card == null || skill == null) return skill != null ? skill.使用回数 : 0;
        if (BattleManager.Instance == null) return 0;
        return counts.TryGetValue(MakeKey(card, skill), out int count) ? count : 0;
    }

    public static void Consume(CardController card, SkillDB skill)
    {
        if (card == null || skill == null) return;
        if (BattleManager.Instance == null) return;

        int next = Get(card, skill) + 1;
        counts[MakeKey(card, skill)] = next;
        ApplyToMatchingCards(card, skill, next);
    }

    public static void ApplyTo(CardController card)
    {
        if (BattleManager.Instance == null) return;
        if (card?.skills == null) return;

        foreach (var skill in card.skills)
        {
            if (skill == null) continue;
            skill.使用回数 = Get(card, skill);
        }
    }

    public static void ResetAll()
    {
        counts.Clear();

        foreach (var card in GetAllBattleCards())
        {
            if (card?.skills == null) continue;
            foreach (var skill in card.skills)
            {
                if (skill != null)
                    skill.使用回数 = 0;
            }
        }
    }

    private static void ApplyToMatchingCards(CardController source, SkillDB skill, int count)
    {
        foreach (var card in GetAllBattleCards())
        {
            if (card == null || card.skills == null) continue;
            if (card.cardKind != source.cardKind || card.cardID != source.cardID) continue;
            if (card.isMyCard != source.isMyCard) continue;

            foreach (var other in card.skills)
            {
                if (IsSameSkill(other, skill))
                    other.使用回数 = count;
            }
        }
    }

    private static string MakeKey(CardController card, SkillDB skill)
    {
        string owner = card.isMyCard ? "me" : "opp";
        return $"{owner}:{(int)card.cardKind}:{card.cardID}:{skill.GetType().FullName}:{skill.スキル名}:{skill.タイミング}:{skill.LocationKey()}:{skill.対象フェイズ}";
    }

    private static IEnumerable<CardController> GetAllBattleCards()
    {
        var battle = BattleManager.Instance;
        if (battle == null) yield break;

        var zones = new[]
        {
            battle.pBattlerZone, battle.eBattlerZone,
            battle.pTacticsZone, battle.eTacticsZone,
            battle.pFieldZone, battle.eFieldZone,
            battle.pHandZone, battle.eHandZone,
            battle.pGraveZone, battle.eGraveZone,
            battle.pDeckZone, battle.eDeckZone
        };

        foreach (var zone in zones)
        {
            if (zone == null) continue;
            foreach (var card in zone.GetComponentsInChildren<CardController>())
            {
                if (card != null)
                    yield return card;
            }
        }
    }
}
