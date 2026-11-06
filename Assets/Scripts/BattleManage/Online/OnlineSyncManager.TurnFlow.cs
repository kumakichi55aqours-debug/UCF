using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    bool phaseAdvanceInProgress;
    bool ownTurnRunning;
    // 相手がエンドフェイズの盤面処理を終えた通知。時間では待たない。
    SyncState peerEndPhaseApplied = new SyncState();
    // 相手から届いたフェイズ処理を、届いた順に1つずつ実行する
    Task opponentPhaseChain = Task.CompletedTask;

    // カード操作が可能かどうかを判定
    public bool CanPlayerOperateCards()
    {
        if (!IsOnlineMode) return true;
        if (PresentationBlocking) return false;
        if (phaseAdvanceInProgress) return false;

        return IsMyTurn;
    }

    /// <summary>
    /// 自分の手番を、ドローからエンドフェイズまで進める。ホストとクライアントで同じ手順。
    /// エンドフェイズに着くまでは、フェイズ操作を待って1つずつ進める。
    /// </summary>
    async Task RunOwnTurn()
    {
        if (ownTurnRunning || PhaseManager.Instance == null || BattleManager.Instance == null)
            return;

        ownTurnRunning = true;
        try
        {
            await StartOwnTurnPhases();

            while (PhaseManager.Instance.CurrentPhase != TurnPhase.エンドフェイズ)
            {
                await PhaseManager.Instance.WaitForPhaseInput();
                await HandlePhaseAdvance();
            }
        }
        finally
        {
            ownTurnRunning = false;
        }

        HandleNextTurn();
    }

    /// <summary>
    /// ドローとメインフェイズへの移行。相手へは、こちらでフェイズが切り替わってから知らせる。
    /// </summary>
    async Task StartOwnTurnPhases()
    {
        phaseAdvanceInProgress = true;
        try
        {
            await PhaseManager.Instance.ApplyPhaseAsync(TurnPhase.ドローフェイズ);
            SendRPC("BeginOpponentTurn");
            await BattleManager.Instance.ExecuteDrawPhase();

            await PhaseManager.Instance.ApplyPhaseAsync(TurnPhase.メインフェイズ);
            NotifyPhaseToOpponent(TurnPhase.メインフェイズ);
        }
        finally
        {
            phaseAdvanceInProgress = false;
        }
    }

    // 自分のターンで1フェイズ進め、相手へ知らせる
    async Task HandlePhaseAdvance()
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

    void NotifyPhaseToOpponent(TurnPhase phase)
    {
        SendRPC("ReceiveOpponentPhase", (int)phase);
    }

    [PunRPC]
    private void ReceiveOpponentPhase(int phaseInt)
    {
        var phase = (TurnPhase)phaseInt;
        EnqueueOpponentPhase(() => ApplyReceivedPhase(phase));
    }

    /// <summary>
    /// 相手の手番開始。こちらもドローフェイズに切り替え、相手のドローを表示する。
    /// </summary>
    [PunRPC]
    private void BeginOpponentTurn()
    {
        EnqueueOpponentPhase(ApplyOpponentDrawPhase);
    }

    async Task ApplyOpponentDrawPhase()
    {
        await PhaseManager.Instance.ApplyPhaseAsync(TurnPhase.ドローフェイズ);
        // 初回ターンは初期手札のあとなので、相手の追加ドローは無い
        if (BattleManager.Instance.curTurn > 1)
            await BattleManager.Instance.EnemyDrawCardAsync();
        BattleManager.Instance.ResetCurrentTurnUsedMoves();
    }

    void EnqueueOpponentPhase(System.Func<Task> work)
    {
        opponentPhaseChain = RunAfter(opponentPhaseChain, work);
    }

    static async Task RunAfter(Task previous, System.Func<Task> work)
    {
        await previous;
        try
        {
            await work();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"相手から届いたフェイズの実行でエラー: {ex}");
        }
    }

    /// <summary>
    /// フェイズを相手へ知らせる。エンドフェイズは、相手の盤面処理が終わるまで返す。
    /// </summary>
    async Task NotifyPhaseResolved(TurnPhase phase)
    {
        bool endPhase = phase == TurnPhase.エンドフェイズ;
        if (endPhase)
            peerEndPhaseApplied.Reset();

        NotifyPhaseToOpponent(phase);

        if (endPhase)
            await peerEndPhaseApplied.WaitForCompletion();
    }

    /// <summary>
    /// 相手から知らされたフェイズを、この盤面でも最後まで実行する。
    /// エンドフェイズは失敗しても完了を返し、送り主のターン進行を止めない。
    /// </summary>
    async Task ApplyReceivedPhase(TurnPhase phase)
    {
        try
        {
            await PhaseManager.Instance.ExecutePhase(phase);
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
    /// 自分の手番が終わったあとの処理。
    /// ホストはターンを進めて次の手番を始める。クライアントはホストへターン完了を送る。
    /// </summary>
    void HandleNextTurn()
    {
        if (!IsHostPlayer)
        {
            SendRPC("NotifyTurnComplete");
            return;
        }

        AdvanceToNextTurn().Forget("ターンの進行");
    }

    /// <summary>
    /// 開幕、またはターンを進めたあとに、今の手番側を開始する。ホストだけが呼ぶ。
    /// </summary>
    public async Task BeginActiveSideTurn()
    {
        if (BattleManager.Instance == null)
            return;

        if (BattleManager.Instance.IsActiveSideTurn)
            await RunOwnTurn();
        else
            SendRPC("BeginOwnTurn");
    }

    async Task AdvanceToNextTurn()
    {
        if (BattleManager.Instance == null)
            return;

        BattleManager.Instance.IncrementTurn();
        SyncTurnNumber(BattleManager.Instance.curTurn);
        await BeginActiveSideTurn();
    }

    /// <summary>
    /// ホストから、クライアントの手番開始を受け取る
    /// </summary>
    [PunRPC]
    private void BeginOwnTurn()
    {
        if (IsHostPlayer) return;

        RunOwnTurn().Forget("自分のターンの進行");
    }

    /// <summary>
    /// クライアントのターン完了を受け取る。クライアントの手番中に届いたものだけ受け付ける。
    /// </summary>
    [PunRPC]
    private void NotifyTurnComplete()
    {
        if (!IsHostPlayer || IsMyTurn)
            return;

        AdvanceToNextTurn().Forget("ターンの進行");
    }

    /// <summary>
    /// ドロー完了同期の初期化
    /// </summary>
    public void InitializeDrawSync()
    {
        clientDrawsCompleted.Reset();
        peerEndPhaseApplied.Reset();
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
        if (clientDrawsCompleted.IsComplete) return;

        await clientDrawsCompleted.WaitForCompletion();
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
}
