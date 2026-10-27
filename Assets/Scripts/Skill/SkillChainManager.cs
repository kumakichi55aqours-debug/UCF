using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// スキルチェーンの進行
/// （オフライン）自分のカードだけをその場で解決
/// （オンライン） MasterClient が窓を開き、候補の並び・任意発動の確認・解決順を決定
/// 各クライアントは自分のカードの候補返信、Yes/No、効果実行だけを行う
/// ChainId は Master が発行し、付け替えない
/// チェーン処理中に来たトリガーは、そのチェーンが終わってから1件ずつ新しいチェーンにする
/// </summary>
public class SkillChainManager : MonoBehaviour
{
    public static SkillChainManager Instance { get; private set; }

    public class ChainLink
    {
        // ローカルChainId
        public int ChainId { get; set; }
        // オンラインChainId
        public int NetworkChainId { get; set; }
        public bool IsMyCard { get; set; }
        public CardController SourceCard { get; set; }
        public SkillDB Skill { get; set; }
        public SkillTrigger Trigger { get; set; }
        public int Priority { get; set; }
        public int ChainNumber { get; set; }
        public bool WillActivate { get; set; }
        public bool IsActivationComplete { get; set; }
        // 後ろのリンクが、この効果本体を無効にした
        public bool EffectNegated { get; set; }
        // 発動確認タスク
        public TaskCompletionSource<bool> ActivationTcs { get; set; }
        // 効果処理タスク
        public TaskCompletionSource<bool> ExecutionTcs { get; set; }
        public CardController MoveTarget { get; set; }
        public SkillContext Context { get; set; }
    }

    private class SequencedSkill
    {
        public bool IsLocal;
        public bool Forced;
        public bool IsTurnPlayerCard;
        public SkillInfo Local;
        public SkillChainSync.SkillCandidate Remote;
        // ---ChainLinkに送信する内容---
        public int ChainId;
        public int Priority;
        public int ChainNumber;
        public bool WillActivate;
        // 後ろのリンクが効果本体を無効にした
        public bool Negated;
        //
        public ChainLink Link;
    }

    private struct ResolveResult
    {
        public bool HasDamage;
        public int Damage;
        public CardController RedirectedTarget;
        public bool NegatedPrevious;
    }

    readonly object chainLock = new object();
    readonly Dictionary<int, ChainLink> pendingActivation = new Dictionary<int, ChainLink>();
    readonly Dictionary<int, TaskCompletionSource<bool>> decisionWaits = new Dictionary<int, TaskCompletionSource<bool>>();
    readonly Dictionary<int, TaskCompletionSource<ResolveResult>> resolveWaits = new Dictionary<int, TaskCompletionSource<ResolveResult>>();
    readonly HashSet<string> reservedKeys = new HashSet<string>();
    readonly Dictionary<int, SkillInfo> boundByChainId = new Dictionary<int, SkillInfo>();

    // 他のチェーン実行中か
    bool isProcessingChain;
    // 実行中チェーンの終了後に、保留したトリガーを1件ずつ処理している
    bool isDrainingDeferred;

    /// <summary>
    /// 起動効果・自動スキルを含むチェーンの処理中。
    /// 発動確認、効果本体、その中のアニメーション完了まで待つタスクが終わるまで真。
    /// </summary>
    public bool IsResolvingSkills
    {
        get
        {
            lock (chainLock)
                return isProcessingChain || isDrainingDeferred;
        }
    }
    // スキル処理完了タスク
    Task activeChain = Task.CompletedTask;
    readonly Queue<DeferredChainRequest> deferredChains = new Queue<DeferredChainRequest>();
    readonly List<CardController> chainTactics = new List<CardController>();
    int nextChainId = 1;
    int nextWindowId;
    int activeWindowId;
    // 今解決しているチェーン。無効スキルが直前のリンクへ印を付ける
    List<SequencedSkill> resolvingChain;
    int resolvingIndex = -1;
    // 相手側で無効スキルが解決したとき、完了通知で進行役へ返す
    bool pendingNegatedPrevious;

    // クライアント側の条件分岐
    bool candidatesSent;
    bool openReceived;
    bool closeReceived;

    CollectedSkills participatingCollection;
    SkillChainSync.WindowNotice cachedNotice;
    SkillChainSync.WindowNotice queuedOpen;
    SkillChainSync.SkillCandidate[] pendingRemoteCandidates;
    TaskCompletionSource<SkillChainSync.SkillCandidate[]> candidatesTcs;
    TaskCompletionSource<bool> openWindowTcs;
    TaskCompletionSource<bool> windowClosedTcs;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (GetComponent<SkillChainSync>() == null)
                gameObject.AddComponent<SkillChainSync>();
        }
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// 墓地へ送ったカードの「墓地に送られた時」を、今のチェーンが終わってから両者で解決する。
    /// 処理中なら保留し、処理中でなければその場でチェーンを始める。
    /// </summary>
    public void ScheduleSentToGraveyard(CardController card, CardZoneType fromLocation)
    {
        var trigger = new SkillTriggerEvent(SkillTrigger.墓地に送られた時, card, null, null, fromLocation);
        if (TryEnqueueTrigger(trigger))
            return;

        EventBus.Publish(trigger);
    }

    /// <summary>
    /// チェーン処理中に来たトリガーを、今のチェーンが終わったあとへ回す。
    /// 受け付けたときだけ true。
    /// </summary>
    public bool TryEnqueueTrigger(SkillTriggerEvent triggerEvent)
    {
        if (triggerEvent == null)
            return false;

        lock (chainLock)
        {
            if (!isProcessingChain && !isDrainingDeferred)
                return false;

            deferredChains.Enqueue(new DeferredChainRequest { TriggerEvent = triggerEvent });
            Debug.Log($"[SkillChainManager] トリガーを保留: Trigger={triggerEvent.Trigger}, Source={triggerEvent.SourceCard?.cardName}");
            return true;
        }
    }

    // スキル処理導入
    public async Task ProcessChainForCollectedSkills(CollectedSkills collectedSkills)
    {
        if (collectedSkills == null)
            return;

        Task deferred;
        bool ownsSession = false;
        lock (chainLock)
        {
            // 
            if (isProcessingChain || isDrainingDeferred)
            {
                var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                deferredChains.Enqueue(new DeferredChainRequest { Collected = collectedSkills, Done = waiter });
                deferred = waiter.Task;
            }
            // スキル処理
            else
            {
                deferred = null;
                isDrainingDeferred = true;
                ownsSession = true;
            }
        }

        // ロック外でdefferedが残ってる場合処理を待機
        if (deferred != null)
        {
            await deferred;
            return;
        }

        if (ownsSession)
            NotifySkillResolutionChanged();

        try
        {
            // スキル処理へ移行
            await ExecuteChainWithState(collectedSkills);
            
            // 保留されたスキル処理へ移行
            await DrainDeferredChains();
        }
        finally
        {
            // 最終的に保留イベントの余りないか確認
            ReleaseDeferredGate();
            if (ownsSession)
            {
                PersistentEffectManager.Instance?.RecalculateImmediately();
                NotifySkillResolutionChanged();
            }
        }
    }

    /// <summary>
    /// 効果処理の開始と終了を操作UIへ伝える。
    /// アニメーションはチェーン内で完了を待っているので、ここでは別の完了通知を待たない。
    /// </summary>
    void NotifySkillResolutionChanged()
    {
        CardContextMenu.DismissTransientWindows();
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
    }

    async Task DrainDeferredChains()
    {
        while (true)
        {
            DeferredChainRequest item;
            lock (chainLock)
            {
                if (deferredChains.Count == 0)
                    return;
                item = deferredChains.Dequeue();
            }

            try
            {
                var collected = item.Collected;
                if (collected == null && item.TriggerEvent != null)
                {
                    var triggerEvent = item.TriggerEvent;
                    Debug.Log($"[SkillChainManager] 保留したトリガーを処理: Trigger={triggerEvent.Trigger}, Source={triggerEvent.SourceCard?.cardName}");
                    collected = SkillManager.CollectSkillsForTrigger(
                        triggerEvent.Trigger,
                        triggerEvent.SourceCard,
                        triggerEvent.MoveTarget,
                        triggerEvent.Context,
                        triggerEvent.FromLocation,
                        triggerEvent.Phase,
                        triggerEvent.SpecialSummon);
                }

                if (collected != null)
                    await ExecuteChainWithState(collected);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SkillChainManager] 保留したチェーンの処理でエラー: {ex.Message}");
                item.TriggerEvent?.Context?.ProcessingCompleteTcs?.TrySetResult(true);
            }
            finally
            {
                item.Done?.TrySetResult(true);
            }
        }
    }

    async Task ExecuteChainWithState(CollectedSkills collectedSkills)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (chainLock)
        {
            isProcessingChain = true;
            activeChain = done.Task;
        }

        try
        {
            // 起動効果は宣言した側だけがリンクを持っている。
            // 相手には空の窓参加を頼んで、同じチェーンの候補返信に乗せる。
            if (BattleManager.Instance.IsOnlineMode
                && SkillChainSync.Instance != null
                && SkillChainSync.Instance.IsReady
                && collectedSkills.Trigger == SkillTrigger.起動効果
                && HasAnySkill(collectedSkills))
            {
                OnlineSyncManager.Instance?.SyncJoinIgnitionChain();
            }

            if (BattleManager.Instance.IsOnlineMode
                && SkillChainSync.Instance != null
                && SkillChainSync.Instance.IsReady
                && SkillManager.IsOwnPhaseTrigger(collectedSkills.Trigger)
                && HasAnySkill(collectedSkills))
            {
                OnlineSyncManager.Instance?.SyncJoinPhaseChain(collectedSkills.Trigger, collectedSkills.Phase);
            }

            // （オフライン時）1人で処理するだけ
            if (!BattleManager.Instance.IsOnlineMode)
            {
                await RunOffline(collectedSkills);
            }
            // （オンライン時）通信準備できていない時
            else if (SkillChainSync.Instance == null || !SkillChainSync.Instance.IsReady)
            {
                Debug.LogError("[SkillChainManager] オンラインですがチェーン送信不可");
            }
            // （オンライン時）マスター
            else if (IsMaster())
            {
                await RunAsMaster(collectedSkills);
            }
            // （オンライン時）クライアント
            else
            {
                await RunAsClient(collectedSkills);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SkillChainManager] チェーン処理でエラー: {ex.Message}");
        }
        finally
        {
            ReleaseChainTactics();
            ResetChainState();
            collectedSkills.Context?.ProcessingCompleteTcs?.TrySetResult(true);
            done.TrySetResult(true);
            lock (chainLock)
            {
                if (activeChain == done.Task)
                    activeChain = Task.CompletedTask;
            }
        }
    }

    void ReleaseDeferredGate()
    {
        List<DeferredChainRequest> leftover = null;
        lock (chainLock)
        {
            isDrainingDeferred = false;
            if (deferredChains.Count == 0)
                return;

            leftover = new List<DeferredChainRequest>(deferredChains);
            deferredChains.Clear();
        }

        Debug.LogError($"[SkillChainManager] 保留したトリガーを処理しきれませんでした: {leftover.Count}件");
        foreach (var item in leftover)
        {
            item.TriggerEvent?.Context?.ProcessingCompleteTcs?.TrySetResult(true);
            item.Collected?.Context?.ProcessingCompleteTcs?.TrySetResult(true);
            item.Done?.TrySetResult(true);
        }
    }

    class DeferredChainRequest
    {
        public SkillTriggerEvent TriggerEvent;
        public CollectedSkills Collected;
        public TaskCompletionSource<bool> Done;
    }

    /// <summary>
    /// 相手が宣言した起動効果の窓に、自分の起動効果は足さず参加する。
    /// 直前のチェーンがまだ閉じていなければ、その終了を待ってから入る。
    /// </summary>
    public void JoinIgnitionWindow()
    {
        _ = JoinIgnitionWindowWhenIdle();
    }

    async Task JoinIgnitionWindowWhenIdle()
    {
        const int waitMs = 8000;
        const int stepMs = 100;
        for (int elapsed = 0; elapsed < waitMs; elapsed += stepMs)
        {
            bool busy;
            lock (chainLock)
            {
                busy = isProcessingChain || isDrainingDeferred;
            }

            if (!busy)
                break;

            await Task.Delay(stepMs);
        }

        lock (chainLock)
        {
            if (isProcessingChain || isDrainingDeferred)
            {
                Debug.LogWarning("[SkillChainManager] 起動効果のチェーン窓に参加できませんでした");
                return;
            }
        }

        await ProcessChainForCollectedSkills(SkillManager.CreateEmptyCollected(SkillTrigger.起動効果));
    }

    /// <summary>
    /// 相手のフェイズタイミングの窓に、自分の候補は足さず参加する。
    /// </summary>
    public void JoinPhaseWindow(SkillTrigger trigger, TurnPhase phase)
    {
        _ = JoinPhaseWindowWhenIdle(trigger, phase);
    }

    async Task JoinPhaseWindowWhenIdle(SkillTrigger trigger, TurnPhase phase)
    {
        const int waitMs = 8000;
        const int stepMs = 100;
        for (int elapsed = 0; elapsed < waitMs; elapsed += stepMs)
        {
            bool busy;
            lock (chainLock)
            {
                busy = isProcessingChain || isDrainingDeferred;
            }

            if (!busy)
                break;

            await Task.Delay(stepMs);
        }

        lock (chainLock)
        {
            if (isProcessingChain || isDrainingDeferred)
            {
                Debug.LogWarning("[SkillChainManager] フェイズタイミングのチェーン窓に参加できませんでした");
                return;
            }
        }

        var collected = SkillManager.CreateEmptyCollected(trigger);
        collected.Phase = phase;
        await ProcessChainForCollectedSkills(collected);
    }

    static bool HasAnySkill(CollectedSkills collected)
    {
        if (collected == null)
            return false;

        return Count(collected.TPForcedSkills)
            + Count(collected.TPOptionalSkills)
            + Count(collected.NonTPForcedSkills)
            + Count(collected.NonTPOptionalSkills) > 0;
    }

    static int Count(List<SkillInfo> skills)
    {
        return skills?.Count ?? 0;
    }

    async Task RunOffline(CollectedSkills collected)
    {
        nextChainId = 1;
        var accepted = await ConfirmAndAccept(BuildLocalPool(collected), askRemote: false);
        await AppendActivationResponses(accepted, askRemote: false);
        await ResolveAll(collected, accepted);
        PublishTacticsIfNeeded(collected);
    }

    async Task RunAsMaster(CollectedSkills collected)
    {
        nextChainId = 1;
        activeWindowId = ++nextWindowId;
        candidatesTcs = new TaskCompletionSource<SkillChainSync.SkillCandidate[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRemoteCandidates = null;

        var notice = BuildNotice(activeWindowId, collected);
        SkillChainSync.Instance.SendOpenWindow(notice);

        SkillChainSync.SkillCandidate[] remote;
        if (pendingRemoteCandidates != null)
        {
            remote = pendingRemoteCandidates;
            pendingRemoteCandidates = null;
        }
        else
        {
            remote = await candidatesTcs.Task;
        }

        var pool = BuildLocalPool(collected);
        if (remote != null)
        {
            foreach (var candidate in remote)
            {
                if (candidate == null) continue;
                pool.Add(new SequencedSkill
                {
                    IsLocal = false,
                    Forced = candidate.Forced,
                    IsTurnPlayerCard = candidate.IsTurnPlayerCard,
                    Remote = candidate
                });
            }
        }

        Debug.Log($"[SkillChainManager] 候補受信: WindowId={activeWindowId}, Local={pool.Count(s => s.IsLocal)}, Remote={pool.Count(s => !s.IsLocal)}");
        var accepted = await ConfirmAndAccept(pool, askRemote: true);
        await AppendActivationResponses(accepted, askRemote: true);
        await ResolveAll(collected, accepted);

        bool trackDamage = collected.Trigger == SkillTrigger.技を受ける時;
        int damage = trackDamage
            ? (collected.Context?.FinalDamage ?? collected.Context?.CalculatedDamage ?? 0)
            : 0;
        SkillChainSync.Instance.SendWindowClosed(activeWindowId, collected.Trigger, trackDamage, damage, collected.Context?.移した攻撃対象);
        ArmLocalWaiters(collected, trackDamage, damage);
        PublishTacticsIfNeeded(collected);
    }

    async Task RunAsClient(CollectedSkills collected)
    {
        participatingCollection = collected;

        if (!openReceived)
        {
            openWindowTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await openWindowTcs.Task;
        }

        SendMyCandidates(activeWindowId, collected);
        if (closeReceived)
        {
            PublishTacticsIfNeeded(collected);
            return;
        }

        windowClosedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (closeReceived)
        {
            windowClosedTcs.TrySetResult(true);
        }

        await windowClosedTcs.Task;
        PublishTacticsIfNeeded(collected);
    }

    async Task<List<SequencedSkill>> ConfirmAndAccept(List<SequencedSkill> pool, bool askRemote)
    {
        var accepted = new List<SequencedSkill>();
        await ConfirmGroup(pool, true, true, accepted, askRemote);
        await ConfirmGroup(pool, false, true, accepted, askRemote);
        await ConfirmGroup(pool, true, false, accepted, askRemote);
        await ConfirmGroup(pool, false, false, accepted, askRemote);

        var ordered = accepted
            .Where(s => s.WillActivate)
            .OrderBy(s => s.Priority)
            .ThenBy(s => s.ChainId)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].ChainNumber = i + 1;
        }

        Debug.Log($"[SkillChainManager] チェーン確定: {ordered.Count}件");
        return ordered;
    }

    /// <summary>
    /// チェーンに乗った発動の直後へ、反応スキルを同じチェーンへ足す。
    /// 足した反応にも、さらに反応を足す。解決は従来どおり後ろから。
    /// </summary>
    async Task AppendActivationResponses(List<SequencedSkill> chain, bool askRemote)
    {
        if (chain == null)
            return;

        int index = 0;
        while (index < chain.Count)
        {
            var link = chain[index];
            if (!CanRespondTo(link))
            {
                index++;
                continue;
            }

            var pool = await BuildResponsePool(link, chain, askRemote);
            if (pool.Count == 0)
            {
                index++;
                continue;
            }

            var accepted = await ConfirmAndAccept(pool, askRemote);
            if (accepted.Count == 0)
            {
                index++;
                continue;
            }

            int insertAt = index + 1;
            for (int i = 0; i < accepted.Count; i++)
                chain.Insert(insertAt + i, accepted[i]);

            Renumber(chain);
            index++;
        }
    }

    async Task<List<SequencedSkill>> BuildResponsePool(SequencedSkill link, List<SequencedSkill> chain, bool askRemote)
    {
        var pool = new List<SequencedSkill>();
        var activating = ActivatingCard(link);
        if (activating != null)
        {
            var collected = SkillManager.CollectActivationResponses(activating, ExcludedLocalSkills(chain));
            pool = BuildLocalPool(collected);
        }

        if (!askRemote)
            return pool;

        var remote = await RequestRemoteResponses(link);
        if (remote == null)
            return pool;

        foreach (var candidate in remote)
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.SkillName))
                continue;
            if (candidate.Trigger != SkillTrigger.他効果誘発)
                continue;
            if (ChainContainsRemote(chain, candidate) || PoolContainsRemote(pool, candidate))
                continue;

            pool.Add(new SequencedSkill
            {
                IsLocal = false,
                Forced = candidate.Forced,
                IsTurnPlayerCard = candidate.IsTurnPlayerCard,
                Remote = candidate
            });
        }

        return pool;
    }

    async Task<SkillChainSync.SkillCandidate[]> RequestRemoteResponses(SequencedSkill link)
    {
        var empty = new SkillChainSync.SkillCandidate[0];
        var activating = ActivatingCard(link);
        if (activating == null || SkillChainSync.Instance == null || !SkillChainSync.Instance.IsReady)
            return empty;
        if (!CardAddress.TryFrom(activating, out CardAddress address))
            return empty;

        candidatesTcs = new TaskCompletionSource<SkillChainSync.SkillCandidate[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRemoteCandidates = null;
        SkillChainSync.Instance.SendResponseWindow(
            activeWindowId,
            address.Kind,
            address.Id,
            address.ZoneName,
            address.Index);

        if (candidatesTcs.Task.IsCompleted)
            return candidatesTcs.Task.Result ?? empty;
        if (pendingRemoteCandidates != null)
        {
            var pending = pendingRemoteCandidates;
            pendingRemoteCandidates = null;
            return pending;
        }

        var remote = await candidatesTcs.Task;
        return remote ?? empty;
    }

    public void OnResponseWindow(int windowId, CardKind cardKind, int cardId, string zoneName, int zoneIndex)
    {
        if (IsMaster() || windowId == 0 || windowId != activeWindowId)
            return;

        var activating = CardAddress.Resolve(cardKind.ToString(), cardId, zoneName, zoneIndex);
        var collected = activating == null
            ? SkillManager.CreateEmptyCollected(SkillTrigger.他効果誘発)
            : SkillManager.CollectActivationResponses(activating, ReservedSkillKeys());
        MergeResponseSkills(collected);
        SkillChainSync.Instance?.SendCandidates(windowId, BuildCandidates(collected));
    }

    HashSet<(int instanceId, string skillName)> ReservedSkillKeys()
    {
        var set = new HashSet<(int instanceId, string skillName)>();
        foreach (var info in Enumerate(participatingCollection))
        {
            if (info?.Card == null || info.Skill == null)
                continue;
            if (!reservedKeys.Contains(Key(info.Card, info.Skill.スキル名)))
                continue;
            set.Add((info.Card.GetInstanceID(), info.Skill.スキル名 ?? string.Empty));
        }

        return set;
    }

    void MergeResponseSkills(CollectedSkills extra)
    {
        if (extra == null)
            return;
        if (participatingCollection == null)
            participatingCollection = SkillManager.CreateEmptyCollected(SkillTrigger.他効果誘発);

        if (participatingCollection.TPForcedSkills == null)
            participatingCollection.TPForcedSkills = new List<SkillInfo>();
        if (participatingCollection.NonTPForcedSkills == null)
            participatingCollection.NonTPForcedSkills = new List<SkillInfo>();
        if (participatingCollection.TPOptionalSkills == null)
            participatingCollection.TPOptionalSkills = new List<SkillInfo>();
        if (participatingCollection.NonTPOptionalSkills == null)
            participatingCollection.NonTPOptionalSkills = new List<SkillInfo>();
        AddMissingSkills(participatingCollection.TPForcedSkills, extra.TPForcedSkills);
        AddMissingSkills(participatingCollection.NonTPForcedSkills, extra.NonTPForcedSkills);
        AddMissingSkills(participatingCollection.TPOptionalSkills, extra.TPOptionalSkills);
        AddMissingSkills(participatingCollection.NonTPOptionalSkills, extra.NonTPOptionalSkills);
    }

    static void AddMissingSkills(List<SkillInfo> destination, List<SkillInfo> source)
    {
        if (destination == null || source == null)
            return;

        foreach (var info in source)
        {
            if (info?.Card == null || info.Skill == null)
                continue;

            bool exists = false;
            foreach (var prior in destination)
            {
                if (prior?.Card == info.Card && prior.Skill == info.Skill)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                destination.Add(info);
        }
    }

    static HashSet<(int instanceId, string skillName)> ExcludedLocalSkills(List<SequencedSkill> chain)
    {
        var set = new HashSet<(int instanceId, string skillName)>();
        if (chain == null)
            return set;

        foreach (var skill in chain)
        {
            if (skill?.IsLocal != true || skill.Local?.Card == null)
                continue;
            set.Add((skill.Local.Card.GetInstanceID(), skill.Local.Skill?.スキル名 ?? string.Empty));
        }

        return set;
    }

    static bool ChainContainsRemote(List<SequencedSkill> chain, SkillChainSync.SkillCandidate candidate)
    {
        if (chain == null || candidate == null)
            return false;

        foreach (var skill in chain)
        {
            if (skill?.Remote != null && SameRemote(skill.Remote, candidate))
                return true;
        }

        return false;
    }

    static bool PoolContainsRemote(List<SequencedSkill> pool, SkillChainSync.SkillCandidate candidate)
    {
        if (pool == null || candidate == null)
            return false;

        foreach (var skill in pool)
        {
            if (skill?.Remote != null && SameRemote(skill.Remote, candidate))
                return true;
        }

        return false;
    }

    static bool SameRemote(SkillChainSync.SkillCandidate a, SkillChainSync.SkillCandidate b)
    {
        return a.CardKind == b.CardKind
            && a.CardId == b.CardId
            && a.SkillName == b.SkillName
            && (a.ZoneName ?? string.Empty) == (b.ZoneName ?? string.Empty)
            && a.ZoneIndex == b.ZoneIndex;
    }

    static CardController ActivatingCard(SequencedSkill skill)
    {
        if (skill == null)
            return null;
        if (skill.IsLocal)
            return skill.Local?.Card;

        var remote = skill.Remote;
        if (remote == null)
            return null;
        return CardAddress.Resolve(remote.CardKind.ToString(), remote.CardId, remote.ZoneName, remote.ZoneIndex);
    }

    static bool CanRespondTo(SequencedSkill skill)
    {
        if (skill == null || !skill.WillActivate)
            return false;

        string skillName;
        if (skill.IsLocal)
        {
            if (skill.Local?.Skill == null)
                return false;
            skillName = skill.Local.Skill.スキル名;
        }
        else
        {
            if (skill.Remote == null)
                return false;
            skillName = skill.Remote.SkillName;
        }

        return SkillManager.IsRespondableActivation(skillName);
    }

    static void Renumber(List<SequencedSkill> chain)
    {
        for (int i = 0; i < chain.Count; i++)
            chain[i].ChainNumber = i + 1;
    }

    async Task ConfirmGroup(List<SequencedSkill> pool, bool turnPlayer, bool forced, List<SequencedSkill> accepted, bool askRemote)
    {
        foreach (var skill in pool)
        {
            if (skill.IsTurnPlayerCard != turnPlayer || skill.Forced != forced) continue;

            // 同名の同じスキルは、この確認の中で既にYesがあれば聞かない。
            // Noのあいだは次の同名を聞き、どの1枚を使うかは選べる。
            if (!forced && HasAcceptedSameName(skill, accepted))
            {
                skill.WillActivate = false;
                Debug.Log($"[SkillChainManager] 同名カードは発動済みのため確認を省略: {Describe(skill)}");
                continue;
            }

            skill.ChainId = nextChainId++;
            skill.Priority = PriorityFor(turnPlayer, forced);
            if (forced)
            {
                if (skill.IsLocal)
                {
                    var card = skill.Local?.Card;
                    if (card != null && !await BattleManager.Instance.TryCommitHandTacticsAppearance(card))
                    {
                        skill.WillActivate = false;
                        continue;
                    }
                }

                skill.WillActivate = true;
                accepted.Add(skill);
                continue;
            }

            bool yes;
            if (skill.IsLocal)
            {
                yes = await AskLocal(skill);
            }
            else if (askRemote)
            {
                yes = await AskRemote(skill);
            }
            else
            {
                yes = false;
            }

            skill.WillActivate = yes;
            if (yes)
            {
                accepted.Add(skill);
            }
        }
    }

    async Task ResolveAll(CollectedSkills collected, List<SequencedSkill> ordered)
    {
        bool trackDamage = collected.Trigger == SkillTrigger.技を受ける時;
        int running = trackDamage
            ? (collected.Context?.CalculatedDamage ?? collected.Context?.FinalDamage ?? 0)
            : 0;

        resolvingChain = ordered;
        try
        {
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                resolvingIndex = i;
                var skill = ordered[i];
                string name = skill.IsLocal ? skill.Local?.Skill?.スキル名 : skill.Remote?.SkillName;
                Debug.Log($"[SkillChainManager] 解決: ChainNumber={skill.ChainNumber}, ChainId={skill.ChainId}, Skill={name}, Local={skill.IsLocal}, Negated={skill.Negated}");

                if (skill.IsLocal)
                {
                    var link = EnsureLocalLink(skill);
                    link.ChainNumber = skill.ChainNumber;
                    link.WillActivate = true;
                    link.EffectNegated = skill.Negated;
                    if (trackDamage && link.Context != null)
                    {
                        link.Context.CalculatedDamage = running;
                        link.Context.FinalDamage = running;
                    }

                    await ExecuteLocal(link);
                    if (trackDamage)
                    {
                        running = link.Context?.FinalDamage ?? running;
                    }
                }
                else
                {
                    int damage = trackDamage ? running : 0;
                    var result = await ExecuteRemote(skill, damage, skill.Negated);
                    if (result.NegatedPrevious && i > 0)
                        ordered[i - 1].Negated = true;
                    if (trackDamage)
                    {
                        if (result.HasDamage)
                            running = result.Damage;
                        if (result.RedirectedTarget != null && collected.Context != null)
                            collected.Context.移した攻撃対象 = result.RedirectedTarget;
                    }
                }
            }
        }
        finally
        {
            resolvingChain = null;
            resolvingIndex = -1;
        }

        if (trackDamage && collected.Context != null)
        {
            collected.Context.FinalDamage = running;
        }
    }

    /// <summary>
    /// 今解決しているリンクの直前を、効果無効にする。
    /// 進行役はチェーンへ印を付け、相手側は解決完了と一緒に進行役へ返す。
    /// </summary>
    public bool NegatePreviousChainLink()
    {
        if (resolvingChain != null)
        {
            if (resolvingIndex <= 0 || resolvingIndex >= resolvingChain.Count)
                return false;

            var previous = resolvingChain[resolvingIndex - 1];
            previous.Negated = true;
            string name = previous.IsLocal ? previous.Local?.Skill?.スキル名 : previous.Remote?.SkillName;
            Debug.Log($"[SkillChainManager] 直前の効果を無効: ChainNumber={previous.ChainNumber}, Skill={name}");
            return true;
        }

        pendingNegatedPrevious = true;
        return true;
    }

    List<SequencedSkill> BuildLocalPool(CollectedSkills collected)
    {
        var pool = new List<SequencedSkill>();
        AddLocal(pool, collected.TPForcedSkills, true, true);
        AddLocal(pool, collected.NonTPForcedSkills, false, true);
        AddLocal(pool, collected.TPOptionalSkills, true, false);
        AddLocal(pool, collected.NonTPOptionalSkills, false, false);
        return pool;
    }

    static void AddLocal(List<SequencedSkill> pool, List<SkillInfo> skills, bool turnPlayer, bool forced)
    {
        if (skills == null) return;
        bool includeCpu = BattleManager.Instance != null && !BattleManager.Instance.IsOnlineMode;
        foreach (var info in skills)
        {
            if (info?.Card == null || info.Skill == null) continue;
            if (!info.Card.isMyCard && !includeCpu) continue;
            pool.Add(new SequencedSkill
            {
                IsLocal = true,
                Forced = forced,
                IsTurnPlayerCard = turnPlayer,
                Local = info
            });
        }
    }

    async Task<bool> AskLocal(SequencedSkill skill)
    {
        var link = EnsureLocalLink(skill);
        link.WillActivate = false;
        link.IsActivationComplete = false;
        return await AskOwner(link);
    }

    async Task<bool> AskOwner(ChainLink link)
    {
        if (link?.SourceCard == null || link.Skill == null)
        {
            return false;
        }

        pendingActivation[link.ChainId] = link;
        EventBus.Publish(new ChainSkillActivationRequestEvent(link));
        await link.ActivationTcs.Task;

        pendingActivation.Remove(link.ChainId);
        return link.WillActivate;
    }

    async Task<bool> AskRemote(SequencedSkill skill)
    {
        var candidate = skill.Remote;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        decisionWaits[skill.ChainId] = tcs;
        SkillChainSync.Instance.SendAskDecision(
            activeWindowId,
            skill.ChainId,
            candidate.CardKind,
            candidate.CardId,
            candidate.ZoneName,
            candidate.ZoneIndex,
            candidate.SkillName,
            candidate.Trigger);

        bool yes = await tcs.Task;
        decisionWaits.Remove(skill.ChainId);
        return yes;
    }

    async Task ExecuteLocal(ChainLink link)
    {
        if (link.ExecutionTcs == null || link.ExecutionTcs.Task.IsCompleted)
        {
            link.ExecutionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        EventBus.Publish(new ChainSkillExecutionEvent(link));
        await link.ExecutionTcs.Task;
    }

    async Task<ResolveResult> ExecuteRemote(SequencedSkill skill, int currentDamage, bool effectNegated)
    {
        var candidate = skill.Remote;
        var tcs = new TaskCompletionSource<ResolveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        resolveWaits[skill.ChainId] = tcs;
        SkillChainSync.Instance.SendResolve(
            activeWindowId,
            skill.ChainId,
            candidate.CardKind,
            candidate.CardId,
            candidate.ZoneName,
            candidate.ZoneIndex,
            candidate.SkillName,
            candidate.Trigger,
            skill.Priority,
            skill.ChainNumber,
            currentDamage,
            effectNegated);

        var result = await tcs.Task;
        resolveWaits.Remove(skill.ChainId);
        if (!result.HasDamage)
            result.Damage = currentDamage;
        return result;
    }

    ChainLink EnsureLocalLink(SequencedSkill skill)
    {
        if (skill.Link != null) return skill.Link;

        var info = skill.Local;
        var card = info.Card;
        skill.Link = new ChainLink
        {
            ChainId = skill.ChainId,
            NetworkChainId = skill.ChainId,
            SourceCard = card,
            Skill = info.Skill,
            Trigger = info.Trigger,
            Priority = skill.Priority,
            ChainNumber = skill.ChainNumber,
            MoveTarget = info.MoveTarget,
            Context = info.Context ?? SkillContext.WithAttackTarget(card, info.MoveTarget),
            IsMyCard = card.isMyCard,
            WillActivate = skill.WillActivate,
            ActivationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            ExecutionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        return skill.Link;
    }

    public void NotifyActivationComplete(int chainId, bool willActivate, bool? isMyCard = null)
    {
        if (!pendingActivation.TryGetValue(chainId, out var link))
        {
            Debug.LogWarning($"[SkillChainManager] 未知のChainId: {chainId}");
            return;
        }

        link.WillActivate = willActivate;
        link.IsActivationComplete = true;
        link.ActivationTcs?.TrySetResult(true);
        Debug.Log($"[SkillChainManager] 発動確認完了: ChainId={chainId}, WillActivate={willActivate}, Card={link.SourceCard?.cardName}");

        if (BattleManager.Instance.IsOnlineMode && !IsMaster() && link.IsMyCard)
        {
            SkillChainSync.Instance?.SendDecision(activeWindowId, chainId, willActivate);
        }
    }

    public void OnOpenWindow(SkillChainSync.WindowNotice notice)
    {
        if (notice == null || IsMaster()) return;

        // 前の窓を閉じる前に次の開始が来たら、今の処理が終わるまで保留する
        if (isProcessingChain && activeWindowId != 0 && notice.WindowId != activeWindowId)
        {
            queuedOpen = notice;
            return;
        }

        ApplyOpen(notice);
    }

    void ApplyOpen(SkillChainSync.WindowNotice notice)
    {
        cachedNotice = notice;
        activeWindowId = notice.WindowId;
        openReceived = true;
        openWindowTcs?.TrySetResult(true);
    }

    public void OnCandidates(int windowId, SkillChainSync.SkillCandidate[] candidates)
    {
        if (!IsMaster() || windowId != activeWindowId) return;

        candidates = candidates ?? new SkillChainSync.SkillCandidate[0];
        if (candidatesTcs != null && !candidatesTcs.Task.IsCompleted)
        {
            candidatesTcs.TrySetResult(candidates);
        }
        else
        {
            pendingRemoteCandidates = candidates;
        }
    }

    public void OnAskDecision(int windowId, int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger)
    {
        if (IsMaster() || windowId != activeWindowId) return;
        _ = AnswerDecision(windowId, chainId, cardKind, cardId, zoneName, zoneIndex, skillName, trigger);
    }

    public void OnDecision(int windowId, int chainId, bool willActivate)
    {
        if (!IsMaster() || windowId != activeWindowId) return;
        if (decisionWaits.TryGetValue(chainId, out var tcs))
        {
            tcs.TrySetResult(willActivate);
        }
    }

    public void OnResolve(int windowId, int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger, int priority, int chainNumber, int currentDamage, bool effectNegated)
    {
        if (IsMaster() || windowId != activeWindowId) return;
        _ = ExecuteOrdered(windowId, chainId, cardKind, cardId, zoneName, zoneIndex, skillName, trigger, priority, chainNumber, currentDamage, effectNegated);
    }

    public void OnResolveComplete(int windowId, int chainId, bool hasDamage, int damage, CardController redirectedTarget = null, bool negatedPrevious = false)
    {
        if (!IsMaster() || windowId != activeWindowId) return;
        if (resolveWaits.TryGetValue(chainId, out var tcs))
        {
            tcs.TrySetResult(new ResolveResult
            {
                HasDamage = hasDamage,
                Damage = damage,
                RedirectedTarget = redirectedTarget,
                NegatedPrevious = negatedPrevious
            });
        }
    }

    public void OnWindowClosed(int windowId, SkillTrigger trigger, bool hasDamage, int finalDamage, CardController redirectedTarget = null)
    {
        if (IsMaster() || windowId != activeWindowId) return;

        closeReceived = true;
        if (hasDamage && participatingCollection?.Context != null)
        {
            participatingCollection.Context.FinalDamage = finalDamage;
        }

        if (redirectedTarget != null && participatingCollection?.Context != null)
            participatingCollection.Context.移した攻撃対象 = redirectedTarget;

        ArmLocalWaiters(participatingCollection, hasDamage, finalDamage);
        windowClosedTcs?.TrySetResult(true);
    }

    async Task AnswerDecision(int windowId, int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger)
    {
        try
        {
            var info = TakeInfo(chainId, cardKind, cardId, zoneName, zoneIndex, skillName, trigger);
            var card = info?.Card ?? FindOwnedCard(cardKind, cardId, zoneName, zoneIndex, skillName);
            var skill = info?.Skill ?? card?.skills?.FirstOrDefault(s => s != null && s.スキル名 == skillName);
            if (card == null || skill == null)
            {
                SkillChainSync.Instance?.SendDecision(windowId, chainId, false);
                return;
            }

            Reserve(card, skillName);
            var link = new ChainLink
            {
                ChainId = chainId,
                NetworkChainId = chainId,
                SourceCard = card,
                Skill = skill,
                Trigger = info?.Trigger ?? trigger,
                MoveTarget = info?.MoveTarget,
                Context = info?.Context ?? SkillContext.WithAttackTarget(card, info?.MoveTarget),
                IsMyCard = true,
                ActivationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
                ExecutionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            await AskOwner(link);
            if (!link.IsActivationComplete)
            {
                SkillChainSync.Instance?.SendDecision(windowId, chainId, link.WillActivate);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SkillChainManager] 発動確認でエラー: {ex.Message}");
            SkillChainSync.Instance?.SendDecision(windowId, chainId, false);
        }
    }

    async Task ExecuteOrdered(int windowId, int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger, int priority, int chainNumber, int currentDamage, bool effectNegated)
    {
        bool hasDamage = false;
        int damage = currentDamage;
        SkillContext context = null;
        pendingNegatedPrevious = false;
        try
        {
            var info = TakeInfo(chainId, cardKind, cardId, zoneName, zoneIndex, skillName, trigger);
            var card = info?.Card ?? FindOwnedCard(cardKind, cardId, zoneName, zoneIndex, skillName);
            var skill = info?.Skill ?? card?.skills?.FirstOrDefault(s => s != null && s.スキル名 == skillName);
            if (card == null || skill == null)
            {
                Debug.LogWarning($"[SkillChainManager] 解決対象のカードが見つかりません: {cardKind} ID:{cardId} Skill={skillName}");
                return;
            }

            Reserve(card, skillName);
            context = info?.Context ?? SkillContext.WithAttackTarget(card, info?.MoveTarget);
            if (trigger == SkillTrigger.技を受ける時)
            {
                context.CalculatedDamage = currentDamage;
                context.FinalDamage = currentDamage;
            }

            var link = new ChainLink
            {
                ChainId = chainId,
                NetworkChainId = chainId,
                SourceCard = card,
                Skill = skill,
                Trigger = info?.Trigger ?? trigger,
                Priority = priority,
                ChainNumber = chainNumber,
                MoveTarget = info?.MoveTarget,
                Context = context,
                IsMyCard = true,
                WillActivate = true,
                EffectNegated = effectNegated,
                ExecutionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            await ExecuteLocal(link);
            if (trigger == SkillTrigger.技を受ける時)
            {
                hasDamage = true;
                damage = context.FinalDamage ?? currentDamage;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SkillChainManager] スキル解決でエラー: {ex.Message}");
        }
        finally
        {
            CardController redirected = trigger == SkillTrigger.技を受ける時 ? context?.移した攻撃対象 : null;
            bool negatedPrevious = pendingNegatedPrevious;
            pendingNegatedPrevious = false;
            SkillChainSync.Instance?.SendResolveComplete(windowId, chainId, hasDamage, damage, redirected, negatedPrevious);
        }
    }

    void SendMyCandidates(int windowId, CollectedSkills collected)
    {
        if (candidatesSent || windowId == 0) return;
        candidatesSent = true;
        SkillChainSync.Instance?.SendCandidates(windowId, BuildCandidates(collected));
    }

    static SkillChainSync.SkillCandidate[] BuildCandidates(CollectedSkills collected)
    {
        var list = new List<SkillChainSync.SkillCandidate>();
        AddCandidate(list, collected?.TPForcedSkills, true, true);
        AddCandidate(list, collected?.NonTPForcedSkills, false, true);
        AddCandidate(list, collected?.TPOptionalSkills, true, false);
        AddCandidate(list, collected?.NonTPOptionalSkills, false, false);
        return list.ToArray();
    }

    static void AddCandidate(List<SkillChainSync.SkillCandidate> list, List<SkillInfo> skills, bool turnPlayer, bool forced)
    {
        if (skills == null) return;
        foreach (var info in skills)
        {
            if (info?.Card == null || info.Skill == null || !info.Card.isMyCard) continue;
            CardAddress.TryFrom(info.Card, out CardAddress address);
            info.SentZoneName = address.ZoneName ?? string.Empty;
            info.SentZoneIndex = address.IsValid ? address.Index : -1;
            list.Add(new SkillChainSync.SkillCandidate
            {
                CardKind = info.Card.cardKind,
                CardId = info.Card.cardID,
                SkillName = info.Skill.スキル名 ?? string.Empty,
                Trigger = info.Trigger,
                Forced = forced,
                IsTurnPlayerCard = turnPlayer,
                ZoneName = info.SentZoneName,
                ZoneIndex = info.SentZoneIndex
            });
        }
    }

    static SkillChainSync.WindowNotice BuildNotice(int windowId, CollectedSkills collected)
    {
        var notice = new SkillChainSync.WindowNotice
        {
            WindowId = windowId,
            Trigger = collected.Trigger,
            Phase = collected.Phase
        };

        if (collected.SourceCard != null)
        {
            notice.HasSource = true;
            notice.SourceKind = collected.SourceCard.cardKind;
            notice.SourceId = collected.SourceCard.cardID;
            notice.SourceIsMyCard = collected.SourceCard.isMyCard;
            if (CardAddress.TryFrom(collected.SourceCard, out CardAddress sourceAddress))
            {
                notice.SourceZone = sourceAddress.ZoneName;
                notice.SourceIndex = sourceAddress.Index;
            }
        }

        if (collected.MoveTarget != null)
        {
            notice.HasTarget = true;
            notice.TargetKind = collected.MoveTarget.cardKind;
            notice.TargetId = collected.MoveTarget.cardID;
            notice.TargetIsMyCard = collected.MoveTarget.isMyCard;
            if (CardAddress.TryFrom(collected.MoveTarget, out CardAddress targetAddress))
            {
                notice.TargetZone = targetAddress.ZoneName;
                notice.TargetIndex = targetAddress.Index;
            }
        }

        var context = collected.Context;
        if (context?.CalculatedDamage != null)
        {
            notice.HasDamage = true;
            notice.CalculatedDamage = context.CalculatedDamage.Value;
            notice.AttackerElement = (int)context.AttackerElement;
            notice.MoveIndex = context.MoveIndex;
        }

        if (context?.Attacker != null)
        {
            notice.HasAttacker = true;
            notice.AttackerKind = context.Attacker.cardKind;
            notice.AttackerId = context.Attacker.cardID;
            notice.AttackerIsMyCard = context.Attacker.isMyCard;
            if (CardAddress.TryFrom(context.Attacker, out CardAddress attackerAddress))
            {
                notice.AttackerZone = attackerAddress.ZoneName;
                notice.AttackerIndex = attackerAddress.Index;
            }
        }

        return notice;
    }

    SkillInfo TakeInfo(int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger)
    {
        if (boundByChainId.TryGetValue(chainId, out var bound))
        {
            return bound;
        }

        foreach (var info in Enumerate(participatingCollection))
        {
            if (info?.Card == null || info.Skill == null) continue;
            if (info.Card.cardKind != cardKind || info.Card.cardID != cardId) continue;
            if ((info.SentZoneName ?? string.Empty) != (zoneName ?? string.Empty) || info.SentZoneIndex != zoneIndex) continue;
            if (info.Skill.スキル名 != skillName || info.Trigger != trigger) continue;
            if (reservedKeys.Contains(Key(info.Card, skillName))) continue;

            boundByChainId[chainId] = info;
            Reserve(info.Card, skillName);
            return info;
        }

        return null;
    }

    void Reserve(CardController card, string skillName)
    {
        if (card == null) return;
        reservedKeys.Add(Key(card, skillName));
    }

    static string Key(CardController card, string skillName)
    {
        return card.GetInstanceID() + "\n" + (skillName ?? string.Empty);
    }

    static IEnumerable<SkillInfo> Enumerate(CollectedSkills collected)
    {
        if (collected == null) yield break;
        foreach (var list in new[]
        {
            collected.TPForcedSkills,
            collected.NonTPForcedSkills,
            collected.TPOptionalSkills,
            collected.NonTPOptionalSkills
        })
        {
            if (list == null) continue;
            foreach (var info in list)
            {
                if (info != null) yield return info;
            }
        }
    }

    CardController FindOwnedCard(CardKind kind, int cardId, string zoneName, int zoneIndex, string skillName)
    {
        CardController card = CardAddress.ResolveLocal(kind.ToString(), cardId, zoneName, zoneIndex);
        if (card == null || !card.isMyCard) return null;
        if (reservedKeys.Contains(Key(card, skillName))) return null;
        if (card.skills == null || !card.skills.Any(s => s != null && s.スキル名 == skillName)) return null;
        return card;
    }

    static void ArmLocalWaiters(CollectedSkills collected, bool hasDamage, int damage)
    {
        var source = collected?.SourceCard;
        if (hasDamage && source != null && !source.isMyCard)
        {
            SkillChainSync.Instance?.ArmDamageWait(damage);
        }

        if (collected?.Trigger == SkillTrigger.相手の技使用時 && source != null && source.isMyCard)
        {
            SkillChainSync.Instance?.ArmMoveWait();
        }
    }

    /// <summary>
    /// チェーン中に手札から出した作戦。このチェーンが終わったら墓地へ送る。
    /// </summary>
    public void RegisterChainTactics(CardController card)
    {
        if (card == null || chainTactics.Contains(card))
            return;
        chainTactics.Add(card);
    }

    void ReleaseChainTactics()
    {
        if (chainTactics.Count == 0)
            return;

        var pending = chainTactics.ToArray();
        chainTactics.Clear();
        foreach (var card in pending)
        {
            if (card == null || card.cardKind != CardKind.作戦)
                continue;
            var battle = BattleManager.Instance;
            if (battle == null)
                continue;
            if (card.transform.parent != battle.pTacticsZone && card.transform.parent != battle.eTacticsZone)
                continue;

            EventBus.Publish(new SkillActivationCompletedEvent(card));
            if (battle.IsOnlineMode && OnlineSyncManager.Instance != null)
                OnlineSyncManager.Instance.SyncTacticsSkillCompleted(card);
        }
    }

    static void PublishTacticsIfNeeded(CollectedSkills collected)
    {
        var source = collected?.SourceCard;
        if (source == null || !source.isMyCard || source.cardKind != CardKind.作戦) return;
        if (collected.Trigger != SkillTrigger.カード使用時) return;

        EventBus.Publish(new SkillActivationCompletedEvent(source));
        if (BattleManager.Instance.IsOnlineMode && OnlineSyncManager.Instance != null)
        {
            OnlineSyncManager.Instance.SyncTacticsSkillCompleted(source);
        }
    }

    static bool HasAcceptedSameName(SequencedSkill skill, List<SequencedSkill> accepted)
    {
        if (!SkipsSameNameConfirmation(TriggerOf(skill)))
            return false;

        foreach (var prior in accepted)
        {
            if (prior == null || !prior.WillActivate || prior.Forced)
                continue;
            if (IsSameNameSkill(prior, skill))
                return true;
        }

        return false;
    }

    static bool SkipsSameNameConfirmation(SkillTrigger trigger)
    {
        return trigger == SkillTrigger.相手の技使用時
            || trigger == SkillTrigger.他カードが墓地に送られた時
            || trigger == SkillTrigger.他バトラー撃破時
            || trigger == SkillTrigger.他効果誘発;
    }

    static bool IsSameNameSkill(SequencedSkill a, SequencedSkill b)
    {
        if (a == null || b == null || a.IsLocal != b.IsLocal)
            return false;
        if (TriggerOf(a) != TriggerOf(b))
            return false;

        if (a.IsLocal)
        {
            var cardA = a.Local?.Card;
            var cardB = b.Local?.Card;
            var skillA = a.Local?.Skill;
            var skillB = b.Local?.Skill;
            if (cardA == null || cardB == null || skillA == null || skillB == null)
                return false;
            if (cardA.isMyCard != cardB.isMyCard)
                return false;
            if (cardA.cardKind != cardB.cardKind || cardA.cardID != cardB.cardID)
                return false;
            return SkillUseCountStore.IsSameSkill(skillA, skillB);
        }

        var remoteA = a.Remote;
        var remoteB = b.Remote;
        if (remoteA == null || remoteB == null)
            return false;
        return remoteA.CardKind == remoteB.CardKind
            && remoteA.CardId == remoteB.CardId
            && remoteA.SkillName == remoteB.SkillName;
    }

    static SkillTrigger TriggerOf(SequencedSkill skill)
    {
        if (skill == null)
            return default;
        return skill.IsLocal
            ? skill.Local?.Trigger ?? default
            : skill.Remote?.Trigger ?? default;
    }

    static string Describe(SequencedSkill skill)
    {
        if (skill?.IsLocal == true)
            return $"{skill.Local?.Card?.cardName}/{skill.Local?.Skill?.スキル名}";
        return $"{skill?.Remote?.CardKind}:{skill?.Remote?.CardId}/{skill?.Remote?.SkillName}";
    }

    static int PriorityFor(bool isTurnPlayerCard, bool forced)
    {
        if (isTurnPlayerCard && forced) return 1;
        if (!isTurnPlayerCard && forced) return 2;
        if (isTurnPlayerCard) return 3;
        return 4;
    }

    static bool IsMaster()
    {
        return OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.IsHostPlayer;
    }

    void ResetChainState()
    {
        var nextOpen = queuedOpen;
        queuedOpen = null;

        isProcessingChain = false;
        candidatesSent = false;
        openReceived = false;
        closeReceived = false;
        activeWindowId = 0;
        nextChainId = 1;
        resolvingChain = null;
        resolvingIndex = -1;
        pendingNegatedPrevious = false;
        participatingCollection = null;
        cachedNotice = null;
        pendingRemoteCandidates = null;
        candidatesTcs = null;
        openWindowTcs = null;
        windowClosedTcs = null;
        pendingActivation.Clear();
        decisionWaits.Clear();
        resolveWaits.Clear();
        reservedKeys.Clear();
        boundByChainId.Clear();

        if (nextOpen != null)
            ApplyOpen(nextOpen);
    }
}

public class ChainSkillActivationRequestEvent
{
    public SkillChainManager.ChainLink Link { get; }
    public ChainSkillActivationRequestEvent(SkillChainManager.ChainLink link) => Link = link;
}

public class ChainSkillExecutionEvent
{
    public SkillChainManager.ChainLink Link { get; }
    public ChainSkillExecutionEvent(SkillChainManager.ChainLink link) => Link = link;
}

public class ChainSkillExecutionCompleteEvent
{
    public int ChainId { get; }
    public bool IsMyCard { get; }
    public ChainSkillExecutionCompleteEvent(int chainId, bool isMyCard)
    {
        ChainId = chainId;
        IsMyCard = isMyCard;
    }
}
