using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 付与スキルの中身に置く。付与された瞬間は何もせず、付与ターン数が切れたときに実行スキルを動かす。
/// オンラインでは付与と同じ残りターンだけを共有し、実行は付与した側だけが行う。
/// </summary>
[Serializable]
[SkillDisplayName("(付与)解除時")]
public class GrantExpireSkill : SkillDB
{
    [SerializeReference]
    public SkillDB 実行スキル;

    public override Task ActivateEffect(SkillContext context)
    {
        return Task.CompletedTask;
    }

    public bool CanResolveOnExpire => 実行スキル != null;

    public async Task ResolveExpired(CardController holder)
    {
        if (実行スキル == null || holder == null)
            return;

        Debug.Log($"[GrantExpireSkill] 付与時間切れ: {holder.cardName} -> {実行スキル.GetDisplayTypeName()}");

        bool online = BattleManager.Instance != null
            && BattleManager.Instance.IsOnlineMode
            && OnlineSyncManager.Instance != null;
        if (online)
            OnlineSyncManager.Instance.SyncRequestOpponentSkillActivation(holder, "付与解除時");

        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.AnimateSkillActivation(holder);

        if (online)
            await OnlineSyncManager.Instance.WaitForSkillActivationAck();

        var context = new SkillContext(holder, holder, holder.transform);
        await 実行スキル.ActivateEffect(context);

        if (online)
            await OnlineSyncManager.Instance.WaitForAllOpponentAnimations();
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is GrantExpireSkill expire)
            expire.実行スキル = 実行スキル != null ? 実行スキル.Clone() : null;
    }
}
