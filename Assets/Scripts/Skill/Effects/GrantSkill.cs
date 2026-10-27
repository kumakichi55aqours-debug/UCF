using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[Serializable]
[SkillDisplayName("(付与)")]
public class GrantSkill : SkillDB
{
    public TargetList 効果対象;
    public PlayerType 対象側 = PlayerType.プレイヤー;
    public SearchCriteria 対象条件;
    public int 付与ターン数;

    [SerializeReference]
    public SkillDB 付与スキル;

    public override TargetList GetEffectTarget() => 効果対象;
    public override SearchCriteria GetTargetCriteria() => 対象条件;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (付与スキル == null) return;

        var targets = await CollectTargetsAsync(効果対象, 対象側, 対象条件, context);
        if (targets.Count == 0) return;

        string sourceName = context?.発動カード != null ? context.発動カード.cardName : null;
        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();
        var durations = new List<int>();
        var skillTypes = new List<string>();
        var parameters = new List<string>();

        foreach (var target in targets)
        {
            if (target == null) continue;

            var grantedInst = 付与スキル.Clone();
            await grantedInst.ActivateEffect(new SkillContext(target, target, target.transform));
            target.AddPassiveEffect(new ActivePassiveEffect(grantedInst, 付与ターン数, sourceName));

            if (!CardAddress.TryAdd(target, kinds, ids, zoneNames, indices)) continue;
            durations.Add(付与ターン数);
            skillTypes.Add(grantedInst.GetType().Name);
            parameters.Add(GrantSkillSerializer.Serialize(grantedInst) ?? string.Empty);
        }

        if (zoneNames.Count > 0 && GrantSkillSyncManager.Instance != null && OnlineSyncManager.Instance?.IsOnlineMode == true)
        {
            GrantSkillSyncManager.Instance.SyncGrantSkillBatch(
                kinds.ToArray(), ids.ToArray(), zoneNames.ToArray(), indices.ToArray(),
                durations.ToArray(), skillTypes.ToArray(), parameters.ToArray(), sourceName);
        }
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is GrantSkill granted)
        {
            granted.付与スキル = 付与スキル != null ? 付与スキル.Clone() : null;
        }
    }
}

public class ActivePassiveEffect
{
    public SkillDB PassiveEffect { get; private set; }
    public int RemainingTurns { get; private set; }

    public string SourceCardName { get; private set; }

    public ActivePassiveEffect(SkillDB passiveEffect, int duration, string sourceCardName = null)
    {
        PassiveEffect = passiveEffect;
        RemainingTurns = duration;
        SourceCardName = sourceCardName;
    }

    public void Tick()
    {
        if (RemainingTurns > 0)
            RemainingTurns--;
    }
}
