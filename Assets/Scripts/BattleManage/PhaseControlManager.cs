using System.Threading.Tasks;
using UnityEngine;
using Photon.Pun;

/// <summary>
/// フェイズ管理権の譲渡と制御を管理するクラス
/// </summary>
public class PhaseControlManager : MonoBehaviourPun
{
    public static PhaseControlManager Instance { get; private set; }
    
    // フェイズ管理権の状態
    private bool isPhaseController = false;
    private bool isHandoverInProgress = false;
    
    // フェイズ管理権の譲渡完了同期
    private bool isHandoverComplete = false;
    private TaskCompletionSource<bool> handoverCompleteSource = null;
    
    public bool IsPhaseController => isPhaseController;
    public bool IsHandoverInProgress => isHandoverInProgress;
    
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    
    /// <summary>
    /// フェイズ管理権の初期化
    /// </summary>
    public void InitializePhaseControl()
    {
        if (!OnlineSyncManager.Instance.IsOnlineMode)
        {
            // オフライン時は常にフェイズ管理権を持つ
            isPhaseController = true;
            return;
        }
        
        bool hostGoesFirst = BattleManager.Instance == null || BattleManager.Instance.ActiveSideGoesFirst;
        isPhaseController = hostGoesFirst == OnlineSyncManager.Instance.IsHostPlayer;
    }
    
    /// <summary>
    /// フェイズ管理権を次のターンプレイヤーに譲渡
    /// </summary>
    public async Task HandoverPhaseControl()
    {
        if (!OnlineSyncManager.Instance.IsOnlineMode)
        {
            Debug.Log("オフライン時: フェイズ管理権の譲渡は不要");
            return;
        }
        
        if (isHandoverInProgress)
        {
            Debug.LogWarning("フェイズ管理権の譲渡が既に進行中です");
            return;
        }
        
        isHandoverInProgress = true;
        Debug.Log($"フェイズ管理権の譲渡開始: 現在の管理権者={(isPhaseController ? "自分" : "相手")}");
        
        try
        {
            // 現在のターンがどちらのターンかを判定
            bool isCurrentTurnHost = BattleManager.Instance.IsActiveSideTurn;
            bool isNextTurnHost = !isCurrentTurnHost;
            
            if (isCurrentTurnHost != isNextTurnHost)
            {
                // ターンが切り替わる場合、フェイズ管理権を譲渡
                
                if (isPhaseController)
                {
                    // 自分が管理権を持っている場合、相手に譲渡
                    await TransferControlToOpponent();
                }
                else
                {
                    // 相手が管理権を持っている場合、自分が受領
                    await ReceiveControlFromOpponent();
                }
            }
            else
            {
                // 同じプレイヤーのターンが続く場合、管理権はそのまま
                Debug.Log("同じプレイヤーのターン継続: フェイズ管理権は変更なし");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"フェイズ管理権の譲渡でエラー: {ex.Message}");
            // エラー時の復旧処理
            await RecoverPhaseControl();
        }
        finally
        {
            isHandoverInProgress = false;
            Debug.Log($"フェイズ管理権の譲渡完了: 現在の管理権者={(isPhaseController ? "自分" : "相手")}");
        }
    }
    
    /// <summary>
    /// 相手にフェイズ管理権を譲渡
    /// </summary>
    private async Task TransferControlToOpponent()
    {
        Debug.Log("相手にフェイズ管理権を譲渡");
        
        // 譲渡完了フラグをリセット
        isHandoverComplete = false;
        
        // 相手に譲渡通知を送信
        photonView.RPC("ReceivePhaseControlHandover", RpcTarget.Others);
        
        // 相手からの譲渡完了確認を待機
        handoverCompleteSource = new TaskCompletionSource<bool>();
        await handoverCompleteSource.Task;
        
        // 自分の管理権を解除
        isPhaseController = false;
        Debug.Log("フェイズ管理権を相手に譲渡完了");
    }
    
    /// <summary>
    /// 相手からフェイズ管理権を受領
    /// </summary>
    private async Task ReceiveControlFromOpponent()
    {
        Debug.Log("相手からフェイズ管理権を受領");
        
        // 相手からの譲渡通知を待機（既に受信済みの場合は即座に処理）
        if (!isHandoverComplete)
        {
            handoverCompleteSource = new TaskCompletionSource<bool>();
            await handoverCompleteSource.Task;
        }
        
        // 自分の管理権を設定
        isPhaseController = true;
        Debug.Log("フェイズ管理権を受領完了");
    }
    
    /// <summary>
    /// フェイズ管理権の譲渡通知を受信
    /// </summary>
    [PunRPC]
    private void ReceivePhaseControlHandover()
    {
        Debug.Log("フェイズ管理権の譲渡通知を受信");
        
        // 譲渡完了を相手に通知
        photonView.RPC("AcknowledgePhaseControlHandover", RpcTarget.Others);
        
        // 自分の管理権を設定
        isPhaseController = true;
        isHandoverComplete = true;
        
        // 待機中のTaskCompletionSourceがあれば完了を通知
        if (handoverCompleteSource != null)
        {
            handoverCompleteSource.SetResult(true);
        }
        
        Debug.Log("フェイズ管理権の譲渡受領完了");
    }
    
    /// <summary>
    /// フェイズ管理権の譲渡完了確認を受信
    /// </summary>
    [PunRPC]
    private void AcknowledgePhaseControlHandover()
    {
        Debug.Log("フェイズ管理権の譲渡完了確認を受信");
        
        isHandoverComplete = true;
        
        // 待機中のTaskCompletionSourceがあれば完了を通知
        if (handoverCompleteSource != null)
        {
            handoverCompleteSource.SetResult(true);
        }
    }
    
    /// <summary>
    /// フェイズ管理権の復旧処理
    /// </summary>
    public Task RecoverPhaseControl()
    {
        Debug.Log("フェイズ管理権の復旧処理開始");
        
        try
        {
            // 現在のターンがどちらのターンかを判定
            bool isCurrentTurnHost = BattleManager.Instance.IsActiveSideTurn;
            bool shouldHaveControl = isCurrentTurnHost == OnlineSyncManager.Instance.IsHostPlayer;
            
            if (shouldHaveControl != isPhaseController)
            {
                Debug.Log($"フェイズ管理権の状態を修正: 現在={isPhaseController}, 正しい状態={shouldHaveControl}");
                isPhaseController = shouldHaveControl;
            }
            
            // 譲渡進行中のフラグをリセット
            isHandoverInProgress = false;
            isHandoverComplete = false;
            
            // 待機中のTaskCompletionSourceをクリア
            if (handoverCompleteSource != null)
            {
                handoverCompleteSource.TrySetCanceled();
                handoverCompleteSource = null;
            }
            
            Debug.Log("フェイズ管理権の復旧処理完了");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"フェイズ管理権の復旧処理でエラー: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// フェイズ管理権の状態を取得（デバッグ用）
    /// </summary>
    public string GetPhaseControlStatus()
    {
        return $"PhaseController: {isPhaseController}, HandoverInProgress: {isHandoverInProgress}, HandoverComplete: {isHandoverComplete}";
    }
}

