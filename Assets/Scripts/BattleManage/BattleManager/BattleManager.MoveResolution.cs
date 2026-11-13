using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 宣言した側が決めた、技の解決方法。オンラインでは番号のまま相手へ送る。
/// </summary>
public enum MovePlan
{
    対象なし,
    キャンセル,
    自分,
    単体を選ぶ,
    複数を選ぶ,
    単体,
    複数,
    全員,
    直接ダメージ,
}

public partial class BattleManager
{
    private readonly HashSet<CardController> cancelledOpponentMoves = new HashSet<CardController>();
    readonly Dictionary<CardController, PendingMoveDamageNullification> moveDamageNullifications = new Dictionary<CardController, PendingMoveDamageNullification>();

    struct PendingMoveDamageNullification
    {
        public bool full;
        public int subtract;
    }
    bool hasPendingMoveHit;
    bool pendingMoveHit;
    int pendingExtraHits;

    #region 技キャンセル関連
    // 技使用したカードを特定（技無効時などで使用）
    public CardController ResolveAttackerFromContext(SkillContext context)
    {
        if (context?.Attacker != null) return context.Attacker;
        if (context == null) return null;

        if (context.IntVariables.TryGetValue("OpponentMove_AttackerInstanceId", out int instanceId))
        {
            Transform[] zones = { pBattlerZone, eBattlerZone, pHandZone, eHandZone };
            foreach (var zone in zones)
            {
                if (zone == null) continue;
                foreach (var card in zone.GetComponentsInChildren<CardController>())
                {
                    if (card != null && card.GetInstanceID() == instanceId)
                        return card;
                }
            }
        }

        if (!context.IntVariables.TryGetValue("OpponentMove_AttackerIndex", out int index))
            return null;

        bool belongsToPlayer = context.IntVariables.TryGetValue("OpponentMove_AttackerBelongsToPlayer", out int ownerFlag) && ownerFlag == 1;
        Transform battlerZone = belongsToPlayer ? pBattlerZone : eBattlerZone;
        if (battlerZone == null) return null;

        var cards = battlerZone.GetComponentsInChildren<CardController>();
        if (index < 0 || index >= cards.Length) return null;
        return cards[index];
    }

    // 技キャンセルの登録
    public void RegisterOpponentMoveCancellation(CardController attacker, bool propagate = true)
    {
        if (attacker == null) return;
        if (!cancelledOpponentMoves.Contains(attacker))
            cancelledOpponentMoves.Add(attacker);

        if (propagate && IsOnlineMode)
        {
            OnlineSyncManager.Instance.SyncRegisterOpponentMoveCancellation(attacker);
        }
    }

    // 技キャンセルリストに入ってるか条件判定
    public bool IsApplyOpponentMoveCancellation(CardController attacker)
    {
        return attacker != null && cancelledOpponentMoves.Remove(attacker);
    }

    /// <summary>
    /// 対象カード以外が、この技のダメージを後から無効化する。技が終わるまで残す。
    /// </summary>
    public void RegisterMoveDamageNullification(CardController attacker, DamageNullificationType type, int subtract, bool propagate = true)
    {
        if (attacker == null)
            return;

        moveDamageNullifications.TryGetValue(attacker, out var pending);
        if (type == DamageNullificationType.完全無効化)
            pending.full = true;
        else
            pending.subtract += Mathf.Max(0, subtract);
        moveDamageNullifications[attacker] = pending;

        if (propagate && IsOnlineMode)
            OnlineSyncManager.Instance.SyncRegisterMoveDamageNullification(attacker, (int)type, subtract);
    }

    public int ApplyMoveDamageNullification(CardController attacker, int damage)
    {
        if (attacker == null || !moveDamageNullifications.TryGetValue(attacker, out var pending))
            return damage;
        if (pending.full)
            return 0;
        return Mathf.Max(0, damage - pending.subtract);
    }

    public void ClearMoveDamageNullification(CardController attacker)
    {
        if (attacker != null)
            moveDamageNullifications.Remove(attacker);
    }

    // 技キャンセル適用
    public void HandleMoveCancelled(CardController attacker)
    {
        ClearPendingMoveHit();
        if (attacker == null) return;

        MarkAttackerMoveFinished(attacker);
        ClearTargetHighlights();

        if (selectedAttacker != null)
            EndMoveTargetSelection();
    }
    #endregion

    public void SetPendingMoveHit(bool hit, int extraHits = 0)
    {
        hasPendingMoveHit = true;
        pendingMoveHit = hit;
        pendingExtraHits = Mathf.Max(0, extraHits);
    }

    public void ClearPendingMoveHit()
    {
        hasPendingMoveHit = false;
        pendingExtraHits = 0;
    }

    /// <summary>
    /// 宣言時に決めた追加ヒットを1回分取り出す。無ければ false。
    /// </summary>
    bool TakeExtraHit()
    {
        if (pendingExtraHits <= 0)
            return false;

        pendingExtraHits--;
        return true;
    }

    /// <summary>
    /// バトラーはいるが、技の対象にできるバトラーがいない。エネルギーは払わず、技も使ったことにしない。
    /// </summary>
    void AbortMoveNoTarget(CardController attacker)
    {
        ClearPendingMoveHit();
        if (attacker == null || !attacker.isMyCard || MessageManager.Instance == null)
            return;

        MessageManager.Instance.ShowMessage("技の対象にできるバトラーがいません");
        HideNoTargetMessage().Forget("HideNoTargetMessage");
    }

    async Task HideNoTargetMessage()
    {
        await Task.Delay(1400);
        MessageManager.Instance?.HideMessage();
    }

    /// <summary>
    /// この技の命中判定を一度だけ取り出す。オンラインでは宣言側の結果を共有する。
    /// </summary>
    public bool ConsumeMoveHit(CardController attacker)
    {
        if (hasPendingMoveHit)
        {
            hasPendingMoveHit = false;
            return pendingMoveHit;
        }

        return attacker == null || attacker.RollMoveHit();
    }

    /// <summary>
    /// 命中済みの単体へ、宣言時に決めた追加ヒットを同じ対象へ重ねる。技を受ける時は開かない。
    /// </summary>
    async Task PlayExtraHitsOnCard(CardController attacker, CardController target, int moveIndex)
    {
        try
        {
            while (TakeExtraHit())
            {
                if (!CanKeepStriking(target))
                    break;

                await StrikeAgain(attacker, target, moveIndex);
            }
        }
        finally
        {
            pendingExtraHits = 0;
        }
    }

    /// <summary>
    /// 複数対象の技では、追加1回につき同じ一覧をもう一度殴る。途中で場を離れたカードは飛ばす。
    /// </summary>
    async Task PlayExtraHitsOnEach(CardController attacker, int moveIndex, IReadOnlyList<CardController> targets)
    {
        try
        {
            while (TakeExtraHit())
            {
                if (targets == null)
                    break;

                for (int i = 0; i < targets.Count; i++)
                {
                    CardController target = targets[i];
                    if (!CanKeepStriking(target))
                        continue;

                    await StrikeAgain(attacker, target, moveIndex);
                }
            }
        }
        finally
        {
            pendingExtraHits = 0;
        }
    }

    /// <summary>
    /// プレイヤーHPへの追加ヒット。0になっても、呼び出し側が最後に1回だけ敗北を通知する。
    /// </summary>
    public async Task PlayExtraDirectHits(CardController attacker, int moveIndex)
    {
        try
        {
            if (attacker == null)
                return;

            ResourceSystem hp = attacker.isMyCard ? eResources.Hp : pResources.Hp;
            Transform zone = GetHpTarget(!attacker.isMyCard);
            while (TakeExtraHit())
            {
                if (hp == null)
                    break;

                int baseDamage = GetMoveBaseDamage(attacker, moveIndex);
                int protectedDamage = ApplyHPProtectionToDirectHP(attacker, moveIndex, baseDamage, hp.currentHp);
                int finalDamage = attacker.ModifyOutgoingMoveDamage(protectedDamage);
                if (CardAnimationManager.Instance != null && zone != null)
                    await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, zone, null);

                Color effectColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
                if (attacker.isMyCard)
                    await ShowDamageEffectToEnemyHP(finalDamage, effectColor);
                else
                    await ShowDamageEffectToPlayerHP(finalDamage, effectColor);

                await Task.Delay(800);
                hp.LoseHp(finalDamage);
            }
        }
        finally
        {
            pendingExtraHits = 0;
        }
    }

    async Task StrikeAgain(CardController attacker, CardController target, int moveIndex)
    {
        int damage = ExtraHitDamage(attacker, target, moveIndex);
        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, target.transform, target);

        await ShowDamageEffectToCard(target, damage, attacker.GetDamageEffectColor(moveIndex, damage));
        target.curHp -= damage;
        target.UpdateDisplay();
    }

    int ExtraHitDamage(CardController attacker, CardController target, int moveIndex)
    {
        if (attacker == null || target == null)
            return 0;

        if (target.cardKind == CardKind.バトラー)
        {
            int calculated = CalculateIncomingMoveDamage(attacker, target, moveIndex, out _);
            return attacker.ModifyOutgoingMoveDamage(calculated);
        }

        return attacker.ModifyOutgoingMoveDamage(GetMoveDamageAgainst(attacker, target, moveIndex));
    }

    static bool CanKeepStriking(CardController target)
    {
        if (target == null)
            return false;

        ActiveLocation location = target.GetCurrentActiveLocation();
        return location == ActiveLocation.Bゾーン || location == ActiveLocation.Fゾーン;
    }

    /// <summary>
    /// 技が外れたとき。ダメージと技を受ける時は行わず、技使用後スキルは行う。
    /// </summary>
    public Task FinishMissedMove(CardController attacker, int moveIndex, CardController targetCard, Vector3 missPosition)
    {
        return FinishMissedMove(attacker, moveIndex, targetCard, new[] { missPosition });
    }

    /// <summary>
    /// 複数対象の技が外れたとき。「ミス」は対象それぞれの位置に同時に出す。
    /// </summary>
    async Task FinishMissedMove(CardController attacker, int moveIndex, CardController targetCard, IReadOnlyList<Vector3> missPositions)
    {
        ClearPendingMoveHit();

        var missColor = new Color(0.82f, 0.82f, 0.9f);
        var labels = new List<Task>();
        foreach (var position in missPositions)
            labels.Add(ShowLabelEffect(position, "ミス", missColor));
        await Task.WhenAll(labels);
        ClearTargetHighlights();
        await ProcessMoveUsedAfterSkills(attacker, moveIndex, targetCard);

        MarkAttackerMoveFinished(attacker);

        if (selectedAttacker != null)
            EndMoveTargetSelection();
    }

    #region 技使用
    // 技使用（オンライン同期含む）
    public async Task UseMoveAsync(CardController attacker, int moveIndex)
    {
        if (!OnlineSyncManager.Instance.CanPlayerOperateCards()) return;
        if (IsResolvingMove) return;
        if (attacker == null || attacker.hasUsedMove || attacker.IsActionBlocked) return;
        if (NoGrantMoveSkill.Blocks(attacker, moveIndex)) return;
        if (MoveRestrictionSkill.Blocks(attacker, moveIndex)) return;

        // オンラインで宣言できるのは、自分のBゾーンにいるバトラーだけ
        if (IsOnlineMode && GetCardIndexInZone(attacker, pBattlerZone) < 0) return;

        bool moveHit = attacker.RollMoveHit();
        int extraHits = moveHit ? MultiHitSkill.RollExtraHits(attacker, moveIndex) : 0;
        SetPendingMoveHit(moveHit, extraHits);

        var targets = new List<CardController>();
        MovePlan plan = DecideMovePlan(attacker, moveIndex, targets);
        if (plan == MovePlan.対象なし)
        {
            AbortMoveNoTarget(attacker);
            return;
        }

        if (IsOnlineMode)
            OnlineSyncManager.Instance.SyncUseMove(attacker, moveIndex, moveHit, extraHits, plan, targets);

        await ExecuteMovePlan(attacker, moveIndex, plan, targets);
    }

    /// <summary>
    /// 相手が宣言した技を、届いた解決方法と対象のとおりに解決する。こちらの盤面で使えるかどうかは判定し直さない。
    /// </summary>
    public async Task ResolveRemoteMove(CardController attacker, int moveIndex, MovePlan plan, IReadOnlyList<CardController> targets)
    {
        if (plan == MovePlan.キャンセル)
            cancelledOpponentMoves.Remove(attacker);

        await ExecuteMovePlan(attacker, moveIndex, plan, targets);
    }

    /// <summary>
    /// 宣言した側だけが、技の解決方法と対象を決める。混乱の無作為な対象もここで選ぶ。
    /// 決まった対象は targets に入れる。対象を選ぶ技では空のまま。
    /// </summary>
    MovePlan DecideMovePlan(CardController attacker, int moveIndex, List<CardController> targets)
    {
        if (IsApplyOpponentMoveCancellation(attacker))
            return MovePlan.キャンセル;

        var battler = attacker.model as BattlerModel;
        if (battler == null)
            return MovePlan.直接ダメージ;

        TargetList targetType = battler.GetMoveTarget(moveIndex);
        PlayerType moveSide = battler.GetMoveSide(moveIndex);

        // 「自分」対象の技は、対象選択・発動演出・ダメージ表示を出さずに解決する
        if (targetType == TargetList.自分)
            return MovePlan.自分;

        if (MoveTargetEvasion.IsConfusedMove(attacker, moveIndex)
            && MoveTargetEvasion.ListZoneBattlers(attacker, PlayerType.両方).Count > 0)
        {
            var select = MoveTargetEvasion.MoveSelect(attacker, moveIndex);
            int needed = select.Count <= 1 ? 1 : select.MinimumCount;
            var picked = MoveTargetEvasion.PickConfusedTargets(attacker, moveIndex);
            if (picked.Count < needed)
                return MovePlan.対象なし;

            if (select.Count <= 1)
            {
                targets.Add(picked[0]);
                return MovePlan.単体;
            }

            targets.AddRange(picked);
            return MovePlan.複数;
        }

        if (MoveTargetEvasion.IsMultiSelect(attacker, moveIndex)
            && !MoveTargetEvasion.UpToMoveZoneIsEmpty(attacker, moveIndex))
        {
            var select = MoveTargetEvasion.MoveSelect(attacker, moveIndex);
            var legal = MoveTargetEvasion.ListLegalTargets(attacker, moveIndex);
            if (legal.Count < select.MinimumCount)
                return MovePlan.対象なし;

            bool auto = (select.選択方式 == CountMode.ちょうど && legal.Count == select.Count)
                || (select.選択方式 == CountMode.まで && legal.Count == 1);
            if (!auto)
                return MovePlan.複数を選ぶ;

            targets.AddRange(legal);
            return MovePlan.複数;
        }

        if (MoveTargetEvasion.IsSelectableBattlerTarget(targetType)
            && MoveTargetEvasion.ListZoneBattlers(attacker, moveSide).Count > 0)
        {
            return MoveTargetEvasion.ListLegalTargets(attacker, moveIndex).Count == 0
                ? MovePlan.対象なし
                : MovePlan.単体を選ぶ;
        }

        // 人数を選ぶ対象は上で処理済み。ここはバトラー全員。
        if (targetType == TargetList.バトラー全員)
        {
            var present = MoveTargetEvasion.ListZoneBattlers(attacker, moveSide);
            if (present.Count > 0)
            {
                var legal = MoveTargetEvasion.LegalBattlers(present, targetType, moveSide);
                if (legal.Count == 0)
                    return MovePlan.対象なし;

                targets.AddRange(legal);
                return MovePlan.全員;
            }
        }

        // 次点：フィールド。単体と同じ解決に乗せる。
        var enemyFZ = attacker.isMyCard ? eFieldZone : pFieldZone;
        var fieldCards = enemyFZ.GetComponentsInChildren<CardController>();
        if (fieldCards.Length > 0)
        {
            targets.Add(fieldCards[0]);
            return MovePlan.単体;
        }

        return MovePlan.直接ダメージ;
    }

    /// <summary>
    /// 決まった解決方法で技を解決する。宣言した側と受け取った側で同じ処理を通る。
    /// </summary>
    async Task ExecuteMovePlan(CardController attacker, int moveIndex, MovePlan plan, IReadOnlyList<CardController> targets)
    {
        switch (plan)
        {
            case MovePlan.キャンセル:
                HandleMoveCancelled(attacker);
                return;
            case MovePlan.自分:
                await ResolveSelfTargetMove(attacker, moveIndex, payEnergy: true);
                return;
            case MovePlan.単体を選ぶ:
                var battler = attacker.model as BattlerModel;
                OpenSingleMoveTargetChoice(attacker, moveIndex, battler != null ? battler.GetMoveTarget(moveIndex) : TargetList.バトラー1体);
                return;
            case MovePlan.複数を選ぶ:
                OpenMultiMoveTargetChoice(attacker, moveIndex);
                return;
            case MovePlan.単体:
                CardController target = targets != null && targets.Count > 0 ? targets[0] : null;
                if (target == null)
                {
                    Debug.LogError($"[BattleManager] 技の対象が見つかりません: {attacker.cardName}");
                    ClearPendingMoveHit();
                    return;
                }
                await SelectTargetRoutine(target, attacker, moveIndex, payEnergy: true);
                return;
            case MovePlan.複数:
                await ResolveSelectedMoveTargets(attacker, moveIndex, targets, payEnergy: true);
                return;
            case MovePlan.全員:
                await ResolveAllTargetsMove(attacker, moveIndex, targets);
                return;
            case MovePlan.直接ダメージ:
                await ResolveDirectMove(attacker, moveIndex);
                return;
            default:
                ClearPendingMoveHit();
                return;
        }
    }

    /// <summary>
    /// 「バトラー全員」の技。選ばれた対象の強調はしない。
    /// </summary>
    async Task ResolveAllTargetsMove(CardController attacker, int moveIndex, IReadOnlyList<CardController> targets)
    {
        PayMoveEnergy(attacker, attacker.GetMoveEnergyCost(moveIndex));

        BeginMoveResolution();
        try
        {
            if (await FinishMoveDeclaration(attacker, moveIndex, null))
                return;

            await ApplyDamageToAllEnemyBattlers(attacker, moveIndex, targets);
        }
        finally
        {
            EndMoveResolution();
        }
    }

    /// <summary>
    /// 相手の場にバトラーもフィールドもいないときの、プレイヤーHPへの直接ダメージ。
    /// </summary>
    async Task ResolveDirectMove(CardController attacker, int moveIndex)
    {
        PayMoveEnergy(attacker, attacker.GetMoveEnergyCost(moveIndex));

        BeginMoveResolution();
        try
        {
            if (await FinishMoveDeclaration(attacker, moveIndex, null))
                return;

            if (!ConsumeMoveHit(attacker))
            {
                Vector3 missPos = attacker.isMyCard ? eFieldZone.position : pFieldZone.position;
                await FinishMissedMove(attacker, moveIndex, null, missPos);
                return;
            }

            ResourceSystem directHp = attacker.isMyCard ? eResources.Hp : pResources.Hp;
            int hpBefore = directHp != null ? directHp.currentHp : 0;
            Transform directHpZone = GetHpTarget(!attacker.isMyCard);
            await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, directHpZone, null);
            int directDamageBase = GetMoveBaseDamage(attacker, moveIndex);
            int protectedDamage = ApplyHPProtectionToDirectHP(attacker, moveIndex, directDamageBase, directHp != null ? directHp.currentHp : 0);
            int finalDamage = attacker.ModifyOutgoingMoveDamage(protectedDamage);
            Color directColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
            if (attacker.isMyCard)
                await ShowDamageEffectToEnemyHP(finalDamage, directColor);
            else
                await ShowDamageEffectToPlayerHP(finalDamage, directColor);
            await Task.Delay(800);
            directHp?.LoseHp(finalDamage);
            await PlayExtraDirectHits(attacker, moveIndex);
            if (directHp != null && hpBefore > 0 && directHp.currentHp <= 0)
                directHp.NotifyHpDepleted();
            ClearTargetHighlights();
            MarkAttackerMoveFinished(attacker, resetDamageBonus: false);
            await ProcessMoveUsedAfterSkills(attacker, moveIndex, null);
        }
        finally
        {
            EndMoveResolution();
        }
    }

    /// <summary>
    /// 対象が「自分」の技。対象選択、発動アニメ、ダメージ表示は行わない。
    /// 「相手の技使用時」は集めない。エネルギー消費と技使用時・技使用後のスキルは通常どおり処理する。
    /// </summary>
    public async Task ResolveSelfTargetMove(CardController attacker, int moveIndex, bool payEnergy)
    {
        if (attacker == null) return;
        ClearPendingMoveHit();

        if (payEnergy)
            PayMoveEnergy(attacker, attacker.GetMoveEnergyCost(moveIndex));

        BeginMoveResolution();
        try
        {
            await ProcessMoveOnUseSkills(attacker, moveIndex, attacker, playActivation: false);
            await ProcessMoveUsedAfterSkills(attacker, moveIndex, attacker);

            MarkAttackerMoveFinished(attacker);
        }
        finally
        {
            EndMoveResolution();
        }
    }
    #endregion

    /// <summary>
    /// 選ばれたバトラーへ技を解決する。技使用時は1回、技を受ける時は対象それぞれに走る。
    /// </summary>
    public async Task ResolveSelectedMoveTargets(
        CardController attacker,
        int moveIndex,
        IReadOnlyList<CardController> targets,
        bool payEnergy)
    {
        if (attacker == null)
            return;

        var list = new List<CardController>();
        if (targets != null)
        {
            foreach (var card in targets)
            {
                if (card != null && !list.Contains(card))
                    list.Add(card);
            }
        }

        RetainChosenTargetHighlights(list);

        if (payEnergy)
            PayMoveEnergy(attacker, attacker.GetMoveEnergyCost(moveIndex));

        BeginMoveResolution();
        try
        {
            if (await FinishMoveDeclaration(attacker, moveIndex, null))
                return;

            await ApplyDamageToAllEnemyBattlers(attacker, moveIndex, list, emphasizeTargets: true);
        }
        finally
        {
            EndMoveResolution();
        }
    }

    /// <summary>
    /// 混乱中の、相手側のバトラー1体を、両Bゾーンから無作為に選んで解決する。
    /// </summary>
    public async Task ResolveConfusedMove(CardController attacker, int moveIndex, bool payEnergy)
    {
        if (attacker == null)
            return;

        var select = MoveTargetEvasion.MoveSelect(attacker, moveIndex);
        int needed = select.Count <= 1 ? 1 : select.MinimumCount;
        var picked = MoveTargetEvasion.PickConfusedTargets(attacker, moveIndex);
        if (picked.Count < needed)
        {
            ClearTargetHighlights();
            if (!payEnergy)
                MarkAttackerMoveFinished(attacker, resetDamageBonus: false);
            else
                AbortMoveNoTarget(attacker);
            return;
        }

        if (select.Count <= 1)
        {
            if (IsOnlineMode && attacker.isMyCard)
                OnlineSyncManager.Instance?.SyncSelectEnemyTarget(attacker, picked[0], moveIndex);
            await SelectTargetRoutine(picked[0], attacker, moveIndex, payEnergy);
            return;
        }

        if (IsOnlineMode && attacker.isMyCard)
            OnlineSyncManager.Instance?.SyncSelectMoveTargets(attacker, picked, moveIndex);
        await ResolveSelectedMoveTargets(attacker, moveIndex, picked, payEnergy);
    }

    public async Task SelectTargetRoutine(CardController enemyCard, CardController attacker = null, int moveIndex = -1, bool payEnergy = true)
    {
        // 対象が決まったあとの取り消しは、ダメージ処理まで巻き戻さない
        moveTargetChoiceOpen = false;

        // パラメータが指定されていない場合は、選択された攻撃者を使用
        if (attacker == null) attacker = selectedAttacker;
        if (moveIndex == -1) moveIndex = selectedMoveIndex;

        RetainChosenTargetHighlights(enemyCard != null ? new[] { enemyCard } : null);

        if (payEnergy)
            PayMoveEnergy(attacker, attacker.GetMoveEnergyCost(moveIndex));

        BeginMoveResolution();
        try
        {
            // 拡大→カットイン→技使用時→「相手の技使用時」。技を受ける時はそのさらにあと。
            if (await FinishMoveDeclaration(attacker, moveIndex, enemyCard))
                return;
            if (!ConsumeMoveHit(attacker))
            {
                await FinishMissedMove(attacker, moveIndex, enemyCard, enemyCard.transform.position);
                return;
            }

            int finalDamage;
            bool cancelled;
            if (enemyCard.cardKind == CardKind.バトラー)
            {
                var resolved = await ResolveIncomingBattlerDamage(attacker, enemyCard, moveIndex);
                enemyCard = resolved.target;
                finalDamage = resolved.damage;
                cancelled = resolved.cancelled;
            }
            else
            {
                int calculatedDamage = GetMoveDamageAgainst(attacker, enemyCard, moveIndex);
                var received = await ProcessDamageReceivedSkills(enemyCard, attacker, moveIndex, ElementList.None, calculatedDamage);
                finalDamage = attacker.ModifyOutgoingMoveDamage(received.Damage);
                cancelled = received.Cancelled;
            }

            if (cancelled)
            {
                HandleMoveCancelled(attacker);
                return;
            }

            // 拡大は技使用時より前に再生済み。ここは対象の強調から。
            if (enemyCard != null)
                await CardAnimationManager.Instance.EmphasizeSelectedCards(new[] { enemyCard });
            await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, enemyCard.transform, enemyCard);

            // ── ステップ4: ダメージエフェクトを表示（元パワーと最終ダメージの差で色を決める） ──
            Color effectColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
            await ShowDamageEffectToCard(enemyCard, finalDamage, effectColor);

            await Task.Delay(600);

            // ── ステップ5: HPにダメージを適用 ──
            enemyCard.curHp -= finalDamage;
            enemyCard.UpdateDisplay();
            await PlayExtraHitsOnCard(attacker, enemyCard, moveIndex);
            ClearTargetHighlights();

            MarkAttackerMoveFinished(attacker, setHasUsedMove:true);
            await ProcessMoveUsedAfterSkills(attacker, moveIndex, enemyCard);

            if (enemyCard.curHp <= 0)
                await HandleGraveyardTransition(enemyCard, attacker, moveIndex);

            ResolveStunIfAlive(enemyCard);
            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        finally
        {
            EndMoveResolution();
        }
    }

    // 選ばれたバトラー、またはバトラー全員にダメージを適用する。
    // 「技を受ける時」は、この一覧のバトラーごとに1回走る。攻撃対象を移したときは、移し先でも1回走る。
    public async Task ApplyDamageToAllEnemyBattlers(CardController attacker, int moveIndex, IReadOnlyList<CardController> enemyBattlers, bool emphasizeTargets = false)
    {
        if (enemyBattlers == null)
            enemyBattlers = System.Array.Empty<CardController>();

        if (!ConsumeMoveHit(attacker))
        {
            var missPositions = new List<Vector3>();
            foreach (var enemyBattler in enemyBattlers)
            {
                if (enemyBattler != null)
                    missPositions.Add(enemyBattler.transform.position);
            }
            if (missPositions.Count == 0)
                missPositions.Add(attacker != null ? attacker.transform.position : Vector3.zero);

            await FinishMissedMove(attacker, moveIndex, null, missPositions);
            return;
        }

        if (emphasizeTargets && CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.EmphasizeSelectedCards(enemyBattlers);

        // すでにこの技の対象になっているバトラーへは、攻撃対象を移せない
        var occupied = new HashSet<CardController>();
        foreach (var enemyBattler in enemyBattlers)
        {
            if (enemyBattler != null)
                occupied.Add(enemyBattler);
        }

        // 各相手バトラーに対してダメージを適用
        var hitBattlers = new List<CardController>();
        foreach (var enemyBattler in enemyBattlers)
        {
            if (enemyBattler == null)
                continue;

            var resolved = await ResolveIncomingBattlerDamage(attacker, enemyBattler, moveIndex, occupied);
            if (resolved.cancelled)
            {
                await FinishCancelledMultiTargetMove(attacker, moveIndex, hitBattlers);
                return;
            }

            CardController hit = resolved.target;
            occupied.Add(hit);
            hitBattlers.Add(hit);
            int finalDamage = resolved.damage;

            // ── ステップ3: 矢印→ヒット音→対象の点滅 ──
            await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, hit.transform, hit);

            // ── ステップ4: ダメージエフェクトを表示（元パワーと最終ダメージの差で色を決める） ──
            Color effectColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
            await ShowDamageEffectToCard(hit, finalDamage, effectColor);

            // ── ステップ5: HPにダメージを適用 ──
            if (hit.cardKind == CardKind.バトラー)
            {
                hit.curHp -= finalDamage;
                hit.UpdateDisplay();
            }

            // 各ターゲット間で少し待機
            await Task.Delay(200);
        }

        await PlayExtraHitsOnEach(attacker, moveIndex, hitBattlers);
        await Task.Delay(600);
        ClearTargetHighlights();

        MarkAttackerMoveFinished(attacker, setHasUsedMove: true);
        await ProcessMoveUsedAfterSkills(attacker, moveIndex);

        // 各ターゲットの墓地への移動を処理。撃破したときは、技撃破時の前に使用済みにする
        foreach (var hit in hitBattlers)
        {
            await HandleGraveyardTransition(hit, attacker, moveIndex);
            ResolveStunIfAlive(hit);
        }
        PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    /// <summary>
    /// 複数対象の途中で技キャンセルされたとき。残りの対象と技使用後は行わず、与えたダメージはそのまま残す。
    /// </summary>
    async Task FinishCancelledMultiTargetMove(CardController attacker, int moveIndex, IReadOnlyList<CardController> hitBattlers)
    {
        ClearTargetHighlights();

        foreach (var hit in hitBattlers)
        {
            if (hit == null)
                continue;

            await HandleGraveyardTransition(hit, attacker, moveIndex);
            ResolveStunIfAlive(hit);
        }

        HandleMoveCancelled(attacker);
        PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    void PayMoveEnergy(CardController attacker, int amount)
    {
        if (attacker.isMyCard)
            pResources.Energy.TakeEnergy(amount);
        else
            eResources.Energy.TakeEnergy(amount);
    }

    async Task<bool> ConsumeOpponentMoveCancellation(CardController attacker, int moveIndex, CardController targetCard)
    {
        await TriggerOpponentMoveUsedSkills(attacker, moveIndex, targetCard);
        if (!IsApplyOpponentMoveCancellation(attacker))
            return false;

        HandleMoveCancelled(attacker);
        return true;
    }

    /// <summary>
    /// 拡大・カットイン・技使用時まで処理してから「相手の技使用時」を開く。
    /// キャンセルされたら true。
    /// </summary>
    public async Task<bool> FinishMoveDeclaration(CardController attacker, int moveIndex, CardController targetCard)
    {
        await ProcessMoveOnUseSkills(attacker, moveIndex, targetCard);
        return await ConsumeOpponentMoveCancellation(attacker, moveIndex, targetCard);
    }

    /// <summary>
    /// 技を使うカードの拡大とカットインを出してから、技1／技2使用時、技使用時を処理する。外れた技でも呼ぶ。
    /// 技1/技2使用時の窓を閉じてから技使用時を開く。同時に発火すると後者が入れ子になる。
    /// </summary>
    async Task ProcessMoveOnUseSkills(CardController attacker, int moveIndex, CardController targetCard, bool playActivation = true)
    {
        if (CardAnimationManager.Instance != null)
        {
            if (playActivation)
                await CardAnimationManager.Instance.AnimateSkillActivation(attacker, playMoveSe: true);
            await CardAnimationManager.Instance.PlayMoveCutIn(attacker, moveIndex);
        }

        var specificTrigger = (moveIndex == 0) ? SkillTrigger.技1使用時 : SkillTrigger.技2使用時;
        await ProcessSkillTriggerAndWait(specificTrigger, attacker, targetCard, moveIndex: moveIndex);
        await ProcessSkillTriggerAndWait(SkillTrigger.技使用時, attacker, targetCard, moveIndex: moveIndex);
    }

    /// <summary>
    /// 技使用後スキルを順に処理し、完了まで待つ
    /// </summary>
    public async Task ProcessMoveUsedAfterSkills(CardController attacker, int moveIndex, CardController targetCard = null)
    {
        if (attacker == null) return;

        var specificTrigger = (moveIndex == 0) ? SkillTrigger.技1使用後 : SkillTrigger.技2使用後;
        await ProcessSkillTriggerAndWait(specificTrigger, attacker, targetCard);
        await ProcessSkillTriggerAndWait(SkillTrigger.技使用後, attacker, targetCard);
    }

    /// <summary>
    /// トリガーに該当するスキルを収集してチェーン処理し、完了まで待つ
    /// </summary>
    public async Task ProcessSkillTriggerAndWait(
        SkillTrigger trigger,
        CardController sourceCard,
        CardController moveTarget = null,
        CardZoneType fromLocation = CardZoneType.None,
        int moveIndex = -1)
    {
        if (sourceCard == null) return;

        if (SkillManager.ShouldSkipOfflineOpponentSkill(trigger, sourceCard))
        {
            Debug.Log($"[BattleManager] オフラインのため相手カード起点のスキルをスキップ: Trigger={trigger}, Source={sourceCard.cardName}");
            return;
        }

        var context = SkillContext.WithAttackTarget(sourceCard, moveTarget);
        context.Attacker = sourceCard;
        if (SkillManager.IsMoveDefeatTrigger(trigger))
            context.SetDefeatedOriginalHp(moveTarget);
        context.ProcessingCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (moveIndex >= 0)
            context.MoveIndex = moveIndex;

        var collectedSkills = SkillManager.CollectSkillsForTrigger(
            trigger,
            sourceCard,
            moveTarget,
            context,
            fromLocation);

        bool hasSkills = collectedSkills.TPForcedSkills.Count > 0
            || collectedSkills.TPOptionalSkills.Count > 0
            || collectedSkills.NonTPForcedSkills.Count > 0
            || collectedSkills.NonTPOptionalSkills.Count > 0;

        if (!hasSkills && !IsOnlineMode)
        {
            context.ProcessingCompleteTcs.TrySetResult(true);
            return;
        }

        if (SkillChainManager.Instance == null)
        {
            context.ProcessingCompleteTcs.TrySetResult(true);
            return;
        }

        await SkillChainManager.Instance.ProcessChainForCollectedSkills(collectedSkills);
    }

    /// <summary>
    ///「相手の技使用時」のスキルの発動確認。技使用時スキルの処理が終わってから開く。
    /// </summary>
    public async Task TriggerOpponentMoveUsedSkills(CardController attacker, int moveIndex, CardController targetCard = null)
    {
        if (attacker == null)
            return;

        cancelledOpponentMoves.Remove(attacker);
        ClearMoveDamageNullification(attacker);

        // チェーンが閉じてから戻る。技を無効化するスキルが攻撃者と対象を辿れるよう、位置はコンテキストに残す。
        // 攻撃対象がいないときに、攻撃者自身を対象カードにはしない。
        var skillContext = SkillContext.WithAttackTarget(null, targetCard);
        skillContext.Attacker = attacker;
        skillContext.MoveIndex = moveIndex;
        skillContext.ProcessingCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        int attackerIndex = GetCardIndexInZone(attacker, attacker?.isMyCard == true ? pBattlerZone : eBattlerZone);
        skillContext.IntVariables["OpponentMove_AttackerIndex"] = attackerIndex;
        skillContext.IntVariables["OpponentMove_AttackerBelongsToPlayer"] = attacker?.isMyCard == true ? 1 : 0;
        if (attacker != null)
        {
            skillContext.IntVariables["OpponentMove_AttackerInstanceId"] = attacker.GetInstanceID();
        }

        if (targetCard != null)
        {
            int targetIndex = GetCardIndexInZone(targetCard, targetCard.isMyCard ? pBattlerZone : eBattlerZone);
            skillContext.IntVariables["OpponentMove_TargetIndex"] = targetIndex;
            skillContext.IntVariables["OpponentMove_TargetBelongsToPlayer"] = targetCard.isMyCard ? 1 : 0;
        }

        // 完了通知ではなくチェーン本体が終わるまで待つ。通知だけだと窓が開いたまま次のタイミングが入れ子になる
        var collectedSkills = SkillManager.CollectSkillsForTrigger(
            SkillTrigger.相手の技使用時,
            attacker,
            targetCard,
            skillContext);

        if (!IsOnlineMode && !attacker.isMyCard && !IsMyTurn)
        {
            bool hasSkills = collectedSkills.TPForcedSkills.Count > 0
                || collectedSkills.TPOptionalSkills.Count > 0
                || collectedSkills.NonTPForcedSkills.Count > 0
                || collectedSkills.NonTPOptionalSkills.Count > 0;
            if (!hasSkills)
                return;
        }

        if (SkillChainManager.Instance == null)
            return;

        await SkillChainManager.Instance.ProcessChainForCollectedSkills(collectedSkills);
    }

    /// <summary>
    /// 技を使用済みにする。技撃破時スキルの発動確認より前に呼ぶ。
    /// </summary>
    private static void MarkMoveUsed(CardController attacker)
    {
        MarkAttackerMoveFinished(attacker, resetDamageBonus: false);
    }

    /// <summary>
    /// 技解決の後処理。オンラインでは技撃破時の墓地送り同期で、相手側の CardController が破棄されていることがある。
    /// 撃破時スキルの前に使用済みにした場合は、ここで上書きしない。
    /// </summary>
    private static void MarkAttackerMoveFinished(CardController attacker, bool resetDamageBonus = true, bool setHasUsedMove = true)
    {
        if (attacker == null)
            return;

        BattleManager.Instance?.ClearMoveDamageNullification(attacker);
        if (resetDamageBonus)
            attacker.damageBonus = 0;
        if (setHasUsedMove)
            attacker.hasUsedMove = true;
        attacker.GetComponent<BattleCardView>()?.UpdateCanUseMovePanel();
    }

    int moveResolutionDepth;
    readonly List<CardController> pendingStunResolves = new List<CardController>();

    /// <summary>
    /// エネルギーを払ってから技撃破時スキルが終わるまで真。演出の待ち時間も含む。
    /// このあいだは技の宣言、メニュー、フェイズ操作を受け付けない。
    /// </summary>
    public bool IsResolvingMove => moveResolutionDepth > 0;

    /// <summary>
    /// 技の解決を始める。終わるまで、気絶と混乱の解除を保留する。技撃破時スキルのあとにまとめて外す。
    /// </summary>
    void BeginMoveResolution()
    {
        moveResolutionDepth++;
    }

    void EndMoveResolution()
    {
        if (moveResolutionDepth > 0)
            moveResolutionDepth--;
        if (moveResolutionDepth == 0)
            FlushPendingStunResolves();
    }

    public bool IsStunResolvePending(CardController card)
    {
        return card != null && pendingStunResolves.Contains(card);
    }

    /// <summary>
    /// ダメージを受けたバトラーの気絶と混乱を解く。技の解決中なら、技撃破時のあとまで待つ。
    /// </summary>
    public void RequestStunResolve(CardController card)
    {
        if (card == null || card.cardKind != CardKind.バトラー)
            return;
        if (!card.HasStatus(StatusAilmentType.気絶) && !card.HasStatus(StatusAilmentType.混乱))
            return;

        if (moveResolutionDepth > 0)
        {
            if (!pendingStunResolves.Contains(card))
                pendingStunResolves.Add(card);
            return;
        }

        card.ResolveStunAfterReceivingMove();
    }

    void FlushPendingStunResolves()
    {
        if (pendingStunResolves.Count == 0)
            return;

        var pending = pendingStunResolves.ToList();
        pendingStunResolves.Clear();
        foreach (var card in pending)
        {
            if (card == null)
                continue;
            card.ResolveStunAfterReceivingMove();
        }
    }

    private static void ResolveStunIfAlive(CardController card)
    {
        if (BattleManager.Instance == null)
            return;

        BattleManager.Instance.RequestStunResolve(card);
    }
}
