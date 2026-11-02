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
        bool playedGrantSe = false;

        foreach (var target in targets)
        {
            if (target == null) continue;

            // 再計算や相手側の再実行では鳴らさない。付与を受け取った側は同期側で鳴らす。
            if (!playedGrantSe && (context == null || !context.相手も同じ効果を実行する))
            {
                CardAnimationManager.Instance?.PlayGrantSe();
                playedGrantSe = true;
            }

            var grantedInst = 付与スキル.Clone();
            var grantContext = new SkillContext(target, target, target.transform);
            grantContext.相手も同じ効果を実行する = true;
            await grantedInst.ActivateEffect(grantContext);
            // 解除時スキルは、この端末が付与したときだけ時間切れで実行する。相手には残りターンだけ渡す。
            target.AddPassiveEffect(new ActivePassiveEffect(
                grantedInst, 付与ターン数, sourceName, grantedInst is GrantExpireSkill, context?.発動カード));

            if (!CardAddress.TryAdd(target, kinds, ids, zoneNames, indices)) continue;
            durations.Add(付与ターン数);
            skillTypes.Add(grantedInst.GetType().Name);
            parameters.Add(GrantSkillSerializer.Serialize(grantedInst) ?? string.Empty);
        }

        if (zoneNames.Count > 0 && GrantSkillSyncManager.Instance != null && OnlineSyncManager.Instance?.IsOnlineMode == true)
        {
            string sourceKind = string.Empty;
            int sourceId = 0;
            string sourceZone = string.Empty;
            int sourceIndex = -1;
            if (CardAddress.TryFrom(context?.発動カード, out CardAddress sourceAddress))
            {
                sourceKind = sourceAddress.Kind.ToString();
                sourceId = sourceAddress.Id;
                sourceZone = sourceAddress.ZoneName;
                sourceIndex = sourceAddress.Index;
            }

            GrantSkillSyncManager.Instance.SyncGrantSkillBatch(
                kinds.ToArray(), ids.ToArray(), zoneNames.ToArray(), indices.ToArray(),
                durations.ToArray(), skillTypes.ToArray(), parameters.ToArray(), sourceName,
                sourceKind, sourceId, sourceZone, sourceIndex);
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

    // 付与した側だけ true。相手側の同じタイマーは表示と消去だけを合わせ、時間切れスキルは動かさない。
    public bool RunsOnExpire { get; private set; }

    public CardController SourceCard { get; private set; }

    public ActivePassiveEffect(SkillDB passiveEffect, int duration, string sourceCardName = null, bool runsOnExpire = false, CardController sourceCard = null)
    {
        PassiveEffect = passiveEffect;
        RemainingTurns = duration;
        SourceCardName = sourceCardName;
        RunsOnExpire = runsOnExpire;
        SourceCard = sourceCard;
    }

    public void Tick()
    {
        if (RemainingTurns > 0)
            RemainingTurns--;
    }
}
