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

            // 先攻がホストなら奇数ターンがホスト、後攻なら偶数ターンがホスト
            bool isHostTurn = BattleManager.Instance.IsActiveSideTurn;
            return isHostTurn == IsHostPlayer;
        }
    }

    // ゲーム開始前同期待機。先に届いた準備完了は消さない。
    bool opponentReadyForBattle;
    bool opponentHasSeenLocalReady;

    // 手札同期
    private List<DeckCard> opponentHand = new List<DeckCard>();

    // 墓地同期
    private List<DeckCard> opponentGrave = new List<DeckCard>();

    // 異次元同期。相手の異次元にあるカード
    private List<DeckCard> opponentDimension = new List<DeckCard>();

    // デッキ同期
    private SyncState deckSyncCompleted = new SyncState();

    // スキル発動アニメーションACK同期
    private SyncState skillActivationAck = new SyncState();

    // 効果内の相手アニメーション完了待ち
    private readonly Queue<TaskCompletionSource<bool>> opponentAnimationAcks = new Queue<TaskCompletionSource<bool>>();
    private readonly object opponentAnimationAckLock = new object();

    // 手札追加アニメは共有FIFOと混ぜない。requestIdで移動完了と強調完了を突き合わせる。
    int nextAddToHandRequestId = 1;
    int activeAddToHandRequestId = -1;
    readonly Dictionary<int, TaskCompletionSource<bool>> addToHandMoveAcks = new Dictionary<int, TaskCompletionSource<bool>>();
    readonly Dictionary<int, TaskCompletionSource<bool>> addToHandEmphasisAcks = new Dictionary<int, TaskCompletionSource<bool>>();
    readonly Dictionary<int, TaskCompletionSource<bool>> addToHandEmphasisContinues = new Dictionary<int, TaskCompletionSource<bool>>();
    readonly HashSet<int> addToHandEmphasisContinuePending = new HashSet<int>();
    readonly object addToHandAckLock = new object();
    const int AddToHandAckTimeoutMs = 15000;

    // 相手に作業させ、完了RPCまで待つ（requestIdなし）
    SyncState skillDrawFinished = new SyncState();
    SyncState opponentOwnHandFinished = new SyncState();

    // 相手リモート選択（同時に1件。requestIdで応答を突き合わせる）
    readonly RemoteRequest<bool> opponentYesNo = new RemoteRequest<bool>();
    readonly RemoteRequest<int> opponentSelect = new RemoteRequest<int>();
    readonly RemoteRequest<int[]> opponentCardPick = new RemoteRequest<int[]>();

    // 相手離脱検知の一度きり実行ガード
    private bool opponentLeft = false;

    // 手札確認などのプレゼンテーション中は操作を禁止する。入れ子でも解除漏れしないよう深さで持つ。
    int presentationBlockDepth;

    public bool PresentationBlocking => presentationBlockDepth > 0;

    public void BeginOperationBlock()
    {
        if (presentationBlockDepth == 0)
            CardContextMenu.DismissTransientWindows();
        presentationBlockDepth++;
    }

    public void EndOperationBlock()
    {
        if (presentationBlockDepth > 0)
            presentationBlockDepth--;
    }

    // クライアントの初期ドロー完了同期
    private SyncState clientDrawsCompleted = new SyncState();

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
    /// 1回分の完了通知を待つ。
    /// Reset するたびに世代を進め、前の完了が次の待ちに残らないようにする。
    /// </summary>
    private class SyncState
    {
        int cycle;
        int completedCycle = -1;
        TaskCompletionSource<bool> source;

        public bool IsComplete => completedCycle == cycle;

        public void SetComplete()
        {
            if (completedCycle == cycle)
                return;

            completedCycle = cycle;
            source?.TrySetResult(true);
        }

        public async Task WaitForCompletion()
        {
            if (IsComplete)
                return;

            var wait = source ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (IsComplete)
                wait.TrySetResult(true);

            await wait.Task;
        }

        /// <summary>
        /// 次の待ちを始める。進行中の前の待ちは解放し、完了フラグは新しい世代へ持ち越さない。
        /// </summary>
        public void Reset()
        {
            cycle++;
            var pending = source;
            source = null;
            pending?.TrySetResult(true);
        }
    }

    /// <summary>
    /// 相手へ依頼し、requestId 付きの応答を待つ。同時進行は1件まで。
    /// </summary>
    class RemoteRequest<T>
    {
        TaskCompletionSource<T> pending;
        int counter;
        int currentId = -1;

        public async Task WaitTurn()
        {
            while (pending != null)
            {
                var wait = pending;
                if (!wait.Task.IsCompleted)
                    await wait.Task;
                else
                    await Task.Yield();
            }
        }

        public int Begin()
        {
            pending = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            currentId = ++counter;
            return currentId;
        }

        public async Task<T> AwaitAndClear()
        {
            try
            {
                return await pending.Task;
            }
            finally
            {
                pending = null;
                currentId = -1;
            }
        }

        public bool TryComplete(int requestId, T result)
        {
            if (pending == null || requestId != currentId)
                return false;
            return pending.TrySetResult(result);
        }
    }

    /// <summary>
    /// 相手に作業RPCを送り、ack の完了まで待つ。
    /// </summary>
    async Task AwaitRemoteAction(SyncState ack, System.Action send, bool blockOperations = true)
    {
        ack.Reset();
        if (blockOperations)
            BeginOperationBlock();
        try
        {
            send();
            await ack.WaitForCompletion();
        }
        finally
        {
            if (blockOperations)
                EndOperationBlock();
        }
    }

    /// <summary>
    /// 相手に選択を依頼し、結果が届くまで待つ。開始できないときは unavailable を返す。
    /// </summary>
    async Task<T> RequestFromOpponent<T>(
        RemoteRequest<T> slot,
        CardController sourceCard,
        System.Action<int> send,
        T unavailable,
        string label)
    {
        if (!IsOnlineMode || sourceCard == null)
        {
            Debug.LogWarning($"[OnlineSyncManager] {label}を開始できないため、既定値を返します。");
            return unavailable;
        }

        await slot.WaitTurn();
        int id = slot.Begin();
        send(id);
        return await slot.AwaitAndClear();
    }

    static object[] BuildCardRpcArgs(CardAddress address, params object[] extra)
    {
        if (extra == null || extra.Length == 0)
            return new object[] { address.Kind.ToString(), address.Id, address.ZoneName, address.Index };

        var args = new object[4 + extra.Length];
        args[0] = address.Kind.ToString();
        args[1] = address.Id;
        args[2] = address.ZoneName;
        args[3] = address.Index;
        System.Array.Copy(extra, 0, args, 4, extra.Length);
        return args;
    }

    /// <summary>
    /// カード住所を先頭4引数にした RPC を送る。
    /// </summary>
    void SendCardRpc(string method, CardAddress address, params object[] extra)
    {
        SendRPC(method, BuildCardRpcArgs(address, extra));
    }

    /// <summary>
    /// カードから住所を取り、取れれば SendCardRpc する。
    /// </summary>
    bool TrySendCardRpc(string method, CardController card, params object[] extra)
    {
        if (!CardAddress.TryFrom(card, out CardAddress address))
            return false;

        SendCardRpc(method, address, extra);
        return true;
    }

    /// <summary>
    /// 2枚の住所を先頭8引数にした RPC を送る。
    /// </summary>
    bool TrySendTwoCardRpc(string method, CardController first, CardController second, params object[] extra)
    {
        if (!CardAddress.TryFrom(first, out CardAddress a) || !CardAddress.TryFrom(second, out CardAddress b))
            return false;

        if (extra == null || extra.Length == 0)
        {
            SendRPC(
                method,
                a.Kind.ToString(), a.Id, a.ZoneName, a.Index,
                b.Kind.ToString(), b.Id, b.ZoneName, b.Index);
            return true;
        }

        var args = new object[8 + extra.Length];
        args[0] = a.Kind.ToString();
        args[1] = a.Id;
        args[2] = a.ZoneName;
        args[3] = a.Index;
        args[4] = b.Kind.ToString();
        args[5] = b.Id;
        args[6] = b.ZoneName;
        args[7] = b.Index;
        System.Array.Copy(extra, 0, args, 8, extra.Length);
        SendRPC(method, args);
        return true;
    }

    /// <summary>
    /// 住所からカードを解決して処理する。見つからない・例外時は onMissing を呼ぶ。
    /// </summary>
    void WithResolvedCard(
        string kind,
        int id,
        string zone,
        int index,
        string errorLabel,
        System.Action<CardController> onFound,
        System.Action onMissing = null)
    {
        try
        {
            CardController card = CardAddress.Resolve(kind, id, zone, index);
            if (card != null)
                onFound(card);
            else
                onMissing?.Invoke();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] {errorLabel}: {ex.Message}");
            onMissing?.Invoke();
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
