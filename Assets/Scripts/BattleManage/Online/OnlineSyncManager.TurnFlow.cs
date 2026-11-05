using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    bool phaseAdvanceInProgress;
    bool turnAdvanceInProgress;
    bool clientTurnTransitionRunning;
    // 相手がエンドフェイズの盤面処理を終えた通知。時間では待たない。
    SyncState peerEndPhaseApplied = new SyncState();

    // カード操作が可能かどうかを判定
    public bool CanPlayerOperateCards()
    {
        if (!IsOnlineMode) return true;
        if (PresentationBlocking) return false;

        return IsMyTurn;
    }

    // NextPhaseButtonの処理を管理
    public async Task HandleNextPhaseButton()
    {
        if (phaseAdvanceInProgress || !IsMyTurn)
            return;

        phaseAdvanceInProgress = true;
        try
        {
            TurnPhase currentPhase = PhaseManager.Instance.CurrentPhase;
            await PhaseManager.Instance.NextPhase();
            await NotifyPhaseResolved(PhaseManager.Instance.CurrentPhase);

            // クライアントのターン終了は、相手側のエンドフェイズも終わってからホストへ知らせる。
            // ホストのターン終了は、PhaseManager がボタン待ちを終えてから進める。
            if (!IsHostPlayer
                && currentPhase == TurnPhase.バトルフェイズ
                && PhaseManager.Instance.CurrentPhase == TurnPhase.エンドフェイズ)
            {
                HandleNextTurn();
            }
        }
        finally
        {
            phaseAdvanceInProgress = false;
        }
    }

    // クライアント側からのフェイズ進行リクエストを受信
    [PunRPC]
    private void RequestNextPhase()
    {
        if (!IsHostPlayer || !IsMyTurn || phaseAdvanceInProgress)
            return;

        _ = HandleHostNextPhase();
    }

    // ホスト側のフェイズ進行処理
    private async Task HandleHostNextPhase()
    {
        if (phaseAdvanceInProgress || !IsMyTurn)
            return;

        phaseAdvanceInProgress = true;
        try
        {
            await PhaseManager.Instance.NextPhase();
            await NotifyPhaseResolved(PhaseManager.Instance.CurrentPhase);
        }
        finally
        {
            phaseAdvanceInProgress = false;
        }
    }

    // クライアント側にフェイズを通知（クライアント側から）
    public void NotifyPhaseToSClient(TurnPhase phase)
    {
        SendRPC("ReceivePhaseFromHost", (int)phase);
    }

    // クライアント側からのフェイズ通知を受信（ホスト側）
    [PunRPC]
    private void ReceivePhaseFromHost(int phaseInt)
    {
        if (IsHostPlayer) return;

        _ = ApplyReceivedPhase((TurnPhase)phaseInt);
    }

    /// <summary>
    /// クライアント側にフェイズを通知（ホスト側から）
    /// </summary>
    public void NotifyPhaseToHost(TurnPhase phase)
    {
        SendRPC("ReceivePhaseFromClient", (int)phase);
    }

    /// <summary>
    /// クライアント側からのフェイズ通知を受信（クライアント側）
    /// </summary>
    [PunRPC]
    private void ReceivePhaseFromClient(int phaseInt)
    {
        if (!IsHostPlayer) return;

        _ = ApplyReceivedPhase((TurnPhase)phaseInt);
    }

    /// <summary>
    /// フェイズを相手へ知らせる。エンドフェイズは、相手の盤面処理が終わるまで返す。
    /// </summary>
    async Task NotifyPhaseResolved(TurnPhase phase)
    {
        bool endPhase = phase == TurnPhase.エンドフェイズ;
        if (endPhase)
            peerEndPhaseApplied.Reset();

        if (IsHostPlayer)
            NotifyPhaseToSClient(phase);
        else
            NotifyPhaseToHost(phase);

        if (endPhase)
            await peerEndPhaseApplied.WaitForCompletion();
    }

    /// <summary>
    /// 相手から知らされたフェイズを、この盤面でも最後まで実行する。
    /// エンドフェイズが終わったら、送り主のターン進行を解放する。
    /// </summary>
    async Task ApplyReceivedPhase(TurnPhase phase)
    {
        try
        {
            await PhaseManager.Instance.ExecutePhase(phase);
            PhaseManager.Instance.UpdatePhaseUI();
            if (IsHostPlayer)
            {
                PhaseGlowUI glow = FindObjectOfType<PhaseGlowUI>();
                if (glow != null)
                    glow.HighlightPhase(phase);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"受信したフェイズの実行でエラー: {ex.Message}");
        }
        finally
        {
            if (phase == TurnPhase.エンドフェイズ)
                SendRPC("AcknowledgeEndPhaseApplied");
        }
    }

    [PunRPC]
    private void AcknowledgeEndPhaseApplied()
    {
        peerEndPhaseApplied.SetComplete();
    }

    /// <summary>
    /// 次のターンの処理を管理。
    /// ホストはターンを進めて、次の手番側の開始処理を1つだけ行う。
    /// クライアントはホストへターン完了を送る。
    /// </summary>
    public void HandleNextTurn()
    {
        if (!IsHostPlayer)
        {
            SendRPC("NotifyTurnComplete");
            return;
        }

        _ = AdvanceToNextTurn();
    }

    /// <summary>
    /// 開幕、またはターンを進めたあとに、今の手番側を開始する。
    /// ホストの手番は PhaseManager の開始処理へ寄せ、ドローとメインの実行をここへ書かない。
    /// ホスト手番は開幕も含めて、相手へドロー表示の開始を知らせる。
    /// </summary>
    public async Task BeginActiveSideTurn()
    {
        if (BattleManager.Instance != null && BattleManager.Instance.IsActiveSideTurn)
        {
            if (PhaseManager.Instance == null)
                return;
            SendRPC("StartHostTurn");
            await PhaseManager.Instance.RunOnlineHostTurn();
            return;
        }

        await BeginOpeningClientTurn();
    }

    async Task AdvanceToNextTurn()
    {
        if (turnAdvanceInProgress || BattleManager.Instance == null)
            return;

        // ホスト手番はここでボタン待ちまで含む。フラグを持ったままだと、
        // その終了から次のターンへ進む呼び出しが重なって捨てられる。
        turnAdvanceInProgress = true;
        try
        {
            BattleManager.Instance.IncrementTurn();
            SyncTurnNumber(BattleManager.Instance.curTurn);
        }
        finally
        {
            turnAdvanceInProgress = false;
        }

        await BeginActiveSideTurn();
    }

    /// <summary>
    /// クライアントのターンを開始する。ターン数は増やさない。
    /// 開幕でクライアントが先攻のときと、ホストのターン終了後に使う。
    /// </summary>
    public async Task BeginOpeningClientTurn()
    {
        clientTurnTransitionComplete.Reset();
        clientDrawsCompleted.Reset();
        isTurnTransitionPhase = true;
        SendRPC("StartTurnTransitionPhase");
        await WaitForClientTurnTransitionComplete();
        SendRPC("NotifyHostTurnTransitionComplete");
        isTurnTransitionPhase = false;
        await WaitForClientDrawComplete();
    }

    /// <summary>
    /// クライアント側からのターン完了通知を受信
    /// </summary>
    [PunRPC]
    private void NotifyTurnComplete()
    {
        if (!IsHostPlayer)
            return;

        _ = AdvanceToNextTurn();
    }

    /// <summary>
    /// ホスト側のターン開始通知を受信
    /// </summary>
    [PunRPC]
    private void StartHostTurn()
    {
        if (IsHostPlayer) return;

        // クライアント側でもホストのドローフェイズを実行（相手のドローアニメーション表示のため）
        _ = ExecuteHostDrawPhaseOnClient();
    }

    /// <summary>
    /// クライアント側でホストのドローフェイズを実行
    /// </summary>
    private async Task ExecuteHostDrawPhaseOnClient()
    {

        try
        {
            // クライアントドローと同様にEnemyDrawCardAsyncを使用（デッキUI更新が含まれる）
            await PhaseManager.Instance.ApplyPhaseAsync(TurnPhase.ドローフェイズ);
            // 初回ターンは初期手札のあとなので、相手の追加ドローは無い
            if (BattleManager.Instance.curTurn > 1)
                await BattleManager.Instance.EnemyDrawCardAsync();
            BattleManager.Instance.ResetCurrentTurnUsedMoves();

        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteHostDrawPhaseOnClientでエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// ターン切り替えの間を開始するためのRPC
    /// </summary>
    [PunRPC]
    private void StartTurnTransitionPhase()
    {
        if (IsHostPlayer || clientTurnTransitionRunning)
            return;

        _ = ExecuteTurnTransitionPhase();
    }

    /// <summary>
    /// クライアント側のターン切り替えの間の実行
    /// </summary>
    private async Task ExecuteTurnTransitionPhase()
    {
        clientTurnTransitionRunning = true;
        hostTurnTransitionComplete.Reset();
        hostDrawPhaseTransitionComplete.Reset();
        hostDrawCompleteAcknowledged.Reset();
        try
        {
            // ホストがターン切り替えを受け取るまで、操作できない間にしておく。
            isTurnTransitionPhase = true;
            SendRPC("NotifyClientTurnTransitionComplete");
            await WaitForHostTurnTransitionComplete();
            isTurnTransitionPhase = false;

            // ホスト側にドローフェイズ移行を通知
            SendRPC("NotifyClientDrawPhaseTransition");

            // ホスト側のドローフェイズ移行完了を待つ
            await WaitForHostDrawPhaseTransitionComplete();

            // クライアント側でドローフェイズに移行（フェイズ表示更新 + ドロー実行）
            await PhaseManager.Instance.ExecutePhase(TurnPhase.ドローフェイズ);

            // ドロー完了と手札情報をホストに送信
            SendDetailedHandInfoToOpponent();
            SendRPC("NotifyClientDrawComplete");

            // ホスト側のドロー完了確認を待つ
            await WaitForHostDrawCompleteAcknowledgment();

            // メインフェイズに移行
            await PhaseManager.Instance.ExecutePhase(TurnPhase.メインフェイズ);

            // ホスト側にメインフェイズ移行を通知
            NotifyPhaseToHost(TurnPhase.メインフェイズ);
        }
        finally
        {
            clientTurnTransitionRunning = false;
        }
    }
    /// <summary>
    /// クライアント側のドローフェイズ移行通知を受信
    /// </summary>
    [PunRPC]
    private void NotifyClientDrawPhaseTransition()
    {
        if (!IsHostPlayer) return;

        // ホスト側もドローフェイズに移行
        _ = ExecuteHostDrawPhaseTransition();
    }

    /// <summary>
    /// ホスト側のドローフェイズ移行処理
    /// </summary>
    private async Task ExecuteHostDrawPhaseTransition()
    {

        // ホスト側でドローフェイズ表示を更新（ドローは実行しない）
        await PhaseManager.Instance.ApplyPhaseAsync(TurnPhase.ドローフェイズ);

        // クライアント側にドローフェイズ移行完了を通知
        SendRPC("AcknowledgeHostDrawPhaseTransition");

        // 初回ターンは初期手札のあとなので、追加ドローはしない
        if (BattleManager.Instance.curTurn > 1)
            await BattleManager.Instance.EnemyDrawCardAsync();
        BattleManager.Instance.ResetCurrentTurnUsedMoves();
    }

    /// <summary>
    /// ホスト側のドローフェイズ移行完了確認を受信
    /// </summary>
    [PunRPC]
    private void AcknowledgeHostDrawPhaseTransition()
    {
        if (IsHostPlayer) return;

        hostDrawPhaseTransitionComplete.SetComplete();
    }

    /// <summary>
    /// クライアント側のドロー完了通知を受信
    /// </summary>
    [PunRPC]
    private void NotifyClientDrawComplete()
    {
        if (!IsHostPlayer) return;

        // 既に完了している場合はスキップ（二重実行防止）
        if (clientDrawsCompleted.IsComplete)
        {
            return;
        }

        clientDrawsCompleted.SetComplete();
    }

    /// <summary>
    /// クライアント側のドロー完了を待機
    /// </summary>
    private async Task WaitForClientDrawComplete()
    {
        if (!IsOnlineMode) return;

        // このターンの開始時に戻してある。ここで消すと、先に届いた完了通知を落とす。
        await clientDrawsCompleted.WaitForCompletion();

        // クライアント側にドロー完了確認を送信
        SendRPC("AcknowledgeClientDrawComplete");
    }

    /// <summary>
    /// ホスト側のドローフェイズ移行完了確認を待機
    /// </summary>
    private async Task WaitForHostDrawPhaseTransitionComplete()
    {
        if (!IsOnlineMode) return;

        // 既に確認済みの場合は即座に返す
        if (hostDrawPhaseTransitionComplete.IsComplete)
        {
            return;
        }

        // 完了を待機
        await hostDrawPhaseTransitionComplete.WaitForCompletion();

    }

    /// <summary>
    /// ホスト側のドロー完了確認を受信
    /// </summary>
    [PunRPC]
    private void AcknowledgeClientDrawComplete()
    {
        if (IsHostPlayer) return;

        hostDrawCompleteAcknowledged.SetComplete();
    }

    /// <summary>
    /// ホスト側のドロー完了確認を待機
    /// </summary>
    private async Task WaitForHostDrawCompleteAcknowledgment()
    {
        if (!IsOnlineMode) return;

        // 既に確認済みの場合は即座に返す
        if (hostDrawCompleteAcknowledged.IsComplete)
        {
            return;
        }

        // 完了を待機
        await hostDrawCompleteAcknowledged.WaitForCompletion();

    }

    /// <summary>
    /// ドロー完了同期の初期化
    /// </summary>
    public void InitializeDrawSync()
    {
        // クライアントドロー完了同期を初期化
        clientDrawsCompleted.Reset();

        // ホスト側のドローフェイズ移行完了確認同期を初期化
        hostDrawPhaseTransitionComplete.Reset();

        // ホスト側のドロー完了確認同期を初期化
        hostDrawCompleteAcknowledged.Reset();

        // ターン切り替え完了を初期化
        clientTurnTransitionComplete.Reset();
        hostTurnTransitionComplete.Reset();
        peerEndPhaseApplied.Reset();

        // デッキ同期完了を初期化
        deckSyncCompleted.Reset();
    }

    /// <summary>
    /// 初期ドロー完了の通知
    /// </summary>
    public void NotifyInitialDrawCompletion()
    {
        SendRPC("ReceiveInitialDrawCompletion");
    }

    /// <summary>
    /// クライアントの初期ドロー完了を待機
    /// </summary>
    public async Task WaitForClientInitialDrawCompletion()
    {
        await WaitForInitialDrawCompletion();
    }

    /// <summary>
    /// 初期ドロー完了通知を受信
    /// </summary>
    [PunRPC]
    private void ReceiveInitialDrawCompletion()
    {
        if (!IsHostPlayer) return;

        clientDrawsCompleted.SetComplete();
    }

    /// <summary>
    /// 初期ドロー完了を待機（内部用）
    /// </summary>
    private async Task WaitForInitialDrawCompletion()
    {
        if (clientDrawsCompleted.IsComplete) return;

        await clientDrawsCompleted.WaitForCompletion();
    }

    /// <summary>
    /// クライアント側のターン切り替え完了を通知
    /// </summary>
    [PunRPC]
    private void NotifyClientTurnTransitionComplete()
    {
        if (!IsHostPlayer) return;

        clientTurnTransitionComplete.SetComplete();
    }

    /// <summary>
    /// ホスト側のターン切り替え完了を通知
    /// </summary>
    [PunRPC]
    private void NotifyHostTurnTransitionComplete()
    {
        if (IsHostPlayer) return;

        hostTurnTransitionComplete.SetComplete();
    }

    /// <summary>
    /// クライアント側のターン切り替え完了を待機
    /// </summary>
    private async Task WaitForClientTurnTransitionComplete()
    {
        if (!IsOnlineMode || !IsHostPlayer) return;

        // 既に完了している場合は即座に返す
        if (clientTurnTransitionComplete.IsComplete)
        {
            return;
        }

        // 完了を待機
        await clientTurnTransitionComplete.WaitForCompletion();

    }

    /// <summary>
    /// ホスト側のターン切り替え完了を待機
    /// </summary>
    private async Task WaitForHostTurnTransitionComplete()
    {
        if (!IsOnlineMode || IsHostPlayer) return;

        // 既に完了している場合は即座に返す
        if (hostTurnTransitionComplete.IsComplete)
        {
            return;
        }

        // 完了を待機
        await hostTurnTransitionComplete.WaitForCompletion();

    }
}
