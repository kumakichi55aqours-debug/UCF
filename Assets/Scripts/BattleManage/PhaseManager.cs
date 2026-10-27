using System.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TurnPhase
{
    ドローフェイズ,
    メインフェイズ,
    バトルフェイズ,
    エンドフェイズ
}

public class PhaseManager : MonoBehaviour
{
    [Header("フェーズ画像（現在表示されているもの）")]
    [SerializeField] private Image drawImage;
    [SerializeField] private Image mainImage;
    [SerializeField] private Image battleImage;
    [SerializeField] private Image endImage;

    [Header("光るマテリアル")]
    [SerializeField] private Material glowMaterial;

    [Header("通常のマテリアル")]
    [SerializeField] private Material defaultMaterial;

    public static PhaseManager Instance { get; private set; }
    public TurnPhase CurrentPhase { get; set; }
    bool phaseInitialized;
    bool currentPhaseOwnedByLocal;
    public bool OwnsCurrentPhase => phaseInitialized && currentPhaseOwnedByLocal;

    [Header("フェーズ進行ボタン")]
    [SerializeField] private Button nextPhaseButton;
    int playingTweenCount;
    

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        nextPhaseButton.gameObject.SetActive(false);
    }

    void Update()
    {
        int playing = DOTween.TotalPlayingTweens();
        if (playing == playingTweenCount)
            return;

        bool started = playingTweenCount == 0 && playing > 0;
        playingTweenCount = playing;
        if (started)
            CardContextMenu.DismissTransientWindows();
        RefreshNextPhaseButtonVisibility();
    }

    /// <summary>
    /// ターン開始処理
    /// </summary>
    public void StartTurn()
    {
        // オンライン対戦時のフェーズ進行制御
        if (OnlineSyncManager.Instance.IsOnlineMode)
        {
            if (OnlineSyncManager.Instance.IsHostPlayer)
            {
                if (OnlineSyncManager.Instance.IsMyTurn)
                    _ = ExecuteInitialPhase();
                else
                    _ = OnlineSyncManager.Instance.BeginOpeningClientTurn();
            }
            // クライアントはホストからの通知でターンを開始する
        }
        else
        {
            // オフライン時は通常通りフェーズ進行を開始
            _ = ExecuteOfflinePhases();
        }
    }

    /// <summary>
    /// 最初のフェーズ(ドローフェイズ)のみを実行(オンライン専用)
    /// </summary>
    private async Task ExecuteInitialPhase()
    {
        // ドローフェイズの実行（同期処理付き）
        await ExecuteDrawPhaseWithSync();

        // メインフェイズへの移行（同期処理付き）
        await TransitionToMainPhaseWithSync();

        // ホスト側の場合はNextPhaseButtonを有効化してプレイヤー操作を待つ
        if (OnlineSyncManager.Instance.IsOnlineMode && OnlineSyncManager.Instance.IsHostPlayer)
        {
            await WaitForPlayerAction("バトルフェイズへ");
        }

        // バトルフェイズ以降の処理を続行
        await ExecuteRemainingPhases();
    }

    /// <summary>
    /// ドローフェイズの実行（同期処理付き）
    /// </summary>
    private async Task ExecuteDrawPhaseWithSync()
    {
        // ドローフェイズの実行（同期処理付き）
        await ApplyPhaseAsync(TurnPhase.ドローフェイズ);
        
        // ドローフェイズの実行
        await BattleManager.Instance.ExecuteDrawPhase();
    }

    /// <summary>
    /// メインフェイズへの移行
    /// </summary>
    private async Task TransitionToMainPhaseWithSync()
    {
        await ApplyPhaseAsync(TurnPhase.メインフェイズ);
        
        // オンライン対戦時の同期処理
        if (OnlineSyncManager.Instance.IsOnlineMode && OnlineSyncManager.Instance.IsHostPlayer)
        {
            // ホスト側：メインフェイズへの移行をスレーブに通知
            OnlineSyncManager.Instance.NotifyPhaseToSClient(TurnPhase.メインフェイズ);
        }
    }

    /// <summary>
    /// スレーブ側の初期フェーズ実行
    /// </summary>
    private async Task ExecuteSlaveInitialPhase()
    {
        // ドローフェイズの実行
        await ExecuteDrawPhaseWithSync();

        // メインフェイズへの移行
        await TransitionToSlaveMainPhase();

        // スレーブ側の場合もNextPhaseButtonを有効化してプレイヤー操作を待つ
        await WaitForPlayerAction("バトルフェイズへ");

        // バトルフェイズ以降の処理を続行
        await ExecuteSlaveRemainingPhases();
    }
    
    /// <summary>
    /// スレーブ側のメインフェイズへの移行
    /// </summary>
    private async Task TransitionToSlaveMainPhase()
    {
        await ApplyPhaseAsync(TurnPhase.メインフェイズ);
        
        // スレーブ側：メインフェイズへの移行をホストに通知
        OnlineSyncManager.Instance.NotifyPhaseToHost(TurnPhase.メインフェイズ);
    }
    
    /// <summary>
    /// スレーブ側のバトルフェイズ以降のフェーズを実行
    /// </summary>
    private async Task ExecuteSlaveRemainingPhases()
    {
        // 現在のターンがどちらのターンかを判定
        bool isHostTurn = BattleManager.Instance.IsActiveSideTurn;
        string currentPlayer = isHostTurn ? "ホスト" : "スレーブ";
        // バトルフェイズ（プレイヤー操作）
        // WaitForPlayerActionでボタンを待ち、ボタンが押されるとHandleNextPhaseButton→NextPhase()が呼ばれる
        // NextPhase()でエンドフェイズが処理されるため、ここではエンドフェイズを直接呼ばない
        await ExecutePhase(TurnPhase.バトルフェイズ, currentPlayer, async () => await WaitForPlayerAction("エンドフェイズへ"), true);
        
        // エンドフェイズはNextPhase()で処理されるため、ここでは呼ばない
        // スレーブ側でのターン切り替えはHandleNextPhaseButtonで処理されるため、ここでは呼ばない
    }
    
    /// <summary>
    /// バトルフェイズ以降のフェーズを実行
    /// </summary>
    private async Task ExecuteRemainingPhases()
    {
        // 現在のターンがどちらのターンかを判定
        bool isHostTurn = BattleManager.Instance.IsActiveSideTurn;
        string currentPlayer = isHostTurn ? "ホスト" : "スレーブ";
        
        // バトルフェイズ（プレイヤー操作）
        // WaitForPlayerActionでボタンを待ち、ボタンが押されるとHandleNextPhaseButton→NextPhase()が呼ばれる
        // NextPhase()でエンドフェイズが処理されるため、ここではエンドフェイズを直接呼ばない
        await ExecutePhase(TurnPhase.バトルフェイズ, currentPlayer, async () => await WaitForPlayerAction("エンドフェイズへ"), true);
        
        // エンドフェイズはNextPhase()で処理されるため、ここでは呼ばない
        
        // 次のターンに進む
        NextTurn();
    }

    /// <summary>
    /// オフライン時のフェーズ進行処理
    /// </summary>
    public async Task ExecuteOfflinePhases()
    {
        // オフライン時は先攻側が奇数ターン
        bool isPlayerTurn = BattleManager.Instance.IsActiveSideTurn;
        string currentPlayer = isPlayerTurn ? "プレイヤー" : "エネミー";

        // ドローフェイズ（自動実行）
        await ExecutePhase(TurnPhase.ドローフェイズ, currentPlayer, async () => await BattleManager.Instance.ExecuteDrawPhase(), false);

        // メインフェイズ・バトルフェイズ
        if (isPlayerTurn)
        {
            // プレイヤーのメインフェイズ：プレイヤー操作待ち（NextPhaseButtonで進行）
            await ExecutePhase(TurnPhase.メインフェイズ, currentPlayer, async () => await WaitForPlayerAction("バトルフェイズへ"), false);

            // プレイヤーのバトルフェイズ：プレイヤー操作待ち（NextPhaseButtonで進行）
            await ExecutePhase(TurnPhase.バトルフェイズ, currentPlayer, async () => await WaitForPlayerAction("エンドフェイズへ"), false);

            if (BattleManager.Instance.pResources.Hp.currentHp == 0 || BattleManager.Instance.eResources.Hp.currentHp == 0) return;

            NextTurn();
        }
        else
        {
            // エネミーターン：メインフェイズ・バトルフェイズをEnemyActionManagerに委譲
            await ExecutePhase(TurnPhase.メインフェイズ, currentPlayer, async () => await EnemyActionManager.Instance.ExecuteEnemyMainPhase(), false);

            await ExecutePhase(TurnPhase.バトルフェイズ, currentPlayer, async () => await EnemyActionManager.Instance.ExecuteEnemyBattlePhase(), false);

            if (BattleManager.Instance.pResources.Hp.currentHp == 0 || BattleManager.Instance.eResources.Hp.currentHp == 0) return;

            // エネミーのエンドフェイズ（自動実行）
            await ExecutePhase(TurnPhase.エンドフェイズ, currentPlayer, async () => await BattleManager.Instance.ExecuteEndPhase(), false);

            NextTurn();
        }
    }
    

    /// <summary>
    /// 統一されたフェーズ実行処理（オンライン/オフライン共通）
    /// </summary>
    private async Task ExecutePhase(TurnPhase phase, string playerType, System.Func<Task> phaseAction, bool isOnlineMode = false)
    {
        await ApplyPhaseAsync(phase);
        
        await phaseAction();
        
        // フェーズ完了後にUI更新（次のフェーズの準備）
        UpdatePhaseUI();
        
        // フェーズ間の待機時間（オンライン/オフラインで調整）
        if (phase == TurnPhase.ドローフェイズ || phase == TurnPhase.エンドフェイズ)
        {
            int delayTime = isOnlineMode ? 10 : 100; // オンライン時は最小限、オフライン時は少し長め
            await Task.Delay(delayTime);
        }
    }

    /// <summary>
    /// プレイヤーの操作を待つ
    /// </summary>
    private async Task WaitForPlayerAction(string buttonText)
    {
        // 最小限の待機時間
        await Task.Delay(10);
        
        if (OnlineSyncManager.Instance.IsOnlineMode)
        {
            // オンライン対戦時
            if (OnlineSyncManager.Instance.IsMyTurn)
            {
                SetNextPhaseButtonText(buttonText);
                EnableNextPhaseButton();
                await WaitForNextPhaseButton();
                DisableNextPhaseButton();
            }
            else
            {
                // 相手のターン中はボタンを確実に無効化
                DisableNextPhaseButton();
                // ホスト主導方式では、ホストからの通知を待つだけ
                await Task.Delay(10);
            }
        }
        else
        {
            // オフライン時：常にプレイヤー操作待ち
            SetNextPhaseButtonText(buttonText);
            EnableNextPhaseButton();
            await WaitForNextPhaseButton();
            DisableNextPhaseButton();
        }
    }

    private async void NextTurn()
    {
        await Task.Delay(500);

        // オンライン対戦時はOnlineSyncManagerに処理を委譲（ターン数増減も含む）
        if (OnlineSyncManager.Instance.IsOnlineMode)
        {
            // 重複呼び出しを防ぐため、一度だけ呼び出す
            OnlineSyncManager.Instance.HandleNextTurn();
        }
        else
        {
            // オフライン時のみターン数を増加
            BattleManager.Instance.IncrementTurn();

            // オフライン時は直接次のターンを開始
            _ = ExecuteOfflinePhases();
        }
    }

    private void EnableNextPhaseButton()
    {
        if (IsNextPhaseSuppressed())
        {
            nextPhaseButton.gameObject.SetActive(false);
            return;
        }

        bool shouldEnable = false;
        
        if (OnlineSyncManager.Instance.IsOnlineMode)
        {
            // オンライン対戦時：自分のターンの場合のみ有効
            shouldEnable = OnlineSyncManager.Instance.IsMyTurn;
        }
        else
        {
            // オフライン時：プレイヤーターンの場合のみ有効
            bool isPlayerTurn = BattleManager.Instance.IsActiveSideTurn;
            shouldEnable = isPlayerTurn;
        }
        
        if (shouldEnable)
        {
            nextPhaseButton.gameObject.SetActive(true);
            nextPhaseButton.interactable = true;
            
            // オンライン対戦時はリスナーを設定
            if (OnlineSyncManager.Instance.IsOnlineMode)
            {
                nextPhaseButton.onClick.RemoveAllListeners();
                nextPhaseButton.onClick.AddListener(() => 
                {
                    CardContextMenu.DismissTransientWindows();
                    OnlineSyncManager.Instance.HandleNextPhaseButton();
                });
            }
        }
        else
        {
            nextPhaseButton.gameObject.SetActive(false);
            nextPhaseButton.interactable = false;
        }
    }

    private void DisableNextPhaseButton()
    {
        nextPhaseButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// 次のフェーズを取得
    /// </summary>
    private TurnPhase GetNextPhase(TurnPhase currentPhase)
    {
        switch (currentPhase)
        {
            case TurnPhase.ドローフェイズ:
                return TurnPhase.メインフェイズ;
            case TurnPhase.メインフェイズ:
                return TurnPhase.バトルフェイズ;
            case TurnPhase.バトルフェイズ:
                return TurnPhase.エンドフェイズ;
            case TurnPhase.エンドフェイズ:
                return TurnPhase.ドローフェイズ; // 次のターンのドローフェイズ
            default:
                return TurnPhase.ドローフェイズ;
        }
    }

    private void SetNextPhaseButtonText(string text)
    {
        TextMeshProUGUI buttonText = nextPhaseButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            buttonText.text = text;
        }
    }

    private async Task WaitForNextPhaseButton()
    {
        var tcs = new TaskCompletionSource<bool>();
        nextPhaseButton.onClick.RemoveAllListeners();
        nextPhaseButton.onClick.AddListener(async () => 
        {
            CardContextMenu.DismissTransientWindows();

            // 重複クリック防止：既に完了している場合は処理をスキップ
            if (tcs.Task.IsCompleted)
            {
                return;
            }
            
            
            // 即座にボタンを非表示にして重複クリックを防ぐ
            DisableNextPhaseButton();
            
            // オンライン対戦時はOnlineSyncManagerに処理を委譲
            if (OnlineSyncManager.Instance.IsOnlineMode)
            {
                OnlineSyncManager.Instance.HandleNextPhaseButton();
            }
            else
            {
                // オフライン時：フェイズを直接更新
                TurnPhase currentPhase = CurrentPhase;
                TurnPhase nextPhase = GetNextPhase(currentPhase);

                await ApplyPhaseAsync(nextPhase);
                
                // エンドフェイズの場合は次のターンに進む
                if (IsEndPhase(nextPhase))
                {
                    await Task.Delay(100);
                    
                    // エンドフェイズの実際の処理を実行
                    if (!OnlineSyncManager.Instance.IsOnlineMode)
                        await BattleManager.Instance.ExecuteEndPhase();
                }
                else
                {
                    // 次のフェイズがプレイヤー操作待ちの場合はボタンを再度有効化
                    if (nextPhase == TurnPhase.メインフェイズ || nextPhase == TurnPhase.バトルフェイズ)
                    {
                        await Task.Delay(100);
                        UpdatePhaseUI();
                    }
                }

                await Task.Delay(200);
            }
            
            // TrySetResultを使用して重複呼び出しを防ぐ
            tcs.TrySetResult(true);
        });
        await tcs.Task;
    }

    /// <summary>
    /// フェイズを切り替え、自分のフェイズ開始時・終了時のスキル確認を待つ。
    /// 同じフェイズへの再設定ではスキルは走らない。
    /// </summary>
    public async Task ApplyPhaseAsync(TurnPhase next)
    {
        bool changed = !phaseInitialized || CurrentPhase != next;
        TurnPhase previous = CurrentPhase;
        bool leavingOwnedByLocal = phaseInitialized && currentPhaseOwnedByLocal;
        bool enteringOwnedByLocal = SimulatesCurrentPhase();

        if (changed)
        {
            CurrentPhase = next;
            currentPhaseOwnedByLocal = enteringOwnedByLocal;
            phaseInitialized = true;
        }

        var glow = FindObjectOfType<PhaseGlowUI>();
        if (glow != null)
            glow.HighlightPhase(CurrentPhase);

        if (changed && BattleManager.Instance != null)
        {
            // エンドフェイズの終了時は ExecuteEndPhase 側で、まだそのターンのうちに確認する
            if (leavingOwnedByLocal && previous != TurnPhase.エンドフェイズ)
                await BattleManager.Instance.ProcessOwnPhaseTiming(SkillTrigger.自分のフェイズ終了時, previous);
            if (enteringOwnedByLocal)
                await BattleManager.Instance.ProcessOwnPhaseTiming(SkillTrigger.自分のフェイズ開始時, next);
        }

        UpdatePhaseUI();
    }

    /// <summary>
    /// エラー復帰など、スキルを発火せずにフェイズ表示だけ戻す。
    /// </summary>
    void SetPhaseWithoutSkills(TurnPhase phase)
    {
        CurrentPhase = phase;
        phaseInitialized = true;
        currentPhaseOwnedByLocal = SimulatesCurrentPhase();
        var glow = FindObjectOfType<PhaseGlowUI>();
        if (glow != null)
            glow.HighlightPhase(phase);
        UpdatePhaseUI();
    }

    bool IsLocalTurn()
    {
        return OnlineSyncManager.Instance == null || OnlineSyncManager.Instance.IsMyTurn;
    }

    /// <summary>
    /// オフラインはプレイヤーとCPUの両方のフェイズをこのクライアントが進める。
    /// </summary>
    bool SimulatesCurrentPhase()
    {
        if (BattleManager.Instance != null && !BattleManager.Instance.IsOnlineMode)
            return true;
        return IsLocalTurn();
    }

    /// <summary>
    /// 画面上のアニメーション、技の対象選択、スキル解決、演出ブロックに合わせて次フェイズボタンの表示だけを切り替える。
    /// クリック時の処理は付け替えない。
    /// </summary>
    public void RefreshNextPhaseButtonVisibility()
    {
        if (nextPhaseButton == null) return;

        if (IsNextPhaseSuppressed())
        {
            nextPhaseButton.gameObject.SetActive(false);
            return;
        }

        if (ShouldShowNextPhaseButton())
        {
            nextPhaseButton.gameObject.SetActive(true);
            nextPhaseButton.interactable = true;
        }
        else
        {
            nextPhaseButton.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 次フェイズボタンを出さない状態。
    /// 画面上の DOTween、技の対象選択、スキルチェーンの解決、演出ブロック。
    /// スキルの対象選択と Yes/No はチェーン解決中に含まれる。
    /// </summary>
    bool IsNextPhaseSuppressed()
    {
        if (DOTween.TotalPlayingTweens() > 0)
            return true;
        if (BattleManager.Instance != null && BattleManager.Instance.IsAwaitingMoveTargetChoice)
            return true;
        if (SkillChainManager.Instance != null && SkillChainManager.Instance.IsResolvingSkills)
            return true;
        if (OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.PresentationBlocking)
            return true;
        return false;
    }

    bool ShouldShowNextPhaseButton()
    {
        bool isActionablePhase = CurrentPhase == TurnPhase.メインフェイズ || CurrentPhase == TurnPhase.バトルフェイズ;
        if (OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.IsOnlineMode)
            return OnlineSyncManager.Instance.IsMyTurn && isActionablePhase;

        bool isPlayerTurn = BattleManager.Instance != null && BattleManager.Instance.IsActiveSideTurn;
        return isPlayerTurn && isActionablePhase;
    }

    /// <summary>
    /// フェーズUIを更新（外部から呼び出し可能）
    /// </summary>
    public void UpdatePhaseUI()
    {
        if (IsNextPhaseSuppressed())
        {
            if (nextPhaseButton != null)
                nextPhaseButton.gameObject.SetActive(false);
            return;
        }

        // オンライン対戦時は自分のターンで、かつ操作可能なフェーズの場合のみボタンを表示
        // オフライン時はプレイヤーターンで操作可能なフェーズの場合のみボタンを表示
        bool shouldShowButton = ShouldShowNextPhaseButton();
        
        if (shouldShowButton)
        {
            EnableNextPhaseButton();
            
            // フェイズに応じてボタンのテキストを更新
            string buttonText = GetNextPhaseButtonText(CurrentPhase);
            SetNextPhaseButtonText(buttonText);
        }
        else
        {
            DisableNextPhaseButton();
        }
    }

    /// <summary>
    /// 現在のフェイズに応じてボタンのテキストを取得
    /// </summary>
    private string GetNextPhaseButtonText(TurnPhase currentPhase)
    {
        switch (currentPhase)
        {
            case TurnPhase.メインフェイズ:
                return "バトルフェイズへ";
            case TurnPhase.バトルフェイズ:
                return "エンドフェイズへ";
            default:
                return "次のフェイズへ";
        }
    }

    /// <summary>
    /// 次のフェイズに進む（新しい設計）
    /// </summary>
    public async Task NextPhase()
    {
        TurnPhase currentPhase = CurrentPhase;
        TurnPhase nextPhase = GetNextPhase(currentPhase);
        
        // フェイズを実行
        await ExecutePhase(nextPhase);
        
        // エンドフェイズの場合は次のターンに進む
        // オンライン対戦時はOnlineSyncManagerが制御するため、オフライン時のみ実行
        if (IsEndPhase(nextPhase) && !OnlineSyncManager.Instance.IsOnlineMode)
        {
            // オフライン時はExecuteOfflinePhases内でNextTurn()が呼ばれるため、
            // ここではNextTurn()を呼び出さない
        }
        else if (IsEndPhase(nextPhase) && OnlineSyncManager.Instance.IsOnlineMode)
        {
            // オンライン時はOnlineSyncManagerが制御
        }
    }
    
    /// <summary>
    /// 指定されたフェイズを実行（統一されたメソッドを使用）
    /// </summary>
    public async Task ExecutePhase(TurnPhase phase)
    {
        // オンライン/オフライン判定
        bool isOnlineMode = OnlineSyncManager.Instance.IsOnlineMode;
        string playerType = isOnlineMode ? "プレイヤー" : "プレイヤー";
        
        // 統一されたExecutePhaseメソッドを使用
        await ExecutePhase(phase, playerType, async () => {
            switch (phase)
            {
                case TurnPhase.ドローフェイズ:
                    await BattleManager.Instance.ExecuteDrawPhase();
                    break;
                case TurnPhase.メインフェイズ:
                    // メインフェイズはプレイヤー操作待ち
                    break;
                case TurnPhase.バトルフェイズ:
                    // バトルフェイズはプレイヤー操作待ち
                    break;
                case TurnPhase.エンドフェイズ:
                    // オンライン/オフライン共通で、常にExecuteEndPhaseを呼ぶ
                    // これにより、自分のターンでも相手のターンでも、両方の画面で1回ずつ処理される
                    await BattleManager.Instance.ExecuteEndPhase();
                    break;
            }
        }, isOnlineMode);
    }
    
    /// <summary>
    /// エンドフェイズかどうかを判定
    /// </summary>
    private bool IsEndPhase(TurnPhase phase)
    {
        return phase == TurnPhase.エンドフェイズ;
    }
}
