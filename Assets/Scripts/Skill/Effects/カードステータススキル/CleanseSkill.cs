using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[Flags]
public enum CleanseCategory
{
    なし = 0,
    技使用済み = 1 << 0,
    付与スキル = 1 << 1,
    状態異常 = 1 << 2,
    ターン制限 = 1 << 3,
    属性無効 = 1 << 4,
    HP保護 = 1 << 5,
    パワー修正 = 1 << 6,
}

/// <summary>
/// 対象から、選んだものだけを外す。
/// 付与スキルはカードに後から乗った効果。カード自身の永続効果は残し、外したあと付け直す。
/// </summary>
[Serializable]
[SkillDisplayName("解除")]
public class CleanseSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;

    [Header("候補を集めるときに1体ずつ見る。対象条件とは別")]
    [SerializeReference]
    public List<ISkillCondition> 対象状態 = new List<ISkillCondition>();

    [Tooltip("外したいものを複数選べる。技使用済みは hasUsedMove。状態異常だけなら、下のリストが空のとき全部外す。")]
    public CleanseCategory 解除内容 = CleanseCategory.技使用済み;

    [Tooltip("状態異常を外すとき、ここが空なら全部。種類を入れるとその状態異常だけ。")]
    public List<StatusAilmentType> 解除する状態異常 = new List<StatusAilmentType>();

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;
    public override IReadOnlyList<ISkillCondition> GetTargetState() => 対象状態;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (解除内容 == CleanseCategory.なし)
            return;

        var targets = KeepContextCard(await CollectTargetsAsync(効果対象, 対象側, 対象条件, context, 対象状態), context);

        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();

        foreach (var card in targets)
        {
            if (card == null || !ConditionsMet(対象状態, card))
                continue;

            CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);
            card.ApplyCleanse(解除内容, 解除する状態異常);
            Debug.Log($"{card.cardName} から {解除内容} を解除");
        }

        if (BattleManager.Instance != null
            && BattleManager.Instance.IsOnlineMode
            && zoneNames.Count > 0
            && OnlineSyncManager.Instance != null)
        {
            int[] statusTypes = 解除する状態異常 != null
                ? 解除する状態異常.Select(type => (int)type).ToArray()
                : Array.Empty<int>();
            OnlineSyncManager.Instance.SyncCleanse(
                kinds.ToArray(), ids.ToArray(), zoneNames.ToArray(), indices.ToArray(), (int)解除内容, statusTypes);
        }

        if (CardController.CleanseNeedsRecalc(解除内容))
            PersistentEffectManager.Instance?.QueueRecalculation();
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is CleanseSkill cleanse)
        {
            cleanse.対象状態 = 対象状態 != null
                ? new List<ISkillCondition>(対象状態)
                : new List<ISkillCondition>();
        }
    }
}
