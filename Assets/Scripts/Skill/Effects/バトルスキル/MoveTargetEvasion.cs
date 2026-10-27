using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class MoveTargetEvasion
{
    public static bool IsEvading(CardController card)
    {
        return EvadeMoveTargetSkill.IsEvading(card);
    }

    public static bool IsBattlerTarget(TargetList targetType)
    {
        return targetType == TargetList.バトラー1体 || targetType == TargetList.バトラー全員;
    }

    /// <summary>
    /// 人数を指定して選ぶ対象。全員対象は含まない。
    /// </summary>
    public static bool IsSelectableBattlerTarget(TargetList targetType)
    {
        return targetType == TargetList.バトラー1体;
    }

    public static SelectTarget MoveSelect(CardController attacker, int moveIndex)
    {
        var battler = attacker?.model as BattlerModel;
        if (battler == null)
            return new SelectTarget();
        return battler.GetMoveSelect(moveIndex);
    }

    public static bool IsMultiSelect(CardController attacker, int moveIndex)
    {
        var battler = attacker?.model as BattlerModel;
        if (battler == null)
            return false;

        TargetList targetType = battler.GetMoveTarget(moveIndex);
        if (!IsSelectableBattlerTarget(targetType))
            return false;

        return MoveSelect(attacker, moveIndex).Count > 1;
    }

    /// <summary>
    /// 混乱中の、相手側のバトラー1体。対象は両Bゾーンから無作為に選ぶ。
    /// </summary>
    public static bool IsConfusedMove(CardController attacker, int moveIndex)
    {
        if (attacker == null || !attacker.HasStatus(StatusAilmentType.混乱))
            return false;

        var battler = attacker.model as BattlerModel;
        return battler != null
            && battler.GetMoveTarget(moveIndex) == TargetList.バトラー1体
            && battler.GetMoveSide(moveIndex) == PlayerType.相手;
    }

    /// <summary>
    /// 混乱の候補。自分と相手のBゾーンにいる、技対象回避中でないバトラー。
    /// </summary>
    public static List<CardController> ListConfusionCandidates(CardController attacker)
    {
        var legal = ListZoneBattlers(attacker, PlayerType.両方)
            .Where(card => !IsEvading(card))
            .ToList();
        legal.Sort(CompareBoardOrder);
        return legal;
    }

    public static List<CardController> PickConfusedTargets(CardController attacker, int moveIndex)
    {
        var legal = ListConfusionCandidates(attacker);
        var select = MoveSelect(attacker, moveIndex);
        int count = select.Count <= 1
            ? 1
            : select.選択方式 == TargetCountMode.まで
                ? Mathf.Min(select.Count, legal.Count)
                : select.Count;
        if (count <= 0 || count > legal.Count)
            return new List<CardController>();

        for (int i = legal.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var swap = legal[i];
            legal[i] = legal[j];
            legal[j] = swap;
        }

        if (legal.Count > count)
            legal.RemoveRange(count, legal.Count - count);
        return legal;
    }

    /// <summary>
    /// 技を出せるか。
    /// 1体選択で、対象ゾーンにバトラーがいないときはフィールドかプレイヤーHPへ進める。
    /// 複数かつ「ちょうど」で、合法な対象が選択数未満なら出せない。バトラー1体は自分側も数える。
    /// 複数かつ「まで」は、1体もいなければ出せない。
    /// </summary>
    public static bool HasLegalTarget(CardController attacker, int moveIndex)
    {
        if (attacker == null || BattleManager.Instance == null)
            return true;

        var battler = attacker.model as BattlerModel;
        if (battler == null)
            return true;

        if (IsConfusedMove(attacker, moveIndex))
        {
            var present = ListZoneBattlers(attacker, PlayerType.両方);
            int legalCount = present.Count(card => !IsEvading(card));
            return HasRequiredSelection(present.Count, legalCount, MoveSelect(attacker, moveIndex));
        }

        TargetList targetType = battler.GetMoveTarget(moveIndex);
        PlayerType side = battler.GetMoveSide(moveIndex);
        if (!IsBattlerTarget(targetType))
            return true;

        var zone = ListZoneBattlers(attacker, side);
        int legal = LegalBattlers(zone, targetType, side).Count;
        if (IsSelectableBattlerTarget(targetType))
            return HasRequiredSelection(zone.Count, legal, MoveSelect(attacker, moveIndex));

        return ZoneEmptyOrHasLegal(zone.Count, legal);
    }

    public static List<CardController> ListZoneBattlers(CardController attacker, PlayerType side)
    {
        var list = new List<CardController>();
        if (attacker == null || BattleManager.Instance == null)
            return list;

        Transform own = CardZones.Of(CardZoneType.プレイヤーBゾーン, attacker.isMyCard);
        Transform opponent = CardZones.Of(CardZoneType.相手Bゾーン, attacker.isMyCard);

        if (TargetScope.IncludesOwner(side))
            AddBattlers(own, list);
        if (TargetScope.IncludesOpponent(side))
            AddBattlers(opponent, list);

        return list;
    }

    /// <summary>
    /// 回避と攻撃集中を反映した、今選べるバトラー。並びは両クライアントで同じになる。
    /// </summary>
    public static List<CardController> ListLegalTargets(CardController attacker, int moveIndex)
    {
        var battler = attacker?.model as BattlerModel;
        if (attacker == null || battler == null)
            return new List<CardController>();

        TargetList targetType = battler.GetMoveTarget(moveIndex);
        PlayerType side = battler.GetMoveSide(moveIndex);
        var legal = LegalBattlers(ListZoneBattlers(attacker, side), targetType, side);
        legal.Sort(CompareBoardOrder);
        return legal;
    }

    static bool HasRequiredSelection(int presentCount, int legalCount, SelectTarget select)
    {
        if (select.Count <= 1)
            return ZoneEmptyOrHasLegal(presentCount, legalCount);

        return legalCount >= select.MinimumCount;
    }

    /// <summary>
    /// ゾーンにバトラーがいないときは出せる。いるときは、合法な対象が1体以上いるときだけ出せる。
    /// </summary>
    static bool ZoneEmptyOrHasLegal(int presentCount, int legalCount)
    {
        return presentCount == 0 || legalCount > 0;
    }

    static void AddBattlers(Transform zone, List<CardController> list)
    {
        if (zone == null)
            return;

        foreach (var card in zone.GetComponentsInChildren<CardController>())
        {
            if (card == null || card.cardKind != CardKind.バトラー || list.Contains(card))
                continue;
            list.Add(card);
        }
    }

    static int CompareBoardOrder(CardController a, CardController b)
    {
        int zone = ZoneRank(a).CompareTo(ZoneRank(b));
        if (zone != 0)
            return zone;

        int indexA = a != null && a.transform != null ? a.transform.GetSiblingIndex() : 0;
        int indexB = b != null && b.transform != null ? b.transform.GetSiblingIndex() : 0;
        return indexA.CompareTo(indexB);
    }

    static int ZoneRank(CardController card)
    {
        if (card == null || card.transform == null || BattleManager.Instance == null)
            return 2;
        if (card.transform.parent == BattleManager.Instance.pBattlerZone)
            return 0;
        if (card.transform.parent == BattleManager.Instance.eBattlerZone)
            return 1;
        return 2;
    }

    public static List<CardController> LegalBattlers(IEnumerable<CardController> candidates, TargetList targetType, PlayerType side)
    {
        var list = candidates?.Where(card => card != null).ToList() ?? new List<CardController>();
        if (targetType == TargetList.バトラー全員 && side == PlayerType.相手)
            return list;

        var open = list.Where(card => !IsEvading(card)).ToList();
        if (targetType == TargetList.バトラー1体 && side == PlayerType.相手)
            return AttackFocusHelper.RestrictToFocused(open);

        return open;
    }
}
