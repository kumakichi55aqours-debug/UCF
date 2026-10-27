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
        if (!CardAddress.TryFrom(card, out CardAddress address))
            return;

        SendRPC("ReceiveTacticsSkillCompleted", address.Kind.ToString(), address.Id, address.ZoneName, address.Index);
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
    /// 相手に、自分のフェイズ開始時・終了時のチェーン窓へ空の候補で参加するよう頼む
    /// </summary>
    public void SyncJoinPhaseChain(SkillTrigger trigger, TurnPhase phase)
    {
        SendRPC("ReceiveJoinPhaseChain", (int)trigger, (int)phase);
    }

    /// <summary>
    /// 受信: 相手のフェイズタイミングのチェーンに、自分のスキルは足さず参加する
    /// </summary>
    [PunRPC]
    private void ReceiveJoinPhaseChain(int triggerInt, int phaseInt)
    {
        if (!IsOnlineMode) return;
        SkillChainManager.Instance?.JoinPhaseWindow((SkillTrigger)triggerInt, (TurnPhase)phaseInt);
    }

    /// <summary>
    /// 相手にスキル発動アニメーションの実行を要求（ACK同期）
    /// </summary>
    public void SyncRequestOpponentSkillActivation(CardController card, string triggerStr)
    {
        if (!IsOnlineMode)
            return;

        skillActivationAck.Reset();
        if (!CardAddress.TryFrom(card, out CardAddress address))
        {
            Debug.LogWarning("[OnlineSyncManager] スキル発動アニメーションの送信先が決まらないため、完了通知を待ちます。");
            return;
        }

        SendRPC(
            "ReceiveRequestOpponentSkillActivation",
            address.Kind.ToString(),
            address.Id,
            address.ZoneName,
            address.Index,
            triggerStr);
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

        var kinds = new List<string>();
        var ids = new List<int>();
        var zones = new List<string>();
        var indices = new List<int>();
        foreach (var card in cards)
            CardAddress.TryAdd(card, kinds, ids, zones, indices);

        if (kinds.Count == 0)
            return;

        SendAnimatedRpc(
            "ReceiveSelectedTargetEmphasis",
            kinds.ToArray(),
            ids.ToArray(),
            zones.ToArray(),
            indices.ToArray());
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
            var cards = new List<CardController>();
            int count = CardAddress.Count(kinds, ids, zones, indices);
            for (int i = 0; i < count; i++)
            {
                CardController card = CardAddress.ResolveAt(kinds, ids, zones, indices, i);
                if (card != null && !cards.Contains(card))
                    cards.Add(card);
            }

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
        {
            opponentAnimationAcks.Enqueue(tcs);
            lastOpponentAnimationAck = tcs;
        }
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
            {
                tcs = opponentAnimationAcks.Dequeue();
            }
        }

        tcs?.TrySetResult(true);
    }

    public async Task WaitForOpponentAnimation()
    {
        if (!IsOnlineMode) return;

        TaskCompletionSource<bool> tcs;
        lock (opponentAnimationAckLock)
        {
            tcs = lastOpponentAnimationAck;
        }

        if (tcs == null || tcs.Task.IsCompleted)
        {
            return;
        }

        await tcs.Task;
    }

    public async Task WaitForAllOpponentAnimations()
    {
        if (!IsOnlineMode) return;

        List<Task> pending;
        lock (opponentAnimationAckLock)
        {
            pending = opponentAnimationAcks.Select(ack => (Task)ack.Task).ToList();
        }

        if (pending.Count == 0) return;

        await Task.WhenAll(pending);
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
        if (!CardAddress.TryFrom(attacker, out CardAddress address))
            return;

        SendRPC(
            "ReceiveRegisterOpponentMoveCancellation",
            address.Kind.ToString(),
            address.Id,
            address.ZoneName,
            address.Index);
    }

    /// <summary>
    /// 相手プレイヤーにYes/No選択を要求し、回答が届くまで待つ
    /// </summary>
    public async Task<bool> RequestOpponentYesNoDecision(CardController sourceCard, string message)
    {
        if (!IsOnlineMode || sourceCard == null)
        {
            Debug.LogWarning("[OnlineSyncManager] 相手選択を開始できないため、回答を待ち続けます。");
            return await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        while (opponentDecisionTcs != null)
        {
            var pending = opponentDecisionTcs;
            if (!pending.Task.IsCompleted)
                await pending.Task;
            else
                await Task.Yield();
        }

        opponentDecisionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentOpponentDecisionRequestId = ++opponentDecisionRequestIdCounter;

        string safeMessage = message ?? string.Empty;
        string cardKindStr = sourceCard.cardKind.ToString();
        int cardId = sourceCard.cardID;
        string sourceCardName = sourceCard.cardName ?? string.Empty;

        SendRPC("ReceiveOpponentDecisionRequest", currentOpponentDecisionRequestId, cardKindStr, cardId, sourceCardName, safeMessage);

        bool result = await opponentDecisionTcs.Task;

        opponentDecisionTcs = null;
        currentOpponentDecisionRequestId = -1;

        return result;
    }

    public Task SendOpponentDecisionResponse(int requestId, bool accepted)
    {
        SendRPC("ReceiveOpponentDecisionResponse", requestId, accepted);
        pendingOpponentDecisionRequestId = -1;
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

        try
        {
            CardController targetCard = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
            if (targetCard != null)
                _ = ExecuteTacticsToGraveyard(targetCard);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveTacticsSkillCompletedでエラー: {ex.Message}");
        }
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

        try
        {
            CardController attacker = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
            if (attacker != null)
                BattleManager.Instance.RegisterOpponentMoveCancellation(attacker, false);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveRegisterOpponentMoveCancellationでエラー: {ex.Message}");
        }
    }

    [PunRPC]
    private void ReceiveOpponentDecisionRequest(int requestId, string cardKindStr, int cardId, string sourceCardName, string message)
    {
        if (!IsOnlineMode) return;

        try
        {
            pendingOpponentDecisionRequestId = requestId;
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

        if (opponentDecisionTcs != null && requestId == currentOpponentDecisionRequestId)
        {
            opponentDecisionTcs.TrySetResult(accepted);
        }
    }

    /// <summary>
    /// 相手プレイヤーに文章ボタンの選択を要求し、選ばれた番号が届くまで待つ。
    /// </summary>
    public async Task<int> RequestOpponentSelectDecision(CardController sourceCard, string title, string[] options, bool[] selectable)
    {
        if (!IsOnlineMode || sourceCard == null)
        {
            Debug.LogWarning("[OnlineSyncManager] 相手選択(セレクト)を開始できないため、回答を待ち続けます。");
            return await new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        while (opponentSelectTcs != null)
        {
            var pending = opponentSelectTcs;
            if (!pending.Task.IsCompleted)
                await pending.Task;
            else
                await Task.Yield();
        }

        opponentSelectTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentOpponentSelectRequestId = ++opponentSelectRequestIdCounter;

        SendRPC(
            "ReceiveOpponentSelectRequest",
            currentOpponentSelectRequestId,
            sourceCard.cardKind.ToString(),
            sourceCard.cardID,
            sourceCard.cardName ?? string.Empty,
            title ?? string.Empty,
            options ?? new string[0],
            selectable ?? new bool[0]);

        int result = await opponentSelectTcs.Task;
        opponentSelectTcs = null;
        currentOpponentSelectRequestId = -1;
        return result;
    }

    public Task SendOpponentSelectResponse(int requestId, int selectedIndex)
    {
        SendRPC("ReceiveOpponentSelectResponse", requestId, selectedIndex);
        pendingOpponentSelectRequestId = -1;
        return Task.CompletedTask;
    }

    [PunRPC]
    private void ReceiveOpponentSelectRequest(int requestId, string cardKindStr, int cardId, string sourceCardName, string title, string[] options, bool[] selectable)
    {
        if (!IsOnlineMode) return;

        try
        {
            pendingOpponentSelectRequestId = requestId;
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

        if (opponentSelectTcs != null && requestId == currentOpponentSelectRequestId)
        {
            opponentSelectTcs.TrySetResult(selectedIndex);
        }
    }

    /// <summary>
    /// 相手に候補カードから枚数だけ選ばせ、選ばれた番号が届くまで待つ。
    /// </summary>
    public async Task<int[]> RequestOpponentCardPick(CardController sourceCard, int[] kinds, int[] ids, int pickCount)
    {
        if (!IsOnlineMode || sourceCard == null)
        {
            Debug.LogWarning("[OnlineSyncManager] 相手のカード選択を開始できないため、選択結果を待ち続けます。");
            return await new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        while (opponentCardPickTcs != null)
        {
            var pending = opponentCardPickTcs;
            if (!pending.Task.IsCompleted)
                await pending.Task;
            else
                await Task.Yield();
        }

        opponentCardPickTcs = new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentOpponentCardPickRequestId = ++opponentCardPickRequestIdCounter;

        SendRPC(
            "ReceiveOpponentCardPickRequest",
            currentOpponentCardPickRequestId,
            pickCount,
            kinds ?? new int[0],
            ids ?? new int[0],
            sourceCard.cardName ?? string.Empty);

        int[] result = await opponentCardPickTcs.Task;
        opponentCardPickTcs = null;
        currentOpponentCardPickRequestId = -1;
        return result;
    }

    [PunRPC]
    private void ReceiveOpponentCardPickRequest(int requestId, int pickCount, int[] kinds, int[] ids, string sourceCardName)
    {
        if (!IsOnlineMode) return;
        _ = ResolveOpponentCardPick(requestId, pickCount, kinds, ids, sourceCardName);
    }

    async Task ResolveOpponentCardPick(int requestId, int pickCount, int[] kinds, int[] ids, string sourceCardName)
    {
        BeginOperationBlock();
        int[] indices = new int[0];
        try
        {
            indices = await BuildOpponentCardPick(pickCount, kinds, ids, sourceCardName);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveOpponentCardPickRequestでエラー: {ex.Message}");
            indices = RandomIndices(Mathf.Min(kinds != null ? kinds.Length : 0, ids != null ? ids.Length : 0), pickCount);
        }
        finally
        {
            SendRPC("ReceiveOpponentCardPickResponse", requestId, indices ?? new int[0]);
            EndOperationBlock();
            SyncEndOpponentDecisionBlock();
        }
    }

    async Task<int[]> BuildOpponentCardPick(int pickCount, int[] kinds, int[] ids, string sourceCardName)
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

        if (!string.IsNullOrEmpty(sourceCardName))

        if (length <= take)
        {
            var all = new int[length];
            for (int i = 0; i < length; i++)
                all[i] = i;
            return all;
        }

        var chosen = await CardSelectionUI.Instance.ShowIndexedAsync(items, take);
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

        if (opponentCardPickTcs != null && requestId == currentOpponentCardPickRequestId)
        {
            opponentCardPickTcs.TrySetResult(indices ?? new int[0]);
        }
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

    #region 技を受ける時スキルの同期
    public Task<int> WaitForDamageReceivedSkillComplete(int defaultDamage)
    {
        if (!IsOnlineMode) return Task.FromResult(defaultDamage);
        if (SkillChainSync.Instance == null) return Task.FromResult(defaultDamage);
        return SkillChainSync.Instance.WaitForDamageReceivedSkillComplete();
    }
    #endregion

    #region 相手の技使用時スキルの同期
    public Task WaitForOpponentMoveUsedSkillComplete()
    {
        if (!IsOnlineMode) return Task.CompletedTask;
        if (SkillChainSync.Instance == null) return Task.CompletedTask;
        return SkillChainSync.Instance.WaitForOpponentMoveUsedSkillComplete();
    }
    #endregion
}
