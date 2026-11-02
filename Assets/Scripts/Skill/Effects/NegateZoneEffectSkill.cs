using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// このカードが場所リストの位置にいるあいだ、指定ゾーンにいて対象条件を満たすカードの効果を無効にする。
/// ゾーンの「プレイヤー」はこのスキルを持つカード側、「相手」はその反対側。
/// 対象条件が空なら、そのゾーンにいるカードすべて。フィルタを並べると、いずれか1件に合うカード。
/// 1件のフィルタにカテゴリを複数入れると、そのすべてを持つカードだけ。
/// タイミングは永続効果かルール効果。
/// </summary>
[Serializable]
[SkillDisplayName("ゾーン効果無効")]
public class NegateZoneEffectSkill : SkillDB
{
    [Tooltip("このスキルを持つカードから見たゾーン。ここにいるカードの効果を止める。")]
    [SerializeField] public CardZoneType 無効ゾーン = CardZoneType.相手墓地;

    [Tooltip("空ならそのゾーンのカードすべて。フィルタはいずれか1件で合う。")]
    [SerializeField] public SearchCriteria 対象条件;

    public NegateZoneEffectSkill()
    {
        タイミング = ActiveTiming.ルール効果;
        if (場所リスト == null)
            場所リスト = new List<ActiveLocation>();
        if (!IncludesLocation(ActiveLocation.Bゾーン))
            場所リスト.Add(ActiveLocation.Bゾーン);

        if (対象条件 == null || 対象条件.IsEmpty())
        {
            対象条件 = new SearchCriteria
            {
                フィルタ = new List<CardSearchFilter>
                {
                    FilterOf(CardCategoryList.怪獣),
                    FilterOf(CardCategoryList.超獣)
                }
            };
        }
    }

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// このカードの効果が、場のゾーン効果無効で止められている。
    /// </summary>
    public static bool Blocks(CardController card)
    {
        if (card == null || BattleManager.Instance == null)
            return false;

        var holders = CollectHolders();
        for (int i = 0; i < holders.Count; i++)
        {
            CardController holder = holders[i];
            if (holder == null || !Locks(holder, card))
                continue;
            if (holder != card && Suppressed(holder, card, holders))
                continue;
            return true;
        }

        return false;
    }

    static bool Suppressed(CardController holder, CardController subject, List<CardController> holders)
    {
        for (int i = 0; i < holders.Count; i++)
        {
            CardController other = holders[i];
            if (other == null || other == holder || other == subject)
                continue;
            if (Locks(other, holder))
                return true;
        }

        return false;
    }

    static bool Locks(CardController holder, CardController card)
    {
        foreach (var skill in PrintedSkills<NegateZoneEffectSkill>(holder))
        {
            if (Applies(skill, holder, card))
                return true;
        }

        foreach (var skill in GrantedSkills<NegateZoneEffectSkill>(holder))
        {
            if (Applies(skill, holder, card))
                return true;
        }

        return false;
    }

    static bool Applies(NegateZoneEffectSkill skill, CardController holder, CardController card)
    {
        if (skill == null || card == null)
            return false;
        if (skill.タイミング != ActiveTiming.永続効果 && skill.タイミング != ActiveTiming.ルール効果)
            return false;
        if (!skill.IncludesLocation(holder.GetCurrentActiveLocation()))
            return false;
        if (!skill.ConditionsMet(holder))
            return false;
        if (!IsInZone(card, skill.無効ゾーン, holder.isMyCard))
            return false;

        var context = new SkillContext(holder, card, card.transform);
        return card.MeetsCriteria(skill.対象条件, context);
    }

    static bool IsInZone(CardController card, CardZoneType zone, bool holderIsLocal)
    {
        if (zone == CardZoneType.None)
            return false;

        Transform expected = CardZones.Of(zone, holderIsLocal);
        return expected != null && card.transform.parent == expected;
    }

    static CardSearchFilter FilterOf(CardCategoryList category)
    {
        return new CardSearchFilter
        {
            カードのカテゴリ = new List<CardCategoryList> { category }
        };
    }

    static List<CardController> CollectHolders()
    {
        var battle = BattleManager.Instance;
        var cards = new List<CardController>();
        Append(cards, battle.pBattlerZone);
        Append(cards, battle.pTacticsZone);
        Append(cards, battle.pFieldZone);
        Append(cards, battle.pHandZone);
        Append(cards, battle.pGraveZone);
        Append(cards, battle.pDimensionZone);
        Append(cards, battle.eBattlerZone);
        Append(cards, battle.eTacticsZone);
        Append(cards, battle.eFieldZone);
        Append(cards, battle.eHandZone);
        Append(cards, battle.eGraveZone);
        Append(cards, battle.eDimensionZone);
        return cards;
    }

    static void Append(List<CardController> cards, Transform zone)
    {
        if (zone == null)
            return;
        cards.AddRange(zone.GetComponentsInChildren<CardController>());
    }
}
