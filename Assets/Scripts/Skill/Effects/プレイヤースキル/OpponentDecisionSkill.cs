using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("相手選択(YES_NO)")]
public class OpponentDecisionSkill : SkillDB
{
    const string DefaultMessage = "{SourceCard} の効果で技を無効にできます。無効にしますか？";

    public string メッセージテンプレート;
    public bool オフライン自動許可 = true;
    public string 結果出力キー;

    [SerializeReference] public List<SkillDB> Yesスキル = new List<SkillDB>();
    [SerializeReference] public List<SkillDB> Noスキル = new List<SkillDB>();

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context == null || context.発動カード == null)
        {
            Debug.LogWarning("[OpponentDecisionSkill] SkillContextまたはSourceCardがnullのため処理を中断します。");
            return;
        }

        if (BattleManager.Instance.IsOnlineMode && !context.発動カード.isMyCard)
            return;

        bool proceed = await Decide(context);
        if (!string.IsNullOrEmpty(結果出力キー))
            context.IntVariables[結果出力キー] = proceed ? 1 : 0;

        await ActivateSubSkills(proceed ? Yesスキル : Noスキル, context);
    }

    async Task<bool> Decide(SkillContext context)
    {
        // 選ぶのは発動したカードの相手。オフラインで発動者がCPUなら、その相手はプレイヤー。
        bool opponentIsLocalPlayer = context.発動カード != null && !context.発動カード.isMyCard;
        if (!BattleManager.Instance.IsOnlineMode)
        {
            if (opponentIsLocalPlayer)
                return await AskLocalPlayer(context);

            bool proceed = オフライン自動許可 || UnityEngine.Random.Range(0, 2) == 0;
            Debug.Log($"[OpponentDecisionSkill] オフラインの選択: {(proceed ? "Yes" : "No")}");
            return proceed;
        }

        var sync = OnlineSyncManager.Instance;
        if (sync == null)
        {
            Debug.LogWarning("[OpponentDecisionSkill] OnlineSyncManagerが存在しないため、相手の選択を待ちます。");
            await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            return false;
        }

        string message = DecisionMessage(context);
        return await sync.WhileOpponentDecides(
            () => sync.RequestOpponentYesNoDecision(context.発動カード, message),
            notifyOpponent: true);
    }

    async Task<bool> AskLocalPlayer(SkillContext context)
    {
        var ui = YesOrNoSelectionUI.Instance;
        if (ui == null)
        {
            Debug.LogWarning("[OpponentDecisionSkill] Yes/No UI がないため、選択を行いません。");
            return false;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnlineSyncManager.Instance?.BeginOperationBlock();
        ui.ShowSelectionUI(
            DecisionMessage(context),
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

    string DecisionMessage(SkillContext context)
    {
        string template = string.IsNullOrWhiteSpace(メッセージテンプレート) ? DefaultMessage : メッセージテンプレート;
        return SkillContext.ApplyTemplate(template, context);
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is OpponentDecisionSkill decision)
        {
            decision.Yesスキル = CloneSkillList(Yesスキル);
            decision.Noスキル = CloneSkillList(Noスキル);
        }
    }
}
