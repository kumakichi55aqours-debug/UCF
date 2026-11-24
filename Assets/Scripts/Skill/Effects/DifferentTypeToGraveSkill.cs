using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 自分Bゾーンから異なるタイプのバトラーを2体選び、
/// その2タイプのどちらでもない相手バトラー1体を墓地へ送る。
/// 選んだ自分のバトラーは移動しない。
/// 相手に送れるバトラーが残る組み合わせがあるときだけ発動できる。
/// </summary>
[System.Serializable]
[SkillDisplayName("異タイプ墓地送り")]
public class DifferentTypeToGraveSkill : SkillDB
{
    [SerializeField] public SearchCriteria 自分対象条件;
    [SerializeField] public SearchCriteria 相手対象条件;

    public override bool CanActivate(SkillContext context)
    {
        if (!base.CanActivate(context))
            return false;

        return context?.発動カード != null
            && HasResolvingLine(
                context.発動カード,
                自分対象条件,
                相手対象条件,
                context);
    }

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context?.発動カード == null)
            return;

        bool ownerIsLocal = context.発動カード.isMyCard;

        var own = BattlersIn(
            CardZoneType.プレイヤーBゾーン,
            ownerIsLocal);

        var opponent = BattlersIn(
            CardZoneType.相手Bゾーン,
            ownerIsLocal);

        var pair = await ChoosePair(
            own,
            opponent,
            自分対象条件,
            相手対象条件,
            context);

        if (pair == null)
            return;

        var first = pair[0];
        var second = pair[1];

        var target = await ChooseOne(
            Targets(
                opponent,
                TypeOf(first),
                TypeOf(second),
                相手対象条件,
                context));

        if (target == null)
            return;

        context.SetSelection(new[]
        {
            new SelectionItem
            {
                source = SelectionItem.SourceType.Existing,
                existingCard = target
            }
        });

        await new ToGraveSkill().ActivateEffect(context);
    }

    public static bool HasResolvingLine(
        CardController source,
        SearchCriteria ownCriteria,
        SearchCriteria opponentCriteria,
        SkillContext context)
    {
        if (source == null)
            return false;

        bool ownerIsLocal = source.isMyCard;

        var own = BattlersIn(
            CardZoneType.プレイヤーBゾーン,
            ownerIsLocal);

        var opponent = BattlersIn(
            CardZoneType.相手Bゾーン,
            ownerIsLocal);

        return FirstOptions(
            own,
            opponent,
            ownCriteria,
            opponentCriteria,
            context).Count > 0;
    }

    // 1体目として選択可能なカードを取得
    static List<CardController> FirstOptions(
        List<CardController> own,
        List<CardController> opponent,
        SearchCriteria ownCriteria,
        SearchCriteria opponentCriteria,
        SkillContext context)
    {
        var options = new List<CardController>();

        if (own == null)
            return options;

        for (int i = 0; i < own.Count; i++)
        {
            var card = own[i];

            if (card == null
                || !card.MeetsCriteria(ownCriteria, context))
                continue;

            if (SecondsFor(
                card,
                own,
                opponent,
                ownCriteria,
                opponentCriteria,
                context).Count > 0)
            {
                options.Add(card);
            }
        }

        return options;
    }

    // 1体目に対して選択可能な2体目を取得
    static List<CardController> SecondsFor(
        CardController first,
        List<CardController> own,
        List<CardController> opponent,
        SearchCriteria ownCriteria,
        SearchCriteria opponentCriteria,
        SkillContext context)
    {
        var options = new List<CardController>();

        if (first == null
            || own == null
            || !first.MeetsCriteria(ownCriteria, context))
            return options;

        CardTypeList firstType = TypeOf(first);

        if (firstType == CardTypeList.None)
            return options;

        for (int i = 0; i < own.Count; i++)
        {
            var card = own[i];

            if (card == null
                || card == first
                || !card.MeetsCriteria(ownCriteria, context))
            {
                continue;
            }

            CardTypeList secondType = TypeOf(card);

            if (secondType == CardTypeList.None
                || secondType == firstType)
                continue;

            if (Targets(
                opponent,
                firstType,
                secondType,
                opponentCriteria,
                context).Count > 0)
            {
                options.Add(card);
            }
        }

        return options;
    }

    // 2体のタイプと異なる相手バトラーを取得
    static List<CardController> Targets(
        List<CardController> opponent,
        CardTypeList firstType,
        CardTypeList secondType,
        SearchCriteria opponentCriteria,
        SkillContext context)
    {
        var options = new List<CardController>();

        if (opponent == null
            || firstType == CardTypeList.None
            || secondType == CardTypeList.None
            || firstType == secondType)
        {
            return options;
        }

        for (int i = 0; i < opponent.Count; i++)
        {
            var card = opponent[i];

            CardTypeList type = TypeOf(card);

            if (type != CardTypeList.None
                && type != firstType
                && type != secondType
                && card.MeetsCriteria(opponentCriteria, context))
            {
                options.Add(card);
            }
        }

        return options;
    }

    static List<CardController> BattlersIn(
        CardZoneType zone,
        bool ownerIsLocal)
    {
        var list = new List<CardController>();

        var zoneTransform = CardZones.Of(zone, ownerIsLocal);

        if (zoneTransform == null)
            return list;

        var cards = zoneTransform.GetComponentsInChildren<CardController>();

        for (int i = 0; i < cards.Length; i++)
        {
            if (TypeOf(cards[i]) != CardTypeList.None)
                list.Add(cards[i]);
        }

        return list;
    }

    static CardTypeList TypeOf(CardController card)
    {
        if (card == null || card.cardKind != CardKind.バトラー)
            return CardTypeList.None;

        if (card.type != CardTypeList.None)
            return card.type;

        if (card.model is BattlerModel battler)
            return battler.タイプ;

        return CardTypeList.None;
    }

    // 自分のバトラー2体を選択
    static async Task<List<CardController>> ChoosePair(
        List<CardController> own,
        List<CardController> opponent,
        SearchCriteria ownCriteria,
        SearchCriteria opponentCriteria,
        SkillContext context)
    {
        if (own == null
            || FirstOptions(
                own,
                opponent,
                ownCriteria,
                opponentCriteria,
                context).Count == 0)
        {
            return null;
        }

        if (CardSelectionUI.Instance == null
            || SkillContext.IsOfflineCpuResolving)
        {
            return RandomPair(
                own,
                opponent,
                ownCriteria,
                opponentCriteria,
                context);
        }

        var selected = await CardSelectionUI.Instance.ShowAsync(
            own,
            new List<DeckCard>(),
            2,
            rejectSameKind: false,
            letCpuChoose: false,
            rejectSameType: true,
            rejectWhen: (picked, item) =>
                !FitsPair(
                    own,
                    opponent,
                    ownCriteria,
                    opponentCriteria,
                    context,
                    picked,
                    item));

        if (selected == null || selected.Count < 2)
            return null;

        var first = selected[0].existingCard;
        var second = selected[1].existingCard;

        if (first == null || second == null)
            return null;

        if (TypeOf(first) == CardTypeList.None
            || TypeOf(first) == TypeOf(second))
        {
            return null;
        }

        if (!first.MeetsCriteria(ownCriteria, context)
            || !second.MeetsCriteria(ownCriteria, context))
        {
            return null;
        }

        if (Targets(
            opponent,
            TypeOf(first),
            TypeOf(second),
            opponentCriteria,
            context).Count == 0)
        {
            return null;
        }

        return new List<CardController> { first, second };
    }

    // 選択UIで組み合わせが成立するか判定
    static bool FitsPair(
        List<CardController> own,
        List<CardController> opponent,
        SearchCriteria ownCriteria,
        SearchCriteria opponentCriteria,
        SkillContext context,
        IReadOnlyList<SelectionItem> picked,
        SelectionItem item)
    {
        var card = item?.existingCard;

        if (card == null
            || !card.MeetsCriteria(ownCriteria, context))
            return false;

        if (picked == null || picked.Count == 0)
        {
            return SecondsFor(
                card,
                own,
                opponent,
                ownCriteria,
                opponentCriteria,
                context).Count > 0;
        }

        var first = picked[0]?.existingCard;

        if (first == null || card == first)
            return false;

        var seconds = SecondsFor(
            first,
            own,
            opponent,
            ownCriteria,
            opponentCriteria,
            context);

        for (int i = 0; i < seconds.Count; i++)
        {
            if (seconds[i] == card)
                return true;
        }

        return false;
    }

    // CPU用ランダム選択
    static List<CardController> RandomPair(
        List<CardController> own,
        List<CardController> opponent,
        SearchCriteria ownCriteria,
        SearchCriteria opponentCriteria,
        SkillContext context)
    {
        var firstOptions = FirstOptions(
            own,
            opponent,
            ownCriteria,
            opponentCriteria,
            context);

        if (firstOptions.Count == 0)
            return null;

        var first = firstOptions[
            Random.Range(0, firstOptions.Count)];

        var seconds = SecondsFor(
            first,
            own,
            opponent,
            ownCriteria,
            opponentCriteria,
            context);

        if (seconds.Count == 0)
            return null;

        return new List<CardController>
        {
            first,
            seconds[Random.Range(0, seconds.Count)]
        };
    }

    static async Task<CardController> ChooseOne(
        List<CardController> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;

        if (candidates.Count == 1
            || CardSelectionUI.Instance == null
            || SkillContext.IsOfflineCpuResolving)
        {
            return candidates[Random.Range(0, candidates.Count)];
        }

        var selected = await CardSelectionUI.Instance.ShowAsync(
            candidates,
            new List<DeckCard>(),
            1,
            rejectSameKind: false,
            letCpuChoose: false);

        if (selected == null)
            return null;

        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i]?.existingCard != null)
                return selected[i].existingCard;
        }

        return null;
    }
}
