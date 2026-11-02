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
    public override bool CanActivate(SkillContext context)
    {
        if (!base.CanActivate(context))
            return false;
        return context?.発動カード != null && HasResolvingLine(context.発動カード);
    }

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context?.発動カード == null)
            return;

        bool ownerIsLocal = context.発動カード.isMyCard;
        var own = BattlersIn(CardZoneType.プレイヤーBゾーン, ownerIsLocal);
        var opponent = BattlersIn(CardZoneType.相手Bゾーン, ownerIsLocal);

        var pair = await ChoosePair(own, opponent);
        if (pair == null)
        {
            Debug.Log("[異タイプ墓地送り] 異なるタイプの2体を選べない");
            return;
        }

        var first = pair[0];
        var second = pair[1];

        var target = await ChooseOne(Targets(opponent, TypeOf(first), TypeOf(second)));
        if (target == null)
        {
            Debug.Log("[異タイプ墓地送り] 選んだ2タイプ以外の相手バトラーがいない");
            return;
        }

        Debug.Log($"[異タイプ墓地送り] {TypeOf(first)} と {TypeOf(second)} を参照し、{target.cardName} を墓地へ送る");
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

    public static bool HasResolvingLine(CardController source)
    {
        if (source == null)
            return false;

        bool ownerIsLocal = source.isMyCard;
        var own = BattlersIn(CardZoneType.プレイヤーBゾーン, ownerIsLocal);
        var opponent = BattlersIn(CardZoneType.相手Bゾーン, ownerIsLocal);
        return FirstOptions(own, opponent).Count > 0;
    }

    static List<CardController> FirstOptions(List<CardController> own, List<CardController> opponent)
    {
        var options = new List<CardController>();
        if (own == null)
            return options;

        for (int i = 0; i < own.Count; i++)
        {
            if (SecondsFor(own[i], own, opponent).Count > 0)
                options.Add(own[i]);
        }

        return options;
    }

    static List<CardController> SecondsFor(CardController first, List<CardController> own, List<CardController> opponent)
    {
        var options = new List<CardController>();
        CardTypeList firstType = TypeOf(first);
        if (firstType == CardTypeList.None || own == null)
            return options;

        for (int i = 0; i < own.Count; i++)
        {
            var card = own[i];
            if (card == null || card == first)
                continue;

            CardTypeList secondType = TypeOf(card);
            if (secondType == CardTypeList.None || secondType == firstType)
                continue;
            if (Targets(opponent, firstType, secondType).Count > 0)
                options.Add(card);
        }

        return options;
    }

    static List<CardController> Targets(List<CardController> opponent, CardTypeList firstType, CardTypeList secondType)
    {
        var options = new List<CardController>();
        if (opponent == null || firstType == CardTypeList.None || secondType == CardTypeList.None || firstType == secondType)
            return options;

        for (int i = 0; i < opponent.Count; i++)
        {
            CardTypeList type = TypeOf(opponent[i]);
            if (type != CardTypeList.None && type != firstType && type != secondType)
                options.Add(opponent[i]);
        }

        return options;
    }

    static List<CardController> BattlersIn(CardZoneType zone, bool ownerIsLocal)
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
            return battler.type;
        return CardTypeList.None;
    }

    static async Task<List<CardController>> ChoosePair(List<CardController> own, List<CardController> opponent)
    {
        if (own == null || FirstOptions(own, opponent).Count == 0)
            return null;

        if (CardSelectionUI.Instance == null || SkillContext.IsOfflineCpuResolving)
            return RandomPair(own, opponent);

        var selected = await CardSelectionUI.Instance.ShowAsync(
            own,
            new List<DeckCard>(),
            2,
            rejectSameKind: false,
            letCpuChoose: false,
            rejectSameType: true,
            rejectWhen: (picked, item) => !FitsPair(own, opponent, picked, item));
        if (selected == null || selected.Count < 2)
            return null;

        var first = selected[0].existingCard;
        var second = selected[1].existingCard;
        if (first == null || second == null)
            return null;
        if (TypeOf(first) == CardTypeList.None || TypeOf(first) == TypeOf(second))
            return null;
        if (Targets(opponent, TypeOf(first), TypeOf(second)).Count == 0)
            return null;

        return new List<CardController> { first, second };
    }

    static bool FitsPair(
        List<CardController> own,
        List<CardController> opponent,
        IReadOnlyList<SelectionItem> picked,
        SelectionItem item)
    {
        var card = item?.existingCard;
        if (card == null)
            return false;
        if (picked == null || picked.Count == 0)
            return SecondsFor(card, own, opponent).Count > 0;

        var first = picked[0]?.existingCard;
        if (first == null || card == first)
            return false;

        var seconds = SecondsFor(first, own, opponent);
        for (int i = 0; i < seconds.Count; i++)
        {
            if (seconds[i] == card)
                return true;
        }

        return false;
    }

    static List<CardController> RandomPair(List<CardController> own, List<CardController> opponent)
    {
        var firstOptions = FirstOptions(own, opponent);
        if (firstOptions.Count == 0)
            return null;

        var first = firstOptions[Random.Range(0, firstOptions.Count)];
        var seconds = SecondsFor(first, own, opponent);
        if (seconds.Count == 0)
            return null;

        return new List<CardController> { first, seconds[Random.Range(0, seconds.Count)] };
    }

    static async Task<CardController> ChooseOne(List<CardController> candidates)
    {
        if (candidates == null || candidates.Count == 0)
            return null;
        if (candidates.Count == 1 || CardSelectionUI.Instance == null || SkillContext.IsOfflineCpuResolving)
            return candidates[Random.Range(0, candidates.Count)];

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
