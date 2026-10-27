using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// オンライン対戦時の同期処理を管理するクラス
/// </summary>
public partial class OnlineSyncManager : MonoBehaviourPunCallbacks
{
    public static OnlineSyncManager Instance { get; private set; }

    // オンライン状態管理
    public bool IsOnlineMode => !PhotonNetwork.OfflineMode;
    public bool IsHostPlayer => PhotonNetwork.IsMasterClient;

    // ターン管理
    public bool IsMyTurn
    {
        get
        {
            if (BattleManager.Instance == null)
                return false;

            // オフラインではプレイヤーが先攻側かどうかで判定する
            if (!IsOnlineMode)
                return BattleManager.Instance.IsActiveSideTurn;

            // ターン切り替えの間の場合は操作不可
            if (isTurnTransitionPhase) return false;

            // 先攻がホストなら奇数ターンがホスト、後攻なら偶数ターンがホスト
            bool isHostTurn = BattleManager.Instance.IsActiveSideTurn;
            return isHostTurn == IsHostPlayer;
        }
    }

    // ゲーム開始前同期待機。先に届いた準備完了は消さない。
    bool opponentReadyForBattle;
    bool opponentHasSeenLocalReady;

    // ターン切り替えの間の管理
    private bool isTurnTransitionPhase = false;

    // ターン切り替え完了同期
    private SyncState clientTurnTransitionComplete = new SyncState();
    private SyncState hostTurnTransitionComplete = new SyncState();

    // 手札同期
    private List<DeckCard> opponentHand = new List<DeckCard>();
    private bool isOpponentHandReceived = false;

    // 墓地同期
    private List<DeckCard> opponentGrave = new List<DeckCard>();

    // デッキ同期
    private SyncState deckSyncCompleted = new SyncState();

    // スキル発動アニメーションACK同期
    private SyncState skillActivationAck = new SyncState();

    // 効果内の相手アニメーション完了待ち
    private readonly Queue<TaskCompletionSource<bool>> opponentAnimationAcks = new Queue<TaskCompletionSource<bool>>();
    private readonly object opponentAnimationAckLock = new object();
    private TaskCompletionSource<bool> lastOpponentAnimationAck;

    // 相手によるYes/No意思決定待機用
    private TaskCompletionSource<bool> opponentDecisionTcs;
    private int opponentDecisionRequestIdCounter = 0;
    private int currentOpponentDecisionRequestId = -1;
    private int pendingOpponentDecisionRequestId = -1;

    // 相手による選択肢ボタンの意思決定待機用
    private TaskCompletionSource<int> opponentSelectTcs;
    private int opponentSelectRequestIdCounter = 0;
    private int currentOpponentSelectRequestId = -1;
    private int pendingOpponentSelectRequestId = -1;

    // 相手に候補カードから選ばせる待機用
    private TaskCompletionSource<int[]> opponentCardPickTcs;
    private int opponentCardPickRequestIdCounter = 0;
    private int currentOpponentCardPickRequestId = -1;

    // 相手離脱検知の一度きり実行ガード
    private bool opponentLeft = false;

    // 手札確認などのプレゼンテーション中は操作を禁止するフラグ
    public bool PresentationBlocking { get; private set; } = false;

    public void BeginOperationBlock()
    {
        PresentationBlocking = true;
        CardContextMenu.DismissTransientWindows();
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
    }

    public void EndOperationBlock()
    {
        PresentationBlocking = false;
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
    }

    // クライアントドロー完了同期
    private SyncState clientDrawsCompleted = new SyncState();

    // ホスト側のドローフェイズ移行完了確認同期
    private SyncState hostDrawPhaseTransitionComplete = new SyncState();

    // ホスト側のドロー完了確認同期
    private SyncState hostDrawCompleteAcknowledged = new SyncState();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    #region 共通ユーティリティ
    /// <summary>
    /// RPC送信の共通メソッド
    /// </summary>
    public void SendRPC(string methodName, params object[] parameters)
    {
        if (IsOnlineMode)
        {
            try
            {
                photonView.RPC(methodName, RpcTarget.Others, parameters);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"RPC送信エラー ({methodName}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 同期状態管理の共通クラス
    /// </summary>
    private class SyncState
    {
        public bool IsComplete { get; set; } = false;
        public TaskCompletionSource<bool> Source { get; set; } = null;

        // 完了状態を設定
        public void SetComplete()
        {
            IsComplete = true;
            if (Source != null && !Source.Task.IsCompleted)
            {
                Source.TrySetResult(true);
            }
        }

        // 完了を待機
        public async Task WaitForCompletion()
        {
            if (IsComplete) return;

            Source = new TaskCompletionSource<bool>();
            await Source.Task;
        }

        // 状態をリセット
        public void Reset()
        {
            IsComplete = false;
            Source = null;
        }
    }
    #endregion

    #region ゲーム開始時処理
    // 相手プレイヤーの退室時処理
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        // バトルシーン中のオンライン対戦で相手が抜けた場合の処理
        if (opponentLeft) return;
        if (PhotonNetwork.OfflineMode) return;

        var active = SceneManager.GetActiveScene().name;
        if (active != null && active == "BattleScene")
        {
            opponentLeft = true;
            StartCoroutine(HandleOpponentLeftAndReturnToTitle());
        }
    }

    // 自分がルームを抜けた時の処理
    public override void OnLeftRoom()
    {
        // ただし、opponentLeft == true の場合は相手退出時の処理なのでスキップ
        if (!opponentLeft && SceneManager.GetActiveScene().name == "BattleScene")
            BattleManager.Instance?.LoadTitleScene();
    }

    private IEnumerator HandleOpponentLeftAndReturnToTitle()
    {
        MessageManager.Instance.ShowMessage("対戦相手がいなくなりました");
        yield return new WaitForSeconds(2f);
        MessageManager.Instance.HideMessage();

        if (GrantSkillSyncManager.Instance != null)
            Destroy(GrantSkillSyncManager.Instance.gameObject);

        // オンラインモードを完全に切断してオフラインモードに戻す
        // Disconnect中にシーン遷移するとSetPropertiesエラーが出るため、先に無効化
        PhotonNetwork.AutomaticallySyncScene = false;

        // DontDestroyOnLoadしているPhotonViewが残るのを防ぐため、明示的に破棄
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        PhotonNetwork.OfflineMode = true;
        BattleLaunchContext.LoadReturnScene();
    }

    // ＜ゲーム開始前＞両プレイヤーの準備完了の待機。
    // シーン到着前に落ちたRPCは、相手が見えるまで送り直す。
    public async Task WaitForBothPlayersReady()
    {
        while (true)
        {
            SendRPC("NotifyPlayerReady", opponentReadyForBattle);
            if (opponentReadyForBattle && opponentHasSeenLocalReady)
                break;
            await Task.Delay(100);
        }
    }

    // ＜ゲーム開始前＞プレイヤー準備完了のRPC通知
    [PunRPC]
    private void NotifyPlayerReady(bool senderHasSeenUs)
    {
        opponentReadyForBattle = true;
        if (senderHasSeenUs)
            opponentHasSeenLocalReady = true;
    }

    // ターン数の同期
    public void SyncTurnNumber(int turn)
    {
        ReceiveTurnNumber(turn);
        SendRPC("ReceiveTurnNumber", turn);
    }

    // ターン数受信のRPC
    [PunRPC]
    private void ReceiveTurnNumber(int newTurn)
    {
        if (IsOnlineMode && BattleManager.Instance != null)
        {
            BattleManager.Instance.curTurn = newTurn;

            // 期限切れのパワーボーナスをクリーンアップしてUIを更新
            BattleManager.Instance.RemovePowerBonuses();
            PowerBonusUI.Instance?.UpdateUI();
        }
    }
    #endregion

}
