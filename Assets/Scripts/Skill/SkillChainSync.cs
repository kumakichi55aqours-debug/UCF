using System;
using System.Threading.Tasks;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// スキルチェーンの通信。進行は MasterClient だけが決め、相手は候補・選択・解決結果を返す。
/// RaiseEvent 50-57（チャンネル 0、確実配送）。
/// 57 は、チェーンに乗った発動への反応候補を相手へ求める。
/// </summary>
public class SkillChainSync : MonoBehaviour, IOnEventCallback
{
    public static SkillChainSync Instance { get; private set; }

    public static class EventCode
    {
        public const byte OpenWindow = 50;
        public const byte Candidates = 51;
        public const byte AskDecision = 52;
        public const byte Decision = 53;
        public const byte Resolve = 54;
        public const byte ResolveComplete = 55;
        public const byte WindowClosed = 56;
        public const byte ResponseWindow = 57;
    }

    public class WindowNotice
    {
        public int WindowId;
        public SkillTrigger Trigger;
        public TurnPhase Phase;
        public bool HasSource;
        public CardKind SourceKind;
        public int SourceId;
        public bool SourceIsMyCard;
        public bool HasTarget;
        public CardKind TargetKind;
        public int TargetId;
        public bool TargetIsMyCard;
        public bool HasDamage;
        public int CalculatedDamage;
        public int AttackerElement;
        public int MoveIndex;
        public bool HasAttacker;
        public CardKind AttackerKind;
        public int AttackerId;
        public bool AttackerIsMyCard;
        public string SourceZone = string.Empty;
        public int SourceIndex = -1;
        public string TargetZone = string.Empty;
        public int TargetIndex = -1;
        public string AttackerZone = string.Empty;
        public int AttackerIndex = -1;
    }

    public class SkillCandidate
    {
        public CardKind CardKind;
        public int CardId;
        public string SkillName;
        public SkillTrigger Trigger;
        public bool Forced;
        public bool IsTurnPlayerCard;
        public string ZoneName = string.Empty;
        public int ZoneIndex = -1;
    }

    private static readonly RaiseEventOptions Others = new RaiseEventOptions
    {
        Receivers = ReceiverGroup.Others
    };

    private TaskCompletionSource<int> damageReceivedTcs;
    private int? pendingFinalDamage;
    private TaskCompletionSource<bool> opponentMoveUsedTcs;
    private bool pendingOpponentMoveUsed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool IsReady =>
        OnlineSyncManager.Instance != null
        && OnlineSyncManager.Instance.IsOnlineMode
        && PhotonNetwork.InRoom;

    private void Raise(byte code, object content)
    {
        if (!IsReady) return;

        bool sent = PhotonNetwork.RaiseEvent(code, content, Others, SendOptions.SendReliable);
        if (!sent)
        {
            Debug.LogError($"[SkillChainSync] RaiseEvent 失敗: code={code}");
        }
    }

    public void SendOpenWindow(WindowNotice notice)
    {
        Debug.Log($"[SkillChainSync] OpenWindow送信: WindowId={notice.WindowId}, Trigger={notice.Trigger}");
        Raise(EventCode.OpenWindow, new object[]
        {
            notice.WindowId,
            (byte)notice.Trigger,
            notice.HasSource,
            (byte)notice.SourceKind,
            notice.SourceId,
            notice.SourceIsMyCard,
            notice.HasTarget,
            (byte)notice.TargetKind,
            notice.TargetId,
            notice.TargetIsMyCard,
            notice.HasDamage,
            notice.CalculatedDamage,
            notice.AttackerElement,
            notice.MoveIndex,
            notice.HasAttacker,
            (byte)notice.AttackerKind,
            notice.AttackerId,
            notice.AttackerIsMyCard,
            (int)notice.Phase,
            notice.SourceZone ?? string.Empty,
            notice.SourceIndex,
            notice.TargetZone ?? string.Empty,
            notice.TargetIndex,
            notice.AttackerZone ?? string.Empty,
            notice.AttackerIndex
        });
    }

    public void SendCandidates(int windowId, SkillCandidate[] candidates)
    {
        candidates = candidates ?? new SkillCandidate[0];
        Debug.Log($"[SkillChainSync] Candidates送信: WindowId={windowId}, Count={candidates.Length}");
        var packed = new object[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            packed[i] = new object[]
            {
                (byte)candidate.CardKind,
                candidate.CardId,
                candidate.SkillName ?? string.Empty,
                (byte)candidate.Trigger,
                candidate.Forced,
                candidate.IsTurnPlayerCard,
                candidate.ZoneName ?? string.Empty,
                candidate.ZoneIndex
            };
        }
        Raise(EventCode.Candidates, new object[] { windowId, packed });
    }

    /// <summary>
    /// チェーンに乗ったカードの効果へ、反応スキルの候補を相手へ求める。
    /// ゾーンは送信者から見た名前。
    /// </summary>
    public void SendResponseWindow(int windowId, CardKind cardKind, int cardId, string zoneName, int zoneIndex)
    {
        Debug.Log($"[SkillChainSync] ResponseWindow送信: WindowId={windowId}, Card={cardKind}:{cardId}");
        Raise(EventCode.ResponseWindow, new object[]
        {
            windowId,
            (byte)cardKind,
            cardId,
            zoneName ?? string.Empty,
            zoneIndex
        });
    }

    public void SendAskDecision(int windowId, int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger)
    {
        Debug.Log($"[SkillChainSync] AskDecision送信: WindowId={windowId}, ChainId={chainId}, Skill={skillName}");
        Raise(EventCode.AskDecision, new object[]
        {
            windowId,
            chainId,
            (byte)cardKind,
            cardId,
            skillName ?? string.Empty,
            (byte)trigger,
            zoneName ?? string.Empty,
            zoneIndex
        });
    }

    public void SendDecision(int windowId, int chainId, bool willActivate)
    {
        Debug.Log($"[SkillChainSync] Decision送信: WindowId={windowId}, ChainId={chainId}, WillActivate={willActivate}");
        Raise(EventCode.Decision, new object[] { windowId, chainId, willActivate });
    }

    public void SendResolve(int windowId, int chainId, CardKind cardKind, int cardId, string zoneName, int zoneIndex, string skillName, SkillTrigger trigger, int priority, int chainNumber, int currentDamage, bool effectNegated = false)
    {
        Debug.Log($"[SkillChainSync] Resolve送信: WindowId={windowId}, ChainId={chainId}, Skill={skillName}, Damage={currentDamage}, Negated={effectNegated}");
        Raise(EventCode.Resolve, new object[]
        {
            windowId,
            chainId,
            (byte)cardKind,
            cardId,
            skillName ?? string.Empty,
            (byte)trigger,
            priority,
            chainNumber,
            currentDamage,
            zoneName ?? string.Empty,
            zoneIndex,
            effectNegated
        });
    }

    public void SendResolveComplete(int windowId, int chainId, bool hasDamage, int finalDamage, CardController redirectedTarget = null, bool negatedPrevious = false)
    {
        Debug.Log($"[SkillChainSync] ResolveComplete送信: WindowId={windowId}, ChainId={chainId}, HasDamage={hasDamage}, Damage={finalDamage}, NegatedPrevious={negatedPrevious}");
        var payload = WithRedirect(new object[] { windowId, chainId, hasDamage, finalDamage }, redirectedTarget);
        var content = new object[payload.Length + 1];
        Array.Copy(payload, content, payload.Length);
        content[payload.Length] = negatedPrevious;
        Raise(EventCode.ResolveComplete, content);
    }

    public void SendWindowClosed(int windowId, SkillTrigger trigger, bool hasDamage, int finalDamage, CardController redirectedTarget = null)
    {
        Debug.Log($"[SkillChainSync] WindowClosed送信: WindowId={windowId}, Trigger={trigger}, HasDamage={hasDamage}, Damage={finalDamage}");
        Raise(EventCode.WindowClosed, WithRedirect(new object[] { windowId, (byte)trigger, hasDamage, finalDamage }, redirectedTarget));
    }

    static object[] WithRedirect(object[] head, CardController card)
    {
        bool has = false;
        byte kind = 0;
        int id = 0;
        string zone = string.Empty;
        int index = -1;
        if (card != null && CardAddress.TryFrom(card, out CardAddress address))
        {
            has = true;
            kind = (byte)address.Kind;
            id = address.Id;
            zone = address.ZoneName ?? string.Empty;
            index = address.Index;
        }

        var result = new object[head.Length + 5];
        System.Array.Copy(head, result, head.Length);
        result[head.Length] = has;
        result[head.Length + 1] = kind;
        result[head.Length + 2] = id;
        result[head.Length + 3] = zone;
        result[head.Length + 4] = index;
        return result;
    }

    public static CardController ReadRedirect(object[] data, int start)
    {
        if (data == null || data.Length < start + 5 || !Convert.ToBoolean(data[start]))
            return null;

        var kind = (CardKind)Convert.ToByte(data[start + 1]);
        int id = Convert.ToInt32(data[start + 2]);
        string zone = data[start + 3] as string ?? string.Empty;
        int index = Convert.ToInt32(data[start + 4]);
        return CardAddress.Resolve(kind.ToString(), id, zone, index);
    }

    public void ArmDamageWait(int finalDamage)
    {
        if (damageReceivedTcs != null && !damageReceivedTcs.Task.IsCompleted)
        {
            damageReceivedTcs.TrySetResult(finalDamage);
        }
        else
        {
            pendingFinalDamage = finalDamage;
        }
    }

    public void ArmMoveWait()
    {
        if (opponentMoveUsedTcs != null && !opponentMoveUsedTcs.Task.IsCompleted)
        {
            opponentMoveUsedTcs.TrySetResult(true);
        }
        else
        {
            pendingOpponentMoveUsed = true;
        }
    }

    public async Task<int> WaitForDamageReceivedSkillComplete()
    {
        if (pendingFinalDamage.HasValue)
        {
            int cached = pendingFinalDamage.Value;
            pendingFinalDamage = null;
            return cached;
        }

        damageReceivedTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int result = await damageReceivedTcs.Task;
        damageReceivedTcs = null;
        return result;
    }

    public async Task WaitForOpponentMoveUsedSkillComplete()
    {
        if (pendingOpponentMoveUsed)
        {
            pendingOpponentMoveUsed = false;
            return;
        }

        opponentMoveUsedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await opponentMoveUsedTcs.Task;
        opponentMoveUsedTcs = null;
    }

    public void OnEvent(EventData photonEvent)
    {
        var manager = SkillChainManager.Instance;
        if (manager == null) return;

        switch (photonEvent.Code)
        {
            case EventCode.OpenWindow:
                HandleOpenWindow(manager, photonEvent.CustomData);
                break;
            case EventCode.Candidates:
                HandleCandidates(manager, photonEvent.CustomData);
                break;
            case EventCode.AskDecision:
                HandleAskDecision(manager, photonEvent.CustomData);
                break;
            case EventCode.Decision:
                HandleDecision(manager, photonEvent.CustomData);
                break;
            case EventCode.Resolve:
                HandleResolve(manager, photonEvent.CustomData);
                break;
            case EventCode.ResolveComplete:
                HandleResolveComplete(manager, photonEvent.CustomData);
                break;
            case EventCode.WindowClosed:
                HandleWindowClosed(manager, photonEvent.CustomData);
                break;
            case EventCode.ResponseWindow:
                HandleResponseWindow(manager, photonEvent.CustomData);
                break;
        }
    }

    private static void HandleResponseWindow(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 5)
        {
            Debug.LogWarning("[SkillChainSync] ResponseWindowのペイロードが不正です");
            return;
        }

        manager.OnResponseWindow(
            Convert.ToInt32(data[0]),
            (CardKind)Convert.ToByte(data[1]),
            Convert.ToInt32(data[2]),
            data[3] as string ?? string.Empty,
            Convert.ToInt32(data[4]));
    }

    private static void HandleOpenWindow(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 18)
        {
            Debug.LogWarning("[SkillChainSync] OpenWindowのペイロードが不正です");
            return;
        }

        var notice = new WindowNotice
        {
            WindowId = Convert.ToInt32(data[0]),
            Trigger = (SkillTrigger)Convert.ToByte(data[1]),
            HasSource = Convert.ToBoolean(data[2]),
            SourceKind = (CardKind)Convert.ToByte(data[3]),
            SourceId = Convert.ToInt32(data[4]),
            SourceIsMyCard = Convert.ToBoolean(data[5]),
            HasTarget = Convert.ToBoolean(data[6]),
            TargetKind = (CardKind)Convert.ToByte(data[7]),
            TargetId = Convert.ToInt32(data[8]),
            TargetIsMyCard = Convert.ToBoolean(data[9]),
            HasDamage = Convert.ToBoolean(data[10]),
            CalculatedDamage = Convert.ToInt32(data[11]),
            AttackerElement = Convert.ToInt32(data[12]),
            MoveIndex = Convert.ToInt32(data[13]),
            HasAttacker = Convert.ToBoolean(data[14]),
            AttackerKind = (CardKind)Convert.ToByte(data[15]),
            AttackerId = Convert.ToInt32(data[16]),
            AttackerIsMyCard = Convert.ToBoolean(data[17]),
            Phase = data.Length > 18 ? (TurnPhase)Convert.ToInt32(data[18]) : TurnPhase.ドローフェイズ,
            SourceZone = data.Length > 19 ? data[19] as string ?? string.Empty : string.Empty,
            SourceIndex = data.Length > 20 ? Convert.ToInt32(data[20]) : -1,
            TargetZone = data.Length > 21 ? data[21] as string ?? string.Empty : string.Empty,
            TargetIndex = data.Length > 22 ? Convert.ToInt32(data[22]) : -1,
            AttackerZone = data.Length > 23 ? data[23] as string ?? string.Empty : string.Empty,
            AttackerIndex = data.Length > 24 ? Convert.ToInt32(data[24]) : -1
        };
        manager.OnOpenWindow(notice);
    }

    private static void HandleCandidates(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 2)
        {
            Debug.LogWarning("[SkillChainSync] Candidatesのペイロードが不正です");
            return;
        }

        int windowId = Convert.ToInt32(data[0]);
        var packed = data[1] as object[] ?? new object[0];
        var candidates = new SkillCandidate[packed.Length];
        for (int i = 0; i < packed.Length; i++)
        {
            if (!(packed[i] is object[] item) || item.Length < 8)
            {
                candidates[i] = new SkillCandidate();
                continue;
            }

            candidates[i] = new SkillCandidate
            {
                CardKind = (CardKind)Convert.ToByte(item[0]),
                CardId = Convert.ToInt32(item[1]),
                SkillName = item[2] as string ?? string.Empty,
                Trigger = (SkillTrigger)Convert.ToByte(item[3]),
                Forced = Convert.ToBoolean(item[4]),
                IsTurnPlayerCard = Convert.ToBoolean(item[5]),
                ZoneName = item[6] as string ?? string.Empty,
                ZoneIndex = Convert.ToInt32(item[7])
            };
        }

        manager.OnCandidates(windowId, candidates);
    }

    private static void HandleAskDecision(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 8)
        {
            Debug.LogWarning("[SkillChainSync] AskDecisionのペイロードが不正です");
            return;
        }

        manager.OnAskDecision(
            Convert.ToInt32(data[0]),
            Convert.ToInt32(data[1]),
            (CardKind)Convert.ToByte(data[2]),
            Convert.ToInt32(data[3]),
            data[6] as string ?? string.Empty,
            Convert.ToInt32(data[7]),
            data[4] as string ?? string.Empty,
            (SkillTrigger)Convert.ToByte(data[5]));
    }

    private static void HandleDecision(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 3)
        {
            Debug.LogWarning("[SkillChainSync] Decisionのペイロードが不正です");
            return;
        }

        manager.OnDecision(Convert.ToInt32(data[0]), Convert.ToInt32(data[1]), Convert.ToBoolean(data[2]));
    }

    private static void HandleResolve(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 11)
        {
            Debug.LogWarning("[SkillChainSync] Resolveのペイロードが不正です");
            return;
        }

        manager.OnResolve(
            Convert.ToInt32(data[0]),
            Convert.ToInt32(data[1]),
            (CardKind)Convert.ToByte(data[2]),
            Convert.ToInt32(data[3]),
            data[9] as string ?? string.Empty,
            Convert.ToInt32(data[10]),
            data[4] as string ?? string.Empty,
            (SkillTrigger)Convert.ToByte(data[5]),
            Convert.ToInt32(data[6]),
            Convert.ToInt32(data[7]),
            Convert.ToInt32(data[8]),
            data.Length > 11 && Convert.ToBoolean(data[11]));
    }

    private static void HandleResolveComplete(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 4)
        {
            Debug.LogWarning("[SkillChainSync] ResolveCompleteのペイロードが不正です");
            return;
        }

        manager.OnResolveComplete(
            Convert.ToInt32(data[0]),
            Convert.ToInt32(data[1]),
            Convert.ToBoolean(data[2]),
            Convert.ToInt32(data[3]),
            ReadRedirect(data, 4),
            data.Length > 9 && Convert.ToBoolean(data[9]));
    }

    private static void HandleWindowClosed(SkillChainManager manager, object customData)
    {
        if (!(customData is object[] data) || data.Length < 4)
        {
            Debug.LogWarning("[SkillChainSync] WindowClosedのペイロードが不正です");
            return;
        }

        manager.OnWindowClosed(
            Convert.ToInt32(data[0]),
            (SkillTrigger)Convert.ToByte(data[1]),
            Convert.ToBoolean(data[2]),
            Convert.ToInt32(data[3]),
            ReadRedirect(data, 4));
    }
}
