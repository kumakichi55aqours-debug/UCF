using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public partial class BattleManager
{
    private readonly HashSet<CardController> cancelledOpponentMoves = new HashSet<CardController>();
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

    // 技キャンセル適用
    public void HandleMoveCancelled(CardController attacker)
    {
        ClearPendingMoveHit();
        if (attacker == null) return;

        MarkAttackerMoveFinished(attacker);
        ClearTargetHighlights();

        if (IsTargetSelectionMode)
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
        _ = HideNoTargetMessage();
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
    async Task PlayExtraHitsOnEach(CardController attacker, int moveIndex, System.Collections.Generic.IReadOnlyList<CardController> targets)
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
                    await Task.Delay(250);
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
            Transform zone = attacker.isMyCard ? eFieldZone : pFieldZone;
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

                await Task.Delay(400);
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
        await Task.Delay(400);
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
    public async Task FinishMissedMove(CardController attacker, int moveIndex, CardController targetCard, Vector3 missPosition)
    {
        ClearPendingMoveHit();

        await ShowLabelEffect(missPosition, "ミス", new Color(0.82f, 0.82f, 0.9f));
        ClearTargetHighlights();
        await ProcessMoveUsedAfterSkills(attacker, moveIndex, targetCard);

        MarkAttackerMoveFinished(attacker);

        if (IsTargetSelectionMode)
            EndMoveTargetSelection();
    }

    #region 技使用
    // 技使用（オンライン同期含む）
    public async Task UseMoveAsync(CardController attacker, int moveIndex)
    {
        if (!OnlineSyncManager.Instance.CanPlayerOperateCards()) return;
        if (attacker == null || attacker.hasUsedMove || attacker.IsActionBlocked) return;

        bool moveHit = attacker.RollMoveHit();
        int extraHits = moveHit ? MultiHitSkill.RollExtraHits(attacker, moveIndex) : 0;
        SetPendingMoveHit(moveHit, extraHits);

        if (IsOnlineMode)
        {
            if (GetCardIndexInZone(attacker, pBattlerZone) >= 0)
            {
                OnlineSyncManager.Instance.SyncUseMove(attacker, moveIndex, moveHit, extraHits);

                // 自分のカードは直接技使用処理を実行
                await UseMoveRoutine(attacker, moveIndex);
                return;
            }

            ClearPendingMoveHit();
            return;
        }

        await UseMoveRoutine(attacker, moveIndex);
    }

    // 技使用処理
    public async Task UseMoveRoutine(CardController attacker, int moveIndex)
    {
        if (attacker != null && attacker.IsActionBlocked)
        {
            ClearPendingMoveHit();
            return;
        }

        if (IsApplyOpponentMoveCancellation(attacker))
        {
            HandleMoveCancelled(attacker);
            return;
        }

        // ── ダメージ／エネルギー取得 ──
        var battler = attacker.model as BattlerModel;
        int energy = attacker.GetMoveEnergyCost(moveIndex);

        // 「自分」対象の技は、対象選択・発動演出・ダメージ表示を出さずに解決する
        if (battler != null)
        {
            TargetList moveTarget = moveIndex == 0 ? battler.target1 : battler.target2;
            if (moveTarget == TargetList.自分)
            {
                await ResolveSelfTargetMove(attacker, moveIndex, payEnergy: true);
                return;
            }
        }

        TargetList targetType = battler != null
            ? battler.GetMoveTarget(moveIndex)
            : TargetList.None;
        PlayerType moveSide = battler != null
            ? battler.GetMoveSide(moveIndex)
            : PlayerType.プレイヤー;

        if (MoveTargetEvasion.IsConfusedMove(attacker, moveIndex)
            && MoveTargetEvasion.ListZoneBattlers(attacker, PlayerType.両方).Count > 0)
        {
            // 相手側は、宣言した側が選んだ対象の同期を待つ
            if (!IsOnlineMode || attacker.isMyCard)
                await ResolveConfusedMove(attacker, moveIndex, payEnergy: true);
            return;
        }

        if (MoveTargetEvasion.IsMultiSelect(attacker, moveIndex))
        {
            await BeginMultiMoveSelection(attacker, moveIndex);
            return;
        }

        if (MoveTargetEvasion.IsSelectableBattlerTarget(targetType)
            && MoveTargetEvasion.ListZoneBattlers(attacker, moveSide).Count > 0)
        {
            if (MoveTargetEvasion.ListLegalTargets(attacker, moveIndex).Count == 0)
            {
                AbortMoveNoTarget(attacker);
                return;
            }

            OpenSingleMoveTargetChoice(attacker, moveIndex, targetType);
            return;
        }

        // ── 各ゾーン参照 ──
        var enemyFZ = attacker.isMyCard ? eFieldZone : pFieldZone;
        var fieldCards = enemyFZ.GetComponentsInChildren<CardController>();

        // 人数を選ぶ対象は上で処理済み。ここはバトラー全員。
        if (targetType == TargetList.バトラー全員)
        {
            var present = MoveTargetEvasion.ListZoneBattlers(attacker, moveSide);
            if (present.Count > 0)
            {
                var legal = MoveTargetEvasion.LegalBattlers(present, targetType, moveSide);
                if (legal.Count == 0)
                {
                    AbortMoveNoTarget(attacker);
                    return;
                }

                PayMoveEnergy(attacker, energy);

                BeginStunResolveDefer();
                try
                {
                    if (await ConsumeOpponentMoveCancellation(attacker, moveIndex, null))
                        return;

                    await ProcessMoveOnUseSkills(attacker, moveIndex, null);
                    await ApplyDamageToAllEnemyBattlers(attacker, moveIndex, legal);
                }
                finally
                {
                    EndStunResolveDefer();
                }
                return;
            }
        }

        // ── 次点：フィールド ──
        if (fieldCards.Length > 0)
        {
            var targetCard = fieldCards[0];

            PayMoveEnergy(attacker, energy);

            BeginStunResolveDefer();
            try
            {
                if (await ConsumeOpponentMoveCancellation(attacker, moveIndex, targetCard))
                    return;

                await ProcessMoveOnUseSkills(attacker, moveIndex, targetCard);
                if (!ConsumeMoveHit(attacker))
                {
                    await FinishMissedMove(attacker, moveIndex, targetCard, targetCard.transform.position);
                    return;
                }

                int fieldDamage = attacker.ModifyOutgoingMoveDamage(GetMoveDamageAgainst(attacker, targetCard, moveIndex));
                targetCard.curHp -= fieldDamage;

                await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, targetCard.transform, targetCard);
                await ShowDamageEffectToCard(targetCard, fieldDamage, attacker.GetDamageEffectColor(moveIndex, fieldDamage));
                await Task.Delay(800);
                targetCard.UpdateDisplay();
                await PlayExtraHitsOnCard(attacker, targetCard, moveIndex);
                ClearTargetHighlights();

                if (IsOnlineMode && attacker.isMyCard && CardAddress.TryFrom(targetCard, out CardAddress fieldAddress))
                {
                    OnlineSyncManager.Instance.SyncAreaDamage(
                        new[] { fieldAddress.Kind.ToString() },
                        new[] { fieldAddress.Id },
                        new[] { fieldAddress.ZoneName },
                        new[] { fieldAddress.Index },
                        new[] { fieldDamage });
                    await OnlineSyncManager.Instance.WaitForOpponentAnimation();
                }

                // 技使用後スキルの処理
                await ProcessMoveUsedAfterSkills(attacker, moveIndex, targetCard);

                // 破壊チェック
                bool defeatedByMove = false;
                if (targetCard.curHp <= 0)
                {
                    await Task.Delay(1000);
                    defeatedByMove = await HandleGraveyardTransition(targetCard, attacker, moveIndex);
                }

                ResolveStunIfAlive(targetCard);
                MarkAttackerMoveFinished(attacker, resetDamageBonus: false, setHasUsedMove: !defeatedByMove);
                PersistentEffectManager.Instance?.RecalculateImmediately();
            }
            finally
            {
                EndStunResolveDefer();
            }
            return;
        }

        // ── どちらもいないなら直接ダメージ ──
        PayMoveEnergy(attacker, energy);

        BeginStunResolveDefer();
        try
        {
            if (await ConsumeOpponentMoveCancellation(attacker, moveIndex, null))
                return;

            await ProcessMoveOnUseSkills(attacker, moveIndex, null);
            if (!ConsumeMoveHit(attacker))
            {
                Vector3 missPos = attacker.isMyCard ? eFieldZone.position : pFieldZone.position;
                await FinishMissedMove(attacker, moveIndex, null, missPos);
                return;
            }

            ResourceSystem directHp = attacker.isMyCard ? eResources.Hp : pResources.Hp;
            int hpBefore = directHp != null ? directHp.currentHp : 0;
            Transform directHpZone = attacker.isMyCard ? eFieldZone : pFieldZone;
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
            await ProcessMoveUsedAfterSkills(attacker, moveIndex, null);
            MarkAttackerMoveFinished(attacker, resetDamageBonus: false);
        }
        finally
        {
            EndStunResolveDefer();
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

        BeginStunResolveDefer();
        try
        {
            await ProcessMoveOnUseSkills(attacker, moveIndex, attacker);
            await ProcessMoveUsedAfterSkills(attacker, moveIndex, attacker);

            MarkAttackerMoveFinished(attacker);
        }
        finally
        {
            EndStunResolveDefer();
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

        resolvingSelectedMove = true;
        try
        {
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

            BeginStunResolveDefer();
            try
            {
                if (await ConsumeOpponentMoveCancellation(attacker, moveIndex, null))
                    return;

                await ProcessMoveOnUseSkills(attacker, moveIndex, null);
                await ApplyDamageToAllEnemyBattlers(attacker, moveIndex, list, emphasizeTargets: true);
            }
            finally
            {
                EndStunResolveDefer();
            }
        }
        finally
        {
            resolvingSelectedMove = false;
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

        Debug.Log($"[混乱] {attacker.cardName} の対象: {string.Join(", ", picked.ConvertAll(card => card.cardName))}");

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

        BeginStunResolveDefer();
        try
        {
            if (await ConsumeOpponentMoveCancellation(attacker, moveIndex, enemyCard))
                return;

            // 技1/技2使用時の窓を閉じてから技使用時を開く。同時に発火すると後者が入れ子になり、技を受ける時まで窓が残る
            await ProcessMoveOnUseSkills(attacker, moveIndex, enemyCard);
            if (!ConsumeMoveHit(attacker))
            {
                await FinishMissedMove(attacker, moveIndex, enemyCard, enemyCard.transform.position);
                return;
            }

            int finalDamage;
            if (enemyCard.cardKind == CardKind.バトラー)
            {
                var resolved = await ResolveIncomingBattlerDamage(attacker, enemyCard, moveIndex);
                enemyCard = resolved.target;
                finalDamage = resolved.damage;
            }
            else
            {
                int calculatedDamage = GetMoveDamageAgainst(attacker, enemyCard, moveIndex);
                var received = await ProcessDamageReceivedSkills(enemyCard, attacker, moveIndex, ElementList.None, calculatedDamage);
                finalDamage = attacker.ModifyOutgoingMoveDamage(received.Damage);
            }

            // 拡大は「相手の技使用時」の収集より前に再生済み。ここは対象の強調から。
            if (enemyCard != null)
                await CardAnimationManager.Instance.EmphasizeSelectedCards(new[] { enemyCard });
            await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, enemyCard.transform, enemyCard);

            // ── ステップ4: ダメージエフェクトを表示（元パワーと最終ダメージの差で色を決める） ──
            Color effectColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
            await ShowDamageEffectToCard(enemyCard, finalDamage, effectColor);

            Debug.Log("攻撃実行！　ダメージ：" + finalDamage);

            await Task.Delay(800);

            // ── ステップ5: HPにダメージを適用 ──
            enemyCard.curHp -= finalDamage;
            enemyCard.UpdateDisplay();
            await PlayExtraHitsOnCard(attacker, enemyCard, moveIndex);
            ClearTargetHighlights();

            await ProcessMoveUsedAfterSkills(attacker, moveIndex, enemyCard);

            // HP が 0 以下になった場合、墓地へ移動。撃破したときは、技撃破時の前に使用済みにする
            bool defeatedByMove = await HandleGraveyardTransition(enemyCard, attacker, moveIndex);
            ResolveStunIfAlive(enemyCard);
            // 技撃破時に攻撃者自身が墓地へ送られると、相手側では墓地UIの再構築でこの参照が破棄される
            MarkAttackerMoveFinished(attacker, setHasUsedMove: !defeatedByMove);
            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        finally
        {
            EndStunResolveDefer();
        }
    }

    // 選ばれたバトラー、またはバトラー全員にダメージを適用する。
    // 「技を受ける時」は、この一覧のバトラーごとに1回走る。
    public async Task ApplyDamageToAllEnemyBattlers(CardController attacker, int moveIndex, IReadOnlyList<CardController> enemyBattlers, bool emphasizeTargets = false)
    {
        if (enemyBattlers == null)
            enemyBattlers = System.Array.Empty<CardController>();

        if (!ConsumeMoveHit(attacker))
        {
            Vector3 missPos = attacker != null ? attacker.transform.position : Vector3.zero;
            await FinishMissedMove(attacker, moveIndex, null, missPos);
            return;
        }

        if (emphasizeTargets && CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.EmphasizeSelectedCards(enemyBattlers);

        // 各相手バトラーに対してダメージを適用
        foreach (var enemyBattler in enemyBattlers)
        {
            if (enemyBattler == null)
                continue;

            int calculatedDamage = CalculateIncomingMoveDamage(attacker, enemyBattler, moveIndex, out ElementList attackerElement);

            // 技を受ける時のスキルをイベント駆動で処理する
            var received = await ProcessDamageReceivedSkills(enemyBattler, attacker, moveIndex, attackerElement, calculatedDamage);
            int finalDamage = attacker.ModifyOutgoingMoveDamage(received.Damage);

            // ── ステップ3: 矢印→ヒット音→対象の点滅 ──
            await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, enemyBattler.transform, enemyBattler);

            // ── ステップ4: ダメージエフェクトを表示（元パワーと最終ダメージの差で色を決める） ──
            Color effectColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
            await ShowDamageEffectToCard(enemyBattler, finalDamage, effectColor);

            // ── ステップ5: HPにダメージを適用 ──
            if (enemyBattler.cardKind == CardKind.バトラー)
            {
                enemyBattler.curHp -= finalDamage;
                enemyBattler.UpdateDisplay();
            }

            // 各ターゲット間で少し待機
            await Task.Delay(250);
        }

        await PlayExtraHitsOnEach(attacker, moveIndex, enemyBattlers);
        await Task.Delay(800);
        ClearTargetHighlights();

        await ProcessMoveUsedAfterSkills(attacker, moveIndex);

        // 各ターゲットの墓地への移動を処理。撃破したときは、技撃破時の前に使用済みにする
        bool defeatedByMove = false;
        foreach (var enemyBattler in enemyBattlers)
        {
            if (enemyBattler == null)
                continue;

            if (await HandleGraveyardTransition(enemyBattler, attacker, moveIndex))
                defeatedByMove = true;
            ResolveStunIfAlive(enemyBattler);
        }

        MarkAttackerMoveFinished(attacker, setHasUsedMove: !defeatedByMove);
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
    /// 技1／技2使用時のあと、技使用時を処理する。外れた技でも呼ぶ。
    /// </summary>
    async Task ProcessMoveOnUseSkills(CardController attacker, int moveIndex, CardController targetCard)
    {
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

        Debug.Log($"[BattleManager] スキル処理開始を待機: Trigger={trigger}, Source={sourceCard.cardName}");
        await SkillChainManager.Instance.ProcessChainForCollectedSkills(collectedSkills);
        Debug.Log($"[BattleManager] スキル処理完了: Trigger={trigger}, Source={sourceCard.cardName}");
    }

    /// <summary>
    ///「相手の技使用時」のスキルの発動確認。
    /// 収集の前に、技を使うカードの拡大を再生する。
    /// </summary>
    public async Task TriggerOpponentMoveUsedSkills(CardController attacker, int moveIndex, CardController targetCard = null)
    {
        if (attacker == null)
            return;

        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.AnimateSkillActivation(attacker, playMoveSe: true);

        cancelledOpponentMoves.Remove(attacker);

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
            {
                Debug.Log($"[BattleManager] オフライン時、「相手の技使用時」スキルがないため待機をスキップ");
                return;
            }
        }

        if (SkillChainManager.Instance == null)
            return;

        Debug.Log($"[BattleManager] スキル処理開始を待機: Trigger={SkillTrigger.相手の技使用時}, Source={attacker.cardName}");
        await SkillChainManager.Instance.ProcessChainForCollectedSkills(collectedSkills);
        Debug.Log($"[BattleManager] スキル処理完了: Trigger={SkillTrigger.相手の技使用時}, Source={attacker.cardName}");
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

        if (resetDamageBonus)
            attacker.damageBonus = 0;
        if (setHasUsedMove)
            attacker.hasUsedMove = true;
        attacker.GetComponent<BattleCardView>()?.UpdateCanUseMovePanel();
    }

    int stunResolveDeferDepth;
    readonly List<CardController> pendingStunResolves = new List<CardController>();

    /// <summary>
    /// 技の解決が終わるまで、気絶と混乱の解除を保留する。技撃破時スキルのあとにまとめて外す。
    /// </summary>
    void BeginStunResolveDefer()
    {
        stunResolveDeferDepth++;
    }

    void EndStunResolveDefer()
    {
        if (stunResolveDeferDepth > 0)
            stunResolveDeferDepth--;
        if (stunResolveDeferDepth == 0)
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

        if (stunResolveDeferDepth > 0)
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
