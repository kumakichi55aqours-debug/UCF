using System.Threading.Tasks;
using DG.Tweening;
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

    int playingTweenCount;
    const int PhaseTransitionDelayMs = 150;
    const int TurnTransitionDelayMs = 1000;

    public bool IsLeavingBattle { get; private set; }

    public void StopBattleProcessing()
    {
        IsLeavingBattle = true;
    }

    // フェイズ画像のクリックで指定された行き先。届くまで1フェイズずつ進める。
    TurnPhase? requestedPhase;
    // 自分の手番で、フェイズ進行の操作を待っている間だけ入る
    TaskCompletionSource<bool> phaseInputWaiter;
    

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        // 行き先になれるのはバトルとエンドだけ。ドローとメインは戻る方向にしかならない。
        RegisterPhaseImage(battleImage, TurnPhase.バトルフェイズ);
        RegisterPhaseImage(endImage, TurnPhase.エンドフェイズ);
    }

    void Update()
    {
        int playing = DOTween.TotalPlayingTweens();
        if (playing != playingTweenCount)
        {
            bool started = playingTweenCount == 0 && playing > 0;
            playingTweenCount = playing;
            if (started)
                CardContextMenu.DismissTransientWindows();
        }

        TryAdvanceTowardRequestedPhase();
    }

    void RegisterPhaseImage(Image image, TurnPhase phase)
    {
        if (image == null)
            return;

        var click = image.GetComponent<PhaseImageClick>();
        if (click == null)
            click = image.gameObject.AddComponent<PhaseImageClick>();
        click.Phase = phase;
    }

    /// <summary>
    /// フェイズ画像のクリック。自分のメインかバトルの間だけ、今より後ろのフェイズを受け付ける。
    /// 間のフェイズも順に通すので、開始時・終了時のスキルは飛ばさない。
    /// </summary>
    public void RequestPhase(TurnPhase target)
    {
        if (!CanOperatePhase())
            return;
        if (target <= CurrentPhase || IsPhaseInputSuppressed())
            return;

        CardContextMenu.DismissTransientWindows();
        requestedPhase = target;
        TryAdvanceTowardRequestedPhase();
    }

    void TryAdvanceTowardRequestedPhase()
    {
        if (!requestedPhase.HasValue)
            return;

        if (!CanOperatePhase() || CurrentPhase >= requestedPhase.Value)
        {
            requestedPhase = null;
            return;
        }

        if (IsPhaseInputSuppressed())
            return;

        if (phaseInputWaiter == null)
            return;

        var waiter = phaseInputWaiter;
        phaseInputWaiter = null;
        waiter.TrySetResult(true);
    }

    bool CanOperatePhase()
    {
        if (!phaseInitialized || !IsLocalTurn())
            return false;
        return CurrentPhase == TurnPhase.メインフェイズ || CurrentPhase == TurnPhase.バトルフェイズ;
    }

    /// <summary>
    /// ターン開始処理
    /// </summary>
    public void StartTurn()
    {
        // オンラインはホストが、先攻側に合わせて手番を始める。クライアントはホストの通知を待つ。
        if (OnlineSyncManager.Instance.IsOnlineMode)
        {
            if (OnlineSyncManager.Instance.IsHostPlayer)
                OnlineSyncManager.Instance.BeginActiveSideTurn().Forget("開幕のターン開始");
        }
        else
        {
            ExecuteOfflinePhases().Forget("オフラインのターン進行");
        }
    }

    /// <summary>
    /// オフライン時のフェーズ進行処理
    /// </summary>
    public async Task ExecuteOfflinePhases()
    {
        if (IsLeavingBattle) return;

        // オフライン時は先攻側が奇数ターン
        bool isPlayerTurn = BattleManager.Instance.IsActiveSideTurn;
        string currentPlayer = isPlayerTurn ? "プレイヤー" : "エネミー";

        // ドローフェイズ（自動実行）
        await ExecutePhase(TurnPhase.ドローフェイズ, currentPlayer, async () => await BattleManager.Instance.ExecuteDrawPhase(), false);

        // メインフェイズ・バトルフェイズ
        if (isPlayerTurn)
        {
            // プレイヤーのメインフェイズ：フェイズ画像のクリックで進行
            await ExecutePhase(TurnPhase.メインフェイズ, currentPlayer, async () => await WaitForPlayerAction(), false);

            // プレイヤーのバトルフェイズ：フェイズ画像のクリックで進行
            await ExecutePhase(TurnPhase.バトルフェイズ, currentPlayer, async () => await WaitForPlayerAction(), false);

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
        if (!isOnlineMode && IsLeavingBattle) return;

        await ApplyPhaseAsync(phase);

        if (!isOnlineMode && IsLeavingBattle) return;

        await phaseAction();
    }

    /// <summary>
    /// オフラインのプレイヤー手番で、フェイズ画像のクリックを待ち、1フェイズ進める。
    /// </summary>
    private async Task WaitForPlayerAction()
    {
        await WaitForPhaseInput();
        await AdvanceOfflinePhase();
    }

    /// <summary>
    /// フェイズ画像のクリックで、次のフェイズへ進む指示が出るまで待つ。
    /// </summary>
    public Task WaitForPhaseInput()
    {
        var waiter = new TaskCompletionSource<bool>();
        phaseInputWaiter = waiter;
        return waiter.Task;
    }

    async Task AdvanceOfflinePhase()
    {
        TurnPhase nextPhase = GetNextPhase(CurrentPhase);
        await ApplyPhaseAsync(nextPhase);

        if (IsEndPhase(nextPhase))
            await BattleManager.Instance.ExecuteEndPhase();
    }

    // オンラインのターン進行は OnlineSyncManager が行う。エンドからドローへの間は ApplyPhaseAsync が取る。
    private void NextTurn()
    {
        BattleManager.Instance.IncrementTurn();
        ExecuteOfflinePhases().Forget("オフラインのターン進行");
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

    /// <summary>
    /// フェイズを切り替え、自分のフェイズ開始時・終了時のスキル確認を待つ。
    /// 同じフェイズへの再設定ではスキルは走らない。
    /// すでにスキル処理が残っているときは、それが空になってから切り替える。
    /// </summary>
    public async Task ApplyPhaseAsync(TurnPhase next)
    {
        if (SkillChainManager.Instance != null)
            await SkillChainManager.Instance.WaitUntilIdle();

        // 相手から届いたフェイズは待たずに反映し、両画面の切り替えをそろえる
        if (SimulatesCurrentPhase() && (!phaseInitialized || CurrentPhase != next))
        {
            bool turnChange = phaseInitialized && CurrentPhase == TurnPhase.エンドフェイズ && next == TurnPhase.ドローフェイズ;
            await Task.Delay(turnChange ? TurnTransitionDelayMs : PhaseTransitionDelayMs);
        }

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
    /// フェイズを進めない状態。
    /// 画面上の DOTween、技の対象選択、スキルチェーンの解決、演出ブロック。
    /// スキルの対象選択と Yes/No はチェーン解決中に含まれる。
    /// </summary>
    bool IsPhaseInputSuppressed()
    {
        if (DOTween.TotalPlayingTweens() > 0)
            return true;
        if (BattleManager.Instance != null && BattleManager.Instance.IsAwaitingMoveTargetChoice)
            return true;
        if (BattleManager.Instance != null && BattleManager.Instance.IsResolvingMove)
            return true;
        if (SkillChainManager.Instance != null && SkillChainManager.Instance.IsResolvingSkills)
            return true;
        if (OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.PresentationBlocking)
            return true;
        return false;
    }

    /// <summary>
    /// 次のフェイズに進む（新しい設計）
    /// </summary>
    public async Task NextPhase()
    {
        TurnPhase nextPhase = GetNextPhase(CurrentPhase);

        // エンドフェイズの後のターン進行は、オフラインは ExecuteOfflinePhases、オンラインは OnlineSyncManager が行う
        await ExecutePhase(nextPhase);
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
