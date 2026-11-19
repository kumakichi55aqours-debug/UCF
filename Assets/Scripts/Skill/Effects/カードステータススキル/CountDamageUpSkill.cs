using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 条件に合うカードの枚数 × 効果数だけ、技の表示パワーを上げる。
/// 永続効果なら、場が変わるたびに数え直す。
/// 数える対象状態が空なら、枚数は対象条件だけで決まる。状態を指定したときはデッキを数えない。
/// 対象状態が空なら、受けるバトラーは適用条件だけで決まる。
/// </summary>
[Serializable]
[SkillDisplayName("(〇付与)枚数パワー")]
public class CountDamageUpSkill : SkillDB
{
    [Header("数える対象")]
    public List<CardZoneType> 参照場所 = new List<CardZoneType> { CardZoneType.プレイヤーBゾーン };
    public SearchCriteria 対象条件;

    [SerializeReference]
    public List<ISkillCondition> 数える対象状態 = new List<ISkillCondition>();

    public bool 自分を除く;

    public TargetList 効果対象 = TargetList.自分;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 適用条件;

    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    public List<ElementList> 対象属性;
    public MoveTarget 適用先 = MoveTarget.両方;

    [Header("1枚あたり")]
    public int 効果数 = 1;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 適用条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        int count = CountMatchingCards(context);
        int amount = count * 効果数;
        if (amount == 0)
            return;

        var targetCards = KeepContextCard(await CollectTargetsAsync(効果対象, 対象側, 適用条件, context, 対象状態), context);

        foreach (var card in targetCards)
            ApplyBonus(card, context, count, amount);
    }

    void ApplyBonus(CardController card, SkillContext context, int count, int amount)
    {
        if (card == null || card.cardKind != CardKind.バトラー)
            return;

        if (適用条件 != null && !card.MeetsCriteria(適用条件, context))
            return;

        if (!ConditionsMet(対象状態, card))
            return;

        var battler = card.model as BattlerModel;
        if (battler == null)
            return;

        bool apply1 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement1, 0);
        bool apply2 = AppliesToMoveElement(適用先, 対象属性, battler.moveElement2, 1);
        if (apply1) card.bonusPower1 += amount;
        if (apply2) card.bonusPower2 += amount;

        card.RecalculateModifiedPowers();
        card.UpdateDisplay();
    }

    int CountMatchingCards(SkillContext context)
    {
        if (参照場所 == null || 参照場所.Count == 0 || BattleManager.Instance == null)
            return 0;

        var source = context?.発動カード;
        bool isMyCard = source == null || source.isMyCard;
        var seen = new HashSet<CardController>();
        int count = 0;

        foreach (var zoneType in 参照場所)
        {
            if (zoneType == CardZoneType.None)
                continue;

            if (zoneType == CardZoneType.プレイヤーデッキ || zoneType == CardZoneType.相手デッキ)
            {
                if (!HasCountState())
                    count += CountDeck(zoneType, isMyCard, context);
                continue;
            }

            Transform zone = CardZones.Of(zoneType, isMyCard);
            if (zone == null)
                continue;

            foreach (var card in zone.GetComponentsInChildren<CardController>())
            {
                if (card == null || !seen.Add(card))
                    continue;
                if (自分を除く && source != null && card == source)
                    continue;
                if (対象条件 != null && !card.MeetsCriteria(対象条件, context))
                    continue;
                if (!ConditionsMet(数える対象状態, card))
                    continue;
                count++;
            }
        }

        return count;
    }

    int CountDeck(CardZoneType zoneType, bool isMyCard, SkillContext context)
    {
        var deck = CardZones.Deck(zoneType, isMyCard);
        if (deck == null)
            return 0;

        int count = 0;
        foreach (var deckCard in deck)
        {
            if (DeckMatches(deckCard, context))
                count++;
        }
        return count;
    }

    bool DeckMatches(DeckCard deckCard, SkillContext context)
    {
        if (deckCard == null)
            return false;
        if (対象条件 == null)
            return true;

        return 対象条件.MatchesDeck(
            deckCard,
            context?.発動カード,
            checkSource: true,
            selfMatchesSameCard: false,
            nullSourcePassesExclude: true);
    }

    bool HasCountState()
    {
        if (数える対象状態 == null)
            return false;

        for (int i = 0; i < 数える対象状態.Count; i++)
        {
            if (数える対象状態[i] != null)
                return true;
        }

        return false;
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is CountDamageUpSkill count)
        {
            count.数える対象状態 = 数える対象状態 != null
                ? new List<ISkillCondition>(数える対象状態)
                : new List<ISkillCondition>();
            count.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}
