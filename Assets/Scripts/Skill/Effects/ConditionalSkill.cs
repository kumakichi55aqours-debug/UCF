using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 発動条件を満たしたときだけ、続く効果を実行する。
/// 複合スキルの後ろに置き、サーチなどの本効果のあとに追加効果を分岐させる。
/// </summary>
[System.Serializable]
[SkillDisplayName("条件付き")]
public class ConditionalSkill : SkillDB
{
    [Tooltip("条件を満たしたあと、使うかどうかをプレイヤーが選ぶ")]
    public bool 任意;

    [Tooltip("任意のとき、オフラインのCPUは追加効果を使う")]
    public bool オフライン時は使用する = true;

    public string 確認メッセージ;

    [SerializeReference]
    public List<SkillDB> 成立時スキル = new List<SkillDB>();

    [SerializeReference]
    public List<SkillDB> 不成立時スキル = new List<SkillDB>();

    public override async Task ActivateEffect(SkillContext context)
    {
        bool met = ConditionsMet(context);
        if (met && 任意)
            met = await AskOptional(context);

        await ActivateSubSkills(met ? 成立時スキル : 不成立時スキル, context);
    }

    bool ConditionsMet(SkillContext context)
    {
        if (条件 == null || 条件.Count == 0)
            return true;

        foreach (var condition in 条件)
        {
            if (condition == null)
                continue;
            if (!condition.IsConditionMet(context))
                return false;
        }

        return true;
    }

    async Task<bool> AskOptional(SkillContext context)
    {
        if (SkillContext.IsOfflineCpuResolving)
            return オフライン時は使用する;

        if (YesOrNoSelectionUI.Instance == null)
        {
            Debug.LogWarning("[ConditionalSkill] Yes/No UI がないため、追加効果は使いません。");
            return false;
        }

        string sourceName = context != null && context.発動カード != null
            ? context.発動カード.cardName
            : "カード";
        string message = string.IsNullOrWhiteSpace(確認メッセージ)
            ? $"{sourceName} の追加効果を使用しますか？"
            : 確認メッセージ;

        var tcs = new TaskCompletionSource<bool>();
        OnlineSyncManager.Instance?.BeginOperationBlock();
        if (BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode)
            OnlineSyncManager.Instance.SyncBeginOpponentDecisionBlock();

        YesOrNoSelectionUI.Instance.ShowSelectionUI(
            message,
            () =>
            {
                tcs.TrySetResult(true);
                return Task.CompletedTask;
            },
            () =>
            {
                tcs.TrySetResult(false);
                return Task.CompletedTask;
            });

        return await tcs.Task;
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is ConditionalSkill conditional)
        {
            conditional.成立時スキル = CloneSkillList(成立時スキル);
            conditional.不成立時スキル = CloneSkillList(不成立時スキル);
        }
    }
}
