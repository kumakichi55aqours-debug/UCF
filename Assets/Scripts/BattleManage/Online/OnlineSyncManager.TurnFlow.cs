using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    // カード操作が可能かどうかを判定
    public bool CanPlayerOperateCards()
    {
        if (!IsOnlineMode) return true;
        if (PresentationBlocking) return false;

        return IsMyTurn;
    }

    // NextPhaseButtonの処理を管理
    public async void HandleNextPhaseButton()
    {
        if (IsHostPlayer)
        {
            // ホスト側：次のフェイズに進み、クライアントに通知
            await PhaseManager.Instance.NextPhase();
            NotifyPhaseToSClient(PhaseManager.Instance.CurrentPhase);
        }
        else
        {
            // クライアント側：自分のターン中は自分でフェイズを管理
            if (IsMyTurn)
            {
                TurnPhase currentPhase = PhaseManager.Instance.CurrentPhase;
                await PhaseManager.Instance.NextPhase();
                NotifyPhaseToHost(PhaseManager.Instance.CurrentPhase);

                // エンドフェイズに進んだ場合、ターン切り替えを実行
                if (currentPhase == TurnPhase.バトルフェイズ && PhaseManager.Instance.CurrentPhase == TurnPhase.エンドフェイズ)
                {
                    // エンドフェイズ処理完了を少し待ってからターン切り替え
                    await Task.Delay(100);
                    HandleNextTurn();
                }
            }
            else
            {
                // 相手のターン中はホストにリクエストを送信
                SendRPC("RequestNextPhase");
            }
        }
    }

    // クライアント側からのフェイズ進行リクエストを受信
    [PunRPC]
    private void RequestNextPhase()
    {
        if (!IsHostPlayer) return;

        // ホスト側で次のフェイズに進み、クライアントに通知
        _ = HandleHostNextPhase();
    }

    // ホスト側のフェイズ進行処理
    private async Task HandleHostNextPhase()
    {
        await PhaseManager.Instance.NextPhase();
        NotifyPhaseToSClient(PhaseManager.Instance.CurrentPhase);
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

        TurnPhase phase = (TurnPhase)phaseInt;

        // クライアント側でフェイズを実行
        _ = PhaseManager.Instance.ExecutePhase(phase);

        // フェイズ更新後にUIを更新
        PhaseManager.Instance.UpdatePhaseUI();
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

        TurnPhase phase = (TurnPhase)phaseInt;

        // ホスト側でフェイズを実行
        _ = PhaseManager.Instance.ExecutePhase(phase);

        // フェイズ更新後にUIを更新
        PhaseManager.Instance.UpdatePhaseUI();

        // PhaseGlowUIも更新
        FindObjectOfType<PhaseGlowUI>().HighlightPhase(phase);
    }

    /// <summary>
    /// 次のターンの処理を管理
    /// </summary>
    public async void HandleNextTurn()
    {
        if (IsHostPlayer)
        {

            // ホスト側：ターン数を増加
            BattleManager.Instance.IncrementTurn();

            // クライアント側にターン数を同期（即座に実行）
            SyncTurnNumber(BattleManager.Instance.curTurn);

            // 次のターンがどちらのターンかを判定
            bool isNextTurnHost = BattleManager.Instance.IsActiveSideTurn;

            if (isNextTurnHost)
            {
                // 次のターンはホストのターン
                // ホスト側のターン開始を通知
                SendRPC("StartHostTurn");

                // ホスト側のターンの最初のフェイズ（ドローフェイズ）を実行
                await PhaseManager.Instance.ExecutePhase(TurnPhase.ドローフェイズ);

                // ドローフェイズ完了後、メインフェイズに移行してクライアントに通知
                await PhaseManager.Instance.ExecutePhase(TurnPhase.メインフェイズ);
                NotifyPhaseToSClient(TurnPhase.メインフェイズ);
            }
            else
            {
                await BeginOpeningClientTurn();
            }

        }
        else
        {
            // クライアント側：ターン完了をホストに通知
            SendRPC("NotifyTurnComplete");
        }
    }

    /// <summary>
    /// クライアントのターンを開始する。ターン数は増やさない。
    /// 開幕でクライアントが先攻のときと、ホストのターン終了後に使う。
    /// </summary>
    public async Task BeginOpeningClientTurn()
    {
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
        if (!IsHostPlayer) return;

        // ホスト側でターン数を増加し、次のターンを開始
        BattleManager.Instance.IncrementTurn();

        // クライアント側にターン数を同期
        SyncTurnNumber(BattleManager.Instance.curTurn);

        // 次のターン（ホストターン）を開始
        _ = StartNextHostTurn();
    }

    /// <summary>
    /// 次のホストターンを開始
    /// </summary>
    private async Task StartNextHostTurn()
    {

        // 短時間待機してから開始
        await Task.Delay(1000);

        // ホスト側のターン開始を通知
        SendRPC("StartHostTurn");

        // ホスト側でターン開始
        PhaseManager.Instance.StartTurn();
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
            await BattleManager.Instance.EnemyDrawCardAsync();

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
        if (IsHostPlayer) return;

        // クライアント側でターン切り替えの間を実行
        _ = ExecuteTurnTransitionPhase();
    }

    /// <summary>
    /// クライアント側のターン切り替えの間の実行
    /// </summary>
    private async Task ExecuteTurnTransitionPhase()
    {
        // クライアント側もターン切り替えの間に移行
        isTurnTransitionPhase = true;

        // 短時間待機（ターン切り替えの間の演出）
        await Task.Delay(500);

        // ターン切り替えの間が完了
        isTurnTransitionPhase = false;

        // ホスト側にターン切り替え完了を通知
        SendRPC("NotifyClientTurnTransitionComplete");

        // ホスト側のターン切り替え完了を待つ
        await WaitForHostTurnTransitionComplete();

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

        // フラグをリセット（前回の完了状態をクリア）
        clientDrawsCompleted.Reset();

        // 完了を待機
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
