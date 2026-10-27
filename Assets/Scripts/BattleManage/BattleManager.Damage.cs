using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class BattleManager
{
    public async Task ShowLabelEffect(Vector3 targetPosition, string label, Color effectColor)
    {
        try
        {
            DamageEffect effect = SpawnCenteredDamageEffect();
            if (effect == null)
                return;

            effect.PlayLabel(effect.transform.position, targetPosition, label, effectColor);
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            Debug.LogError($"ShowLabelEffectでエラー: {ex.Message}");
        }
    }

    DamageEffect SpawnCenteredDamageEffect()
    {
        DamageEffect prefab = Resources.Load<DamageEffect>("DamageEffect");
        Canvas canvas = FindObjectOfType<Canvas>();
        if (prefab == null || canvas == null)
            return null;

        DamageEffect effect = Instantiate(prefab, canvas.transform);
        RectTransform canvasRect = canvas.transform as RectTransform;
        Vector2 screenCenter = new Vector2(Screen.width / 2f + 255f, Screen.height / 2f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenCenter, null, out Vector2 localPoint);

        RectTransform effectRect = effect.GetComponent<RectTransform>();
        if (effectRect != null)
            effectRect.anchoredPosition = localPoint;
        else if (Camera.main != null)
            effect.transform.position = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height / 2f, 10f));

        return effect;
    }

    #region ダメージエフェクト
    /// <summary>
    /// エフェクトを表示する共通メソッド（ダメージ・回復など）
    /// </summary>
    public async Task ShowEffect(Vector3 targetPosition, int value, Color effectColor = default)
    {
        if (effectColor == default) effectColor = Color.yellow; // デフォルトは黄色（ダメージ）

        try
        {
            DamageEffect effect = SpawnCenteredDamageEffect();
            if (effect == null)
                return;

            effect.PlayEffect(effect.transform.position, targetPosition, value, effectColor);
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            Debug.LogError($"ShowEffectでエラー: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// プレイヤーHPへの直接ダメージエフェクトを表示
    /// </summary>
    public async Task ShowDamageEffectToPlayerHP(int damage, Color effectColor = default)
    {
        Vector3 targetPos = pFieldZone.transform.position;
        await ShowEffect(targetPos, damage, effectColor);
    }

    /// <summary>
    /// 敵HPへの直接ダメージエフェクトを表示
    /// </summary>
    private async Task ShowDamageEffectToEnemyHP(int damage, Color effectColor = default)
    {
        Vector3 targetPos = eFieldZone.transform.position;
        await ShowEffect(targetPos, damage, effectColor);
    }

    /// <summary>
    /// カードへのダメージエフェクトを表示
    /// </summary>
    private async Task ShowDamageEffectToCard(CardController targetCard, int damageAmount, Color effectColor = default)
    {
        Vector3 targetPos = targetCard.transform.position;
        if (effectColor == default) effectColor = Color.yellow;
        await ShowEffect(targetPos, damageAmount, effectColor);
    }
    #endregion

    /// <summary>
    /// ダメージ計算と適用の共通処理
    /// </summary>
    public async Task<int> CalculateAndApplyDamage(CardController attacker, CardController target, int moveIndex, int baseDamage)
    {
        int finalDamage = baseDamage + CategoryDamageUpSkill.GetBonus(attacker, target, moveIndex);

        if (target.cardKind == CardKind.バトラー)
        {
            var resolved = await ResolveIncomingBattlerDamage(attacker, target, moveIndex);
            target = resolved.target;
            finalDamage = resolved.damage;
            target.curHp -= finalDamage;
        }
        else if (target.cardKind == CardKind.フィールド)
        {
            finalDamage = attacker.ModifyOutgoingMoveDamage(finalDamage);
            target.curHp -= finalDamage;
        }
        
        target.UpdateDisplay();
        return finalDamage;
    }

    /// <summary>
    /// バトラーへの技ダメージを決め、攻撃対象を移すスキルがあれば移し先で計算し直す。
    /// 同じバトラーの「技を受ける時」は、この攻撃では一度だけ処理する。
    /// </summary>
    async Task<(CardController target, int damage)> ResolveIncomingBattlerDamage(CardController attacker, CardController target, int moveIndex)
    {
        var resolved = new HashSet<CardController>();
        int damage = 0;

        while (target != null)
        {
            int calculated = CalculateIncomingMoveDamage(attacker, target, moveIndex, out ElementList element);
            if (!resolved.Add(target))
            {
                damage = calculated;
                break;
            }

            var received = await ProcessDamageReceivedSkills(target, attacker, moveIndex, element, calculated);
            damage = received.Damage;
            if (!IsRedirectDestination(target, received.RedirectedTarget))
                break;

            Debug.Log($"[BattleManager] 攻撃対象を移す: {target.cardName} -> {received.RedirectedTarget.cardName}");
            target = received.RedirectedTarget;
        }

        if (attacker != null)
            damage = attacker.ModifyOutgoingMoveDamage(damage);
        return (target, damage);
    }

    int CalculateIncomingMoveDamage(CardController attacker, CardController target, int moveIndex, out ElementList attackerElement)
    {
        int baseDamage = GetMoveDamageAgainst(attacker, target, moveIndex);
        attackerElement = ElementList.None;
        if (target == null || target.cardKind != CardKind.バトラー)
            return baseDamage;

        var battler = attacker != null ? attacker.model as BattlerModel : null;
        var targetBattler = target.model as BattlerModel;
        if (battler == null || targetBattler == null)
            return baseDamage;

        attackerElement = moveIndex == 0 ? battler.moveElement1 : battler.moveElement2;
        int calculated = baseDamage + attacker.CalculateElementDamageModification(attackerElement, targetBattler, moveIndex);
        if (CheckInvalidPassive(target, attackerElement))
            calculated = 0;
        else if (CheckHPProtection(attacker, target, moveIndex, attackerElement))
        {
            int maxAllowedDamage = target.curHp - 1;
            if (calculated > maxAllowedDamage)
                calculated = maxAllowedDamage;
        }

        return ApplyDamageModifier(target, attacker, moveIndex, attackerElement, calculated);
    }

    bool IsRedirectDestination(CardController from, CardController to)
    {
        if (from == null || to == null || from == to)
            return false;
        if (to.cardKind != CardKind.バトラー || to.isMyCard != from.isMyCard)
            return false;
        if (MoveTargetEvasion.IsEvading(to))
            return false;
        if (!TargetSelectionManager.IsAttackTargetOnField(to))
            return false;

        Transform zone = to.isMyCard ? pBattlerZone : eBattlerZone;
        return to.transform.parent == zone;
    }

    private async Task<(int Damage, CardController RedirectedTarget)> ProcessDamageReceivedSkills(CardController defender, CardController attacker, int moveIndex, ElementList attackerElement, int calculatedDamage)
    {
        var damageContext = new SkillContext(defender, defender, defender.transform);
        damageContext.SetDamageInfo(calculatedDamage, attackerElement, moveIndex, attacker);

        if (IsOnlineMode)
        {
            var collectedSkills = SkillManager.CollectSkillsForTrigger(
                SkillTrigger.技を受ける時,
                defender,
                attacker,
                damageContext);
            if (SkillChainManager.Instance != null)
            {
                Debug.Log($"[BattleManager] スキル処理開始を待機: Trigger={SkillTrigger.技を受ける時}, Source={defender.cardName}");
                await SkillChainManager.Instance.ProcessChainForCollectedSkills(collectedSkills);
            }

            int finalDamage = damageContext.FinalDamage ?? calculatedDamage;
            Debug.Log($"[BattleManager] スキル処理完了: Trigger={SkillTrigger.技を受ける時}, Source={defender.cardName}, 最終ダメージ={finalDamage}");
            return (finalDamage, damageContext.移した攻撃対象);
        }

        var offlineSkills = SkillManager.CollectSkillsForTrigger(
            SkillTrigger.技を受ける時,
            defender,
            attacker,
            damageContext);
        bool hasSkills = offlineSkills.TPForcedSkills.Count > 0
            || offlineSkills.TPOptionalSkills.Count > 0
            || offlineSkills.NonTPForcedSkills.Count > 0
            || offlineSkills.NonTPOptionalSkills.Count > 0;
        if (!hasSkills)
        {
            Debug.Log($"[BattleManager] オフライン時、「技を受ける時」スキルがないため待機をスキップ");
            return (calculatedDamage, null);
        }

        if (SkillChainManager.Instance != null)
            await SkillChainManager.Instance.ProcessChainForCollectedSkills(offlineSkills);

        int offlineDamage = damageContext.FinalDamage ?? calculatedDamage;
        Debug.Log($"[BattleManager] 技を受ける時スキル処理完了: 最終ダメージ={offlineDamage}");
        return (offlineDamage, damageContext.移した攻撃対象);
    }

    /// <summary>
    /// HP保護スキルが有効かどうかをチェック
    /// </summary>
    private bool CheckHPProtection(CardController attacker, CardController target, int moveIndex, ElementList attackerElement)
    {
        // 攻撃者がHP保護スキルを持っているかチェック
        bool hasProtection1 = moveIndex == 0 && attacker.hpProtection1;
        bool hasProtection2 = moveIndex == 1 && attacker.hpProtection2;
        
        if (!hasProtection1 && !hasProtection2)
            return false;
        
        // 属性チェック
        ElementList protectionElement = moveIndex == 0 ? attacker.hpProtectionTargetElement1 : attacker.hpProtectionTargetElement2;
        
        // Noneの場合は全属性に適用、それ以外は指定属性のみ
        if (protectionElement != ElementList.None && protectionElement != attackerElement)
            return false;
        
        return true;
    }

    /// <summary>
    /// InvailedPassiveスキルが有効かどうかをチェック
    /// </summary>
    private bool CheckInvalidPassive(CardController target, ElementList attackerElement)
    {
        // 対象の属性無効化効果をチェック
        return target.ShouldInvalidateDamage(attackerElement);
    }

    /// <summary>
    /// 直接HP攻撃時のHP保護スキル適用
    /// </summary>
    public int ApplyHPProtectionToDirectHP(CardController attacker, int moveIndex, int baseDamage, int currentHP)
    {
        var battler = attacker.model as BattlerModel;
        ElementList attackerElement = moveIndex == 0 ? battler.moveElement1 : battler.moveElement2;
        
        // HP保護スキルのチェック
        bool hasHPProtection = CheckHPProtection(attacker, null, moveIndex, attackerElement);
        if (hasHPProtection)
        {
            // HP保護が有効な場合、HPが1を下回らないように制限
            int maxAllowedDamage = currentHP - 1;
            if (baseDamage > maxAllowedDamage)
            {
                Debug.Log($"HP保護スキル発動（直接HP攻撃）: ダメージを{maxAllowedDamage}に制限（元のダメージ: {baseDamage}）");
                return maxAllowedDamage;
            }
        }
        
        return baseDamage;
    }

    /// <summary>
    /// HPが0以下の場合の墓地移動処理の共通メソッド（技による攻撃用）
    /// </summary>
    public async Task<bool> HandleGraveyardTransition(CardController targetCard, CardController attacker, int moveIndex)
    {
        bool defeatedByMove = false;
        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.PlayDefeatExplosion(targetCard);

        if (targetCard.cardKind == CardKind.バトラー)
        {
            BattlerModel targetBattler = (BattlerModel)targetCard.model;
            if (targetCard.curHp <= 0)
            {
                // HPを最大HPにリセットしてから墓地に送る
                targetCard.RemoveAllPassiveEffects();
                targetCard.ClearStatusAilments(keepStun: true);
                targetCard.curHp = targetBattler.maxHp;
                targetCard.UpdateDisplay();
                await DiscardToOwnGrave(targetCard, attacker);

                // 技使用後、技撃破時の発動確認より前に使用済みにする
                MarkMoveUsed(attacker);
                defeatedByMove = true;

                var defeatTrigger = (moveIndex == 0) ? SkillTrigger.技1撃破時 : SkillTrigger.技2撃破時;
                await ProcessSkillTriggerAndWait(defeatTrigger, attacker, targetCard);
                await ProcessSkillTriggerAndWait(SkillTrigger.技撃破時, attacker, targetCard);

                // 攻撃されたカードの所有者に応じてダメージを与える
                if (targetCard.isMyCard)
                    pResources.Hp.TakeDamage(5);
                else
                    eResources.Hp.TakeDamage(5);
            }
        }
        else if (targetCard.cardKind == CardKind.フィールド)
        {
            FieldModel targetField = (FieldModel)targetCard.model;
            if (targetCard.curHp <= 0)
            {
                // HPを最大HPにリセットしてから墓地に送る
                targetCard.curHp = targetField.maxHp;
                targetCard.UpdateDisplay();
                await DiscardToOwnGrave(targetCard, attacker);
            }
        }
        
        if (attacker != null)
            attacker.GetComponent<BattleCardView>()?.UpdateCanUseMovePanel();

        if (selectedAttacker != null)
            EndMoveTargetSelection();
        return defeatedByMove;
    }

    async Task DiscardToOwnGrave(CardController targetCard, CardController attacker)
    {
        Transform graveZone = targetCard.isMyCard ? pGraveZone : eGraveZone;
        CardZoneType fromLocation = CardController.ZoneTypeFromTransform(targetCard.transform.parent);
        await CardAnimationManager.Instance.AnimateDiscard(
            targetCard.transform.parent as RectTransform,
            graveZone as RectTransform,
            targetCard,
            suppressSkillTrigger: true);
        await ProcessSkillTriggerAndWait(SkillTrigger.墓地に送られた時, targetCard, attacker, fromLocation);
    }

    /// <summary>
    /// HPが0以下の場合の墓地移動処理の共通メソッド（スキル効果用）
    /// </summary>
    public async Task HandleGraveyardTransition(CardController targetCard, bool syncOnline = true)
    {
        if (targetCard == null || targetCard.curHp > 0)
            return;
        if (targetCard.cardKind != CardKind.バトラー && targetCard.cardKind != CardKind.フィールド)
            return;

        targetCard.RemoveAllPassiveEffects();
        targetCard.ClearStatusAilments(IsStunResolvePending(targetCard));

        if (CardAnimationManager.Instance != null)
            await CardAnimationManager.Instance.PlayDefeatExplosion(targetCard);

        // 墓地へ動かしたあとの番号はずれるので、送る前の並びを取る
        CardAddress graveAddress = default;
        bool hasGraveAddress = false;
        if (syncOnline)
            hasGraveAddress = CardAddress.TryFrom(targetCard, out graveAddress);

        // HPが0以下のまま墓地へ送る。最大HPへ戻すのは、同期の判定が終わったあと
        var targetGraveZone = targetCard.isMyCard ? pGraveZone : eGraveZone;
        RectTransform currentSlot = targetCard.transform.parent as RectTransform;
        RectTransform graveSlot = targetGraveZone as RectTransform;
        await CardAnimationManager.Instance.AnimateDiscard(currentSlot, graveSlot, targetCard);

        if (IsOnlineMode && syncOnline && hasGraveAddress)
        {
            OnlineSyncManager.Instance.SyncAreaGrave(
                new[] { graveAddress.Kind.ToString() },
                new[] { graveAddress.Id },
                new[] { graveAddress.ZoneName },
                new[] { graveAddress.Index });
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
        }

        if (targetCard.cardKind == CardKind.バトラー && targetCard.model is BattlerModel battler)
            targetCard.curHp = battler.maxHp;
        else if (targetCard.cardKind == CardKind.フィールド && targetCard.model is FieldModel field)
            targetCard.curHp = field.maxHp;
        targetCard.UpdateDisplay();
    }

    /// <summary>
    /// 攻撃エフェクト処理（ダメージ計算以外）
    /// </summary>
    public async Task ProcessAttackEffects(CardController attacker, CardController target, int moveIndex, int finalDamage)
    {
        await CardAnimationManager.Instance.AnimateSkillActivation(attacker, playMoveSe: true);
        await CardAnimationManager.Instance.PlayMoveImpact(attacker.transform, target.transform, target);

        Color effectColor = attacker.GetDamageEffectColor(moveIndex, finalDamage);
        DamageEffect dmgEffect = SpawnCenteredDamageEffect();
        if (dmgEffect != null)
            dmgEffect.PlayEffect(dmgEffect.transform.position, target.transform.position, finalDamage, effectColor);

        Debug.Log("攻撃実行！　ダメージ：" + finalDamage);

        await Task.Delay(1000);
        ClearTargetHighlights();

        BeginStunResolveDefer();
        try
        {
            await ProcessMoveUsedAfterSkills(attacker, moveIndex, target);

            // HP が 0 以下になった場合、墓地へ移動。撃破したときは、技撃破時の前に使用済みにする
            bool defeatedByMove = await HandleGraveyardTransition(target, attacker, moveIndex);
            ResolveStunIfAlive(target);
            MarkAttackerMoveFinished(attacker, setHasUsedMove: !defeatedByMove);
        }
        finally
        {
            EndStunResolveDefer();
        }
    }

    private async void HandlePlayerDefeat()
    {
        Debug.Log("プレイヤーの敗北！");
        Color loseColor = new Color(0.902f, 0.082f, 0.243f, 1f); // #e6153e
        await ShowGameResultMessage("YOU LOSE", loseColor);
    }

    private async void HandleEnemyDefeat()
    {
        Debug.Log("プレイヤーの勝利！");
        Color winColor = new Color(0.290f, 0.467f, 1.000f, 1f); // #4a77ff
        await ShowGameResultMessage("YOU WIN", winColor);
    }

    /// <summary>
    /// ゲーム結果メッセージを表示してタイトルに戻る
    /// </summary>
    private async Task ShowGameResultMessage(string message, Color messageColor)
    {
        // プレハブの読み込みと Canvas の取得
        DamageEffect dmgEffectPrefab = Resources.Load<DamageEffect>("DamageEffect");
        Canvas canvas = FindObjectOfType<Canvas>();
        
        if (dmgEffectPrefab == null || canvas == null) return;

        // Canvas の直下に DamageEffect を生成
        DamageEffect resultEffect = Instantiate(dmgEffectPrefab, canvas.transform);

        // 生成された DamageEffect の RectTransform を取得
        RectTransform effectRect = resultEffect.GetComponent<RectTransform>();
        if (effectRect != null)
        {
            // Canvasの中央に配置（x座標を+255にずらす）
            effectRect.anchoredPosition = new Vector2(255f, 0f);
            // アンカーを中央に設定
            effectRect.anchorMin = new Vector2(0.5f, 0.5f);
            effectRect.anchorMax = new Vector2(0.5f, 0.5f);
            effectRect.pivot = new Vector2(0.5f, 0.5f);
        }

        // 勝敗メッセージを表示（x座標を+255にずらした位置に表示）
        resultEffect.PlayGameResultEffect(new Vector2(255f, 0f), message, messageColor);

        // 2秒待機（DamageEffectのアニメーション完了を待つ）
        await Task.Delay(2000);

        // タイトルシーンに戻る
        ReturnToTitle();
    }

    /// <summary>
    /// タイトルシーンに戻る処理
    /// </summary>
    private void ReturnToTitle()
    {
        // オンラインルームにいるなら、まずルームを抜ける
        if (IsOnlineMode && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LeaveRoom();
        }
        else
        {
            // オフライン、または既にルームを出ているならタイトルへ
            LoadTitleScene();
        }
    }

    /// <summary>
    /// タイトルシーンをロード
    /// </summary>
    public void LoadTitleScene()
    {
        // DontDestroyOnLoadしているPhotonViewが残るのを防ぐため、明示的に破棄
        if (GrantSkillSyncManager.Instance != null)
        {
            Destroy(GrantSkillSyncManager.Instance.gameObject);
        }

        // Disconnect中にシーン遷移するとSetPropertiesエラーが出るため、先に無効化
        PhotonNetwork.AutomaticallySyncScene = false;

        // オンライン設定・OfflineMode を明示的にリセット
        PhotonNetwork.OfflineMode = false;
        // 必要なら Disconnect() を呼ぶ
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        BattleLaunchContext.LoadReturnScene();
    }

    private int ApplyDamageModifier(CardController target, CardController attacker, int moveIndex, ElementList attackerElement, int originalDamage)
    {
        int finalDamage = originalDamage;
        if (target.skills != null)
        {
            foreach (var skillData in target.skills)
                finalDamage = ApplyOneDamageModifier(skillData as DamageModifierSkill, target, attacker, moveIndex, attackerElement, finalDamage);
        }

        if (target.passiveEffects != null)
        {
            foreach (var passiveEffect in target.passiveEffects)
                finalDamage = ApplyOneDamageModifier(passiveEffect.PassiveEffect as DamageModifierSkill, target, attacker, moveIndex, attackerElement, finalDamage);
        }

        return IncomingDamageModifierSkill.Apply(target, finalDamage);
    }

    static int ApplyOneDamageModifier(
        DamageModifierSkill skill,
        CardController target,
        CardController attacker,
        int moveIndex,
        ElementList attackerElement,
        int damage)
    {
        if (skill == null)
            return damage;

        var conditionContext = new SkillContext(target, target, target.transform);
        conditionContext.SetDamageInfo(damage, attackerElement, moveIndex, attacker);
        if (!skill.CanActivate(conditionContext))
            return damage;
        if (!skill.IsApplicable(attackerElement, moveIndex + 1, attacker, target))
            return damage;

        return skill.CalculateModifiedDamage(damage, attacker, moveIndex + 1, attackerElement, target);
    }
}
