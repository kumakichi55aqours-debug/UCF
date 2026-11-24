using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    /// <summary>
    /// タクティクススキル処理完了のRPC同期
    /// </summary>
    public void SyncTacticsSkillCompleted(CardController card)
    {
        TrySendCardRpc("ReceiveTacticsSkillCompleted", card);
    }

    public void SyncTacticsSkillCompleted(CardAddress address)
    {
        if (!address.IsValid)
            return;

        SendCardRpc("ReceiveTacticsSkillCompleted", address);
    }

    /// <summary>
    /// 相手に、宣言済み起動効果のチェーン窓へ空の候補で参加するよう頼む
    /// </summary>
    public void SyncJoinIgnitionChain()
    {
        SendRPC("ReceiveJoinIgnitionChain");
    }

    /// <summary>
    /// 受信: 相手の起動効果チェーンに、自分の起動効果は足さず参加する
    /// </summary>
    [PunRPC]
    private void ReceiveJoinIgnitionChain()
    {
        if (!IsOnlineMode) return;
        SkillChainManager.Instance?.JoinIgnitionWindow();
    }

    /// <summary>
    /// 相手に、フェイズ開始時・終了時のチェーン窓へ参加するよう頼む。
    /// 自分のフェイズの窓なら、相手は相手のフェイズのスキルを候補に載せる。
    /// </summary>
    public void SyncJoinPhaseChain(SkillTrigger trigger, TurnPhase phase)
    {
        SendRPC("ReceiveJoinPhaseChain", (int)trigger, (int)phase);
    }

    /// <summary>
    /// 受信: 相手が開いたフェイズの窓に参加する
    /// </summary>
    [PunRPC]
    private void ReceiveJoinPhaseChain(int triggerInt, int phaseInt)
    {
        if (!IsOnlineMode) return;
        SkillChainManager.Instance?.JoinPhaseWindow((SkillTrigger)triggerInt, (TurnPhase)phaseInt);
    }

    /// <summary>
    /// こちらに自分のフェイズの候補が無いとき、相手のフェイズの候補があれば相手に窓を開いてもらう。
    /// </summary>
    public async Task RequestOpponentPhaseChain(SkillTrigger trigger, TurnPhase phase)
    {
        if (!IsOnlineMode || !SkillManager.IsOwnPhaseTrigger(trigger))
            return;

        opponentPhaseSkillsDone.Reset();
        SendRPC("ReceiveOpponentPhaseChain", (int)trigger, (int)phase);
        await opponentPhaseSkillsDone.WaitForCompletion();
    }

    /// <summary>
    /// 受信: 相手のフェイズに、自分の「相手のフェイズ」スキルがあれば窓を開く
    /// </summary>
    [PunRPC]
    private void ReceiveOpponentPhaseChain(int triggerInt, int phaseInt)
    {
        if (!IsOnlineMode)
            return;

        RespondToOpponentPhaseChain((SkillTrigger)triggerInt, (TurnPhase)phaseInt)
            .Forget("相手のフェイズスキルの確認");
    }

    async Task RespondToOpponentPhaseChain(SkillTrigger trigger, TurnPhase phase)
    {
        try
        {
            if (!SkillManager.IsOwnPhaseTrigger(trigger) || SkillChainManager.Instance == null)
                return;

            var context = new SkillContext(null, null, null);
            var collected = SkillManager.CollectSkillsForTrigger(
                SkillManager.ToOpponentPhaseTrigger(trigger),
                null,
                null,
                context,
                phase: phase);
            if (!SkillManager.HasCollectedSkills(collected))
                return;

            collected.InvitePhasePartner = true;
            context.ProcessingCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await SkillChainManager.Instance.ProcessChainForCollectedSkills(collected);
            await SkillChainManager.Instance.WaitUntilIdle();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] 相手のフェイズスキルの確認でエラー: {ex.Message}");
        }
        finally
        {
            SendRPC("ReceiveOpponentPhaseChainDone");
        }
    }

    [PunRPC]
    private void ReceiveOpponentPhaseChainDone()
    {
        opponentPhaseSkillsDone.SetComplete();
    }

    /// <summary>
    /// 相手にスキル発動アニメーションの実行を要求（ACK同期）
    /// </summary>
    public void SyncRequestOpponentSkillActivation(CardController card, string triggerStr)
    {
        if (!IsOnlineMode)
            return;

        skillActivationAck.Reset();
        if (!TrySendCardRpc("ReceiveRequestOpponentSkillActivation", card, triggerStr))
        {
            // 住所が取れないと相手へ要求を送れない。完了扱いにしないと WaitForSkillActivationAck で操作不能になる。
            Debug.LogWarning("[OnlineSyncManager] スキル発動アニメーションの送信先が決まらないため、完了扱いにします。");
            skillActivationAck.SetComplete();
        }
    }

    /// <summary>
    /// 受信: 相手からスキル発動アニメーション要求
    /// </summary>
    [PunRPC]
    private void ReceiveRequestOpponentSkillActivation(string cardKindStr, int cardID, string zoneName, int zoneIndex, string triggerStr)
    {
        if (!IsOnlineMode) return;

        CardController targetCard = null;
        try
        {
            targetCard = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex)
                ?? FindChainTacticsInFlight(cardKindStr, cardID);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveRequestOpponentSkillActivationでエラー: {ex.Message}");
        }

        if (targetCard != null)
            _ = AnimateAndAckSkillActivation(targetCard);
        else
        {
            Debug.LogError("[OnlineSyncManager] スキル発動アニメーションの対象カードが見つかりません。");
            SendRPC("ReceiveSkillActivationAck");
        }
    }

    private async Task AnimateAndAckSkillActivation(CardController targetCard)
    {
        try
        {
            BeginOperationBlock();
            await CardAnimationManager.Instance.AnimateSkillActivation(targetCard);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] AnimateAndAckSkillActivationでエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
            // 送信者にACKを返す
            SendRPC("ReceiveSkillActivationAck");
        }
    }

    /// <summary>
    /// ACK受信: 相手側のスキル発動アニメーション完了
    /// </summary>
    [PunRPC]
    private void ReceiveSkillActivationAck()
    {
        skillActivationAck.SetComplete();
    }

    /// <summary>
    /// 相手スキル発動アニメーションのACKを待機
    /// </summary>
    public async Task WaitForSkillActivationAck()
    {
        try
        {
            await skillActivationAck.WaitForCompletion();
        }
        finally
        {
            skillActivationAck.Reset();
        }
    }

    /// <summary>
    /// スキルで選ばれたカードの強調を、相手の画面でも再生する。
    /// </summary>
    public void SyncSelectedTargetEmphasis(IReadOnlyList<CardController> cards)
    {
        if (!IsOnlineMode || cards == null)
            return;

        var packed = CardAddress.Pack(cards);
        if (packed.IsEmpty)
            return;

        SendAnimatedRpc(
            "ReceiveSelectedTargetEmphasis",
            packed.Kinds,
            packed.Ids,
            packed.Zones,
            packed.Indices);
    }

    [PunRPC]
    private void ReceiveSelectedTargetEmphasis(string[] kinds, int[] ids, string[] zones, int[] indices)
    {
        _ = PlayReceivedTargetEmphasis(kinds, ids, zones, indices);
    }

    async Task PlayReceivedTargetEmphasis(string[] kinds, int[] ids, string[] zones, int[] indices)
    {
        try
        {
            var cards = CardAddress.ResolveAll(kinds, ids, zones, indices);
            if (cards.Count > 0 && CardAnimationManager.Instance != null)
                await CardAnimationManager.Instance.EmphasizeSelectedCards(cards);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveSelectedTargetEmphasisでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    private void RegisterOpponentAnimationWait()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (opponentAnimationAckLock)
            opponentAnimationAcks.Enqueue(tcs);
    }

    public void SendOpponentAnimationComplete()
    {
        SendRPC("ReceiveOpponentAnimationComplete");
    }

    [PunRPC]
    private void ReceiveOpponentAnimationComplete()
    {
        TaskCompletionSource<bool> tcs = null;
        lock (opponentAnimationAckLock)
        {
            if (opponentAnimationAcks.Count > 0)
                tcs = opponentAnimationAcks.Dequeue();
        }

        tcs?.TrySetResult(true);
    }

    public async Task WaitForOpponentAnimation()
    {
        if (!IsOnlineMode) return;

        // 完了通知は登録順（先頭）から消化する。最新だけを待つと、残件があるときに食い違う。
        TaskCompletionSource<bool> tcs;
        lock (opponentAnimationAckLock)
        {
            tcs = opponentAnimationAcks.Count > 0
                ? opponentAnimationAcks.Peek()
                : null;
        }

        if (tcs == null || tcs.Task.IsCompleted)
            return;

        await tcs.Task;
    }

    public async Task WaitForAllOpponentAnimations()
    {
        if (!IsOnlineMode) return;

        List<TaskCompletionSource<bool>> generalWaits;
        List<TaskCompletionSource<bool>> handWaits;
        lock (opponentAnimationAckLock)
        {
            generalWaits = opponentAnimationAcks.ToList();
        }

        lock (addToHandAckLock)
        {
            handWaits = addToHandMoveAcks.Values
                .Concat(addToHandEmphasisAcks.Values)
                .ToList();
        }

        var pending = generalWaits.Concat(handWaits).Select(ack => (Task)ack.Task).ToList();
        if (pending.Count == 0) return;

        Task all = Task.WhenAll(pending);
        Task finished = await Task.WhenAny(all, Task.Delay(AddToHandAckTimeoutMs));
        if (finished != all)
        {
            Debug.LogWarning($"[OnlineSyncManager] 相手アニメ完了待ちがタイムアウトしました: general={generalWaits.Count}, hand={handWaits.Count}");
            foreach (var tcs in generalWaits)
                tcs.TrySetResult(true);
            foreach (var tcs in handWaits)
                tcs.TrySetResult(true);
            return;
        }

        await all;
    }

    /// <summary>
    /// 相手側の意思決定UI（Yes/No等）表示中に操作をブロックさせる開始同期
    /// </summary>
    public void SyncBeginOpponentDecisionBlock()
    {
        SendRPC("ReceiveBeginOpponentDecisionBlock");
    }

    /// <summary>
    /// 相手側の意思決定UI（Yes/No等）表示終了時にブロック解除する同期
    /// </summary>
    public void SyncEndOpponentDecisionBlock()
    {
        SendRPC("ReceiveEndOpponentDecisionBlock");
    }

    /// <summary>
    /// 選択が終わるまで操作を止め、必要なら相手側にも同じブロックを出す。
    /// </summary>
    public async Task<T> WhileOpponentDecides<T>(System.Func<Task<T>> action, bool notifyOpponent)
    {
        BeginOperationBlock();
        if (notifyOpponent)
            SyncBeginOpponentDecisionBlock();
        try
        {
            return await action();
        }
        finally
        {
            EndOperationBlock();
            if (notifyOpponent)
                SyncEndOpponentDecisionBlock();
        }
    }

    /// <summary>
    /// 攻撃キャンセル登録を相手側へ通知
    /// </summary>
    public void SyncRegisterOpponentMoveCancellation(CardController attacker)
    {
        TrySendCardRpc("ReceiveRegisterOpponentMoveCancellation", attacker);
    }

    /// <summary>
    /// 技ダメージの無効化を、攻撃者へ残すよう相手側へ通知する。
    /// </summary>
    public void SyncRegisterMoveDamageNullification(CardController attacker, int nullificationType, int subtract)
    {
        TrySendCardRpc("ReceiveRegisterMoveDamageNullification", attacker, nullificationType, subtract);
    }

    /// <summary>
    /// 相手プレイヤーにYes/No選択を要求し、回答が届くまで待つ
    /// </summary>
    public Task<bool> RequestOpponentYesNoDecision(CardController sourceCard, string message)
    {
        return RequestFromOpponent(
            opponentYesNo,
            sourceCard,
            id => SendRPC(
                "ReceiveOpponentDecisionRequest",
                id,
                sourceCard.cardKind.ToString(),
                sourceCard.cardID,
                sourceCard.cardName ?? string.Empty,
                message ?? string.Empty),
            unavailable: false,
            label: "相手選択");
    }

    public Task SendOpponentDecisionResponse(int requestId, bool accepted)
    {
        SendRPC("ReceiveOpponentDecisionResponse", requestId, accepted);
        return Task.CompletedTask;
    }

    /// <summary>
    /// タクティクススキル処理完了のRPC受信
    /// </summary>
    [PunRPC]
    private void ReceiveTacticsSkillCompleted(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        if (!IsOnlineMode)
            return;

        WithResolvedCard(
            cardKindStr, cardID, zoneName, zoneIndex,
            "ReceiveTacticsSkillCompletedでエラー",
            card => _ = ExecuteTacticsToGraveyard(card));
    }

    /// <summary>
    /// 相手からの意思決定ブロック開始通知を受信
    /// </summary>
    [PunRPC]
    private void ReceiveBeginOpponentDecisionBlock()
    {
        BeginOperationBlock();
    }

    /// <summary>
    /// 相手からの意思決定ブロック終了通知を受信
    /// </summary>
    [PunRPC]
    private void ReceiveEndOpponentDecisionBlock()
    {
        EndOperationBlock();
    }

    [PunRPC]
    private void ReceiveRegisterOpponentMoveCancellation(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        if (!IsOnlineMode) return;

        WithResolvedCard(
            cardKindStr, cardID, zoneName, zoneIndex,
            "ReceiveRegisterOpponentMoveCancellationでエラー",
            attacker => BattleManager.Instance.RegisterOpponentMoveCancellation(attacker, false));
    }

    [PunRPC]
    private void ReceiveRegisterMoveDamageNullification(string cardKindStr, int cardID, string zoneName, int zoneIndex, int nullificationType, int subtract)
    {
        if (!IsOnlineMode) return;

        WithResolvedCard(
            cardKindStr, cardID, zoneName, zoneIndex,
            "ReceiveRegisterMoveDamageNullificationでエラー",
            attacker =>
            {
                var type = (DamageNullificationType)nullificationType;
                BattleManager.Instance.RegisterMoveDamageNullification(attacker, type, subtract, false);
            });
    }

    [PunRPC]
    private void ReceiveOpponentDecisionRequest(int requestId, string cardKindStr, int cardId, string sourceCardName, string message)
    {
        if (!IsOnlineMode) return;

        try
        {
            BeginOperationBlock();

            string displayMessage = BuildOpponentDecisionMessage(message, sourceCardName, cardKindStr, cardId);

            if (YesOrNoSelectionUI.Instance == null)
            {
                Debug.LogError("[OnlineSyncManager] Yes/No選択UIがないため、相手の選択を表示できません。");
                return;
            }

            YesOrNoSelectionUI.Instance.ShowSelectionUI(
                displayMessage,
                async () =>
                {
                    await SendOpponentDecisionResponse(requestId, true);
                },
                async () =>
                {
                    await SendOpponentDecisionResponse(requestId, false);
                }
            );
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveOpponentDecisionRequestでエラー: {ex.Message}");
        }
    }

    [PunRPC]
    private void ReceiveOpponentDecisionResponse(int requestId, bool accepted)
    {
        if (!IsOnlineMode) return;
        opponentYesNo.TryComplete(requestId, accepted);
    }

    /// <summary>
    /// 相手プレイヤーに文章ボタンの選択を要求し、選ばれた番号が届くまで待つ。
    /// </summary>
    public Task<int> RequestOpponentSelectDecision(CardController sourceCard, string title, string[] options, bool[] selectable)
    {
        return RequestFromOpponent(
            opponentSelect,
            sourceCard,
            id => SendRPC(
                "ReceiveOpponentSelectRequest",
                id,
                sourceCard.cardKind.ToString(),
                sourceCard.cardID,
                sourceCard.cardName ?? string.Empty,
                title ?? string.Empty,
                options ?? new string[0],
                selectable ?? new bool[0]),
            unavailable: -1,
            label: "相手選択(セレクト)");
    }

    public Task SendOpponentSelectResponse(int requestId, int selectedIndex)
    {
        SendRPC("ReceiveOpponentSelectResponse", requestId, selectedIndex);
        return Task.CompletedTask;
    }

    [PunRPC]
    private void ReceiveOpponentSelectRequest(int requestId, string cardKindStr, int cardId, string sourceCardName, string title, string[] options, bool[] selectable)
    {
        if (!IsOnlineMode) return;

        try
        {
            BeginOperationBlock();

            var labels = options ?? new string[0];
            bool anySelectable = false;
            for (int i = 0; i < labels.Length; i++)
            {
                if (selectable == null || i >= selectable.Length || selectable[i])
                {
                    anySelectable = true;
                    break;
                }
            }

            var ui = OpponentSelectUI.Ensure();
            if (ui == null || labels.Length == 0 || !anySelectable)
            {
                Debug.LogError($"[OnlineSyncManager] 相手選択(セレクト)を表示できないため、選択を行いません。 source={sourceCardName} {cardKindStr}:{cardId}");
                _ = SendOpponentSelectResponse(requestId, -1);
                EndOperationBlock();
                SyncEndOpponentDecisionBlock();
                return;
            }

            string displayTitle = string.IsNullOrWhiteSpace(title)
                ? "どちらを選択しますか？"
                : title;

            ui.Show(
                displayTitle,
                labels,
                selectable,
                async index =>
                {
                    await SendOpponentSelectResponse(requestId, index);
                });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveOpponentSelectRequestでエラー: {ex.Message}");
            _ = SendOpponentSelectResponse(requestId, 0);
            EndOperationBlock();
            SyncEndOpponentDecisionBlock();
        }
    }

    [PunRPC]
    private void ReceiveOpponentSelectResponse(int requestId, int selectedIndex)
    {
        if (!IsOnlineMode) return;
        opponentSelect.TryComplete(requestId, selectedIndex);
    }

    /// <summary>
    /// 相手に候補カードから枚数だけ選ばせ、選ばれた番号が届くまで待つ。
    /// </summary>
    public Task<int[]> RequestOpponentCardPick(CardController sourceCard, int[] kinds, int[] ids, int pickCount, bool upTo = false)
    {
        return RequestFromOpponent(
            opponentCardPick,
            sourceCard,
            id => SendRPC(
                "ReceiveOpponentCardPickRequest",
                id,
                pickCount,
                kinds ?? new int[0],
                ids ?? new int[0],
                sourceCard.cardName ?? string.Empty,
                upTo),
            unavailable: new int[0],
            label: "相手のカード選択");
    }

    [PunRPC]
    private void ReceiveOpponentCardPickRequest(int requestId, int pickCount, int[] kinds, int[] ids, string sourceCardName, bool upTo)
    {
        if (!IsOnlineMode) return;
        _ = ResolveOpponentCardPick(requestId, pickCount, kinds, ids, sourceCardName, upTo);
    }

    async Task ResolveOpponentCardPick(int requestId, int pickCount, int[] kinds, int[] ids, string sourceCardName, bool upTo)
    {
        BeginOperationBlock();
        int[] indices = new int[0];
        try
        {
            indices = await BuildOpponentCardPick(pickCount, kinds, ids, upTo);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveOpponentCardPickRequestでエラー: {ex.Message} source={sourceCardName}");
            indices = RandomIndices(Mathf.Min(kinds != null ? kinds.Length : 0, ids != null ? ids.Length : 0), pickCount);
        }
        finally
        {
            SendRPC("ReceiveOpponentCardPickResponse", requestId, indices ?? new int[0]);
            EndOperationBlock();
            SyncEndOpponentDecisionBlock();
        }
    }

    async Task<int[]> BuildOpponentCardPick(int pickCount, int[] kinds, int[] ids, bool upTo)
    {
        int length = Mathf.Min(kinds != null ? kinds.Length : 0, ids != null ? ids.Length : 0);
        int take = Mathf.Clamp(pickCount, 0, length);
        if (take == 0)
            return new int[0];

        if (CardSelectionUI.Instance == null)
        {
            return RandomIndices(length, take);
        }

        var items = new List<SelectionItem>(length);
        for (int i = 0; i < length; i++)
        {
            items.Add(new SelectionItem
            {
                source = SelectionItem.SourceType.Deck,
                deckData = new DeckCard((CardKind)kinds[i], ids[i])
            });
        }

        if (length <= take && !(upTo && length > 1))
        {
            var all = new int[length];
            for (int i = 0; i < length; i++)
                all[i] = i;
            return all;
        }

        var chosen = await CardSelectionUI.Instance.ShowIndexedAsync(items, take, upTo);
        if (chosen == null || chosen.Count == 0)
            return RandomIndices(length, take);

        return chosen.ToArray();
    }

    static int[] RandomIndices(int length, int count)
    {
        int take = Mathf.Clamp(count, 0, length);
        var order = new int[length];
        for (int i = 0; i < length; i++)
            order[i] = i;

        for (int i = length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int swap = order[i];
            order[i] = order[j];
            order[j] = swap;
        }

        var result = new int[take];
        for (int i = 0; i < take; i++)
            result[i] = order[i];
        return result;
    }

    [PunRPC]
    private void ReceiveOpponentCardPickResponse(int requestId, int[] indices)
    {
        if (!IsOnlineMode) return;
        opponentCardPick.TryComplete(requestId, indices ?? new int[0]);
    }

    private string BuildOpponentDecisionMessage(string message, string sourceCardName, string cardKindStr, int cardId)
    {
        if (!string.IsNullOrWhiteSpace(message))
            return message;

        string resolvedName = !string.IsNullOrWhiteSpace(sourceCardName)
            ? sourceCardName
            : ResolveOpponentCardName(cardKindStr, cardId);

        if (!string.IsNullOrWhiteSpace(resolvedName))
            return $"{resolvedName} の効果が発動しました。技を無効にしますか？";

        return "相手の効果で技を無効にできます。無効にしますか？";
    }

    private string ResolveOpponentCardName(string cardKindStr, int cardId)
    {
        if (string.IsNullOrWhiteSpace(cardKindStr) || BattleManager.Instance == null)
            return string.Empty;

        if (!System.Enum.TryParse(cardKindStr, out CardKind cardKind))
            return string.Empty;

        var bm = BattleManager.Instance;
        var searchZones = new[]
        {
            bm.eTacticsZone,
            bm.eFieldZone,
            bm.eBattlerZone,
            bm.eHandZone,
            bm.eGraveZone
        };

        foreach (var zone in searchZones)
        {
            if (zone == null) continue;
            var cards = zone.GetComponentsInChildren<CardController>();
            var match = cards.FirstOrDefault(c => c.cardKind == cardKind && c.cardID == cardId);
            if (match != null)
            {
                return match.cardName;
            }
        }

        return string.Empty;
    }
}
