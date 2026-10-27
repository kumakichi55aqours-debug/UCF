using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading.Tasks;

/// <summary>
/// 効果の対象選択。技のクリック選択は BattleManager が持つ。
/// </summary>
public class TargetSelectionManager : MonoBehaviour
{
    public static TargetSelectionManager Instance { get; private set; }

    public TargetList CurrentSkillTarget { get; set; }
    public PlayerType CurrentSkillTargetSide { get; set; }
    public bool IsSkillTargetSelectionMode { get; set; }
    public SearchCriteria CurrentSkillTargetCriteria { get; set; }
    public CardController CurrentSkillTargetSource { get; set; }

    private TaskCompletionSource<CardController> targetSelectionTcs;
    private TaskCompletionSource<List<CardController>> multiTargetTcs;
    private readonly List<CardController> chosenTargets = new List<CardController>();
    private HashSet<CardController> limitedCandidates;
    private int requiredSelectCount = 1;
    private int minimumSelectCount = 1;
    private TargetCountMode selectCountMode = TargetCountMode.ちょうど;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        ResetTargetSelectionState();
    }

    private void OnEnable()
    {
        ResetTargetSelectionState();
    }

    private void ResetTargetSelectionState()
    {
        IsSkillTargetSelectionMode = false;
        CurrentSkillTarget = default;
        CurrentSkillTargetSide = default;
        CurrentSkillTargetCriteria = null;
        CurrentSkillTargetSource = null;
        limitedCandidates = null;

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.ClearTargetHighlights();
        }

        targetSelectionTcs?.TrySetCanceled();
        targetSelectionTcs = null;
        EndMultiTargetSelection();
    }

    public static bool IsSingleBattlerTarget(TargetList targetList)
    {
        return targetList == TargetList.バトラー1体;
    }

    /// <summary>
    /// 効果対象から対象カードを集める。
    /// 「自分」は発動カード。側は見ない。
    /// 「技対象」は、まだバトラーゾーンかフィールドゾーンにいる対象カードだけ。いなければ空。
    /// プレイヤーHPへの指定はカードを返さない。未対応の指定は対象カードになる。
    /// </summary>
    public static Task<List<CardController>> CollectTargetsAsync(
        TargetList targetList,
        PlayerType side,
        SearchCriteria criteria,
        SkillContext context,
        SelectTarget select)
    {
        if (select == null)
            select = new SelectTarget();
        return CollectTargetsAsync(targetList, side, criteria, context, select.Count, select.選択方式);
    }

    public static async Task<List<CardController>> CollectTargetsAsync(
        TargetList targetList,
        PlayerType side,
        SearchCriteria criteria,
        SkillContext context,
        int selectCount = 1,
        TargetCountMode countMode = TargetCountMode.ちょうど)
    {
        var targets = new List<CardController>();
        if (BattleManager.Instance == null)
            return targets;

        var source = context?.発動カード;
        bool treatSourceAsPlayer = source == null || source.isMyCard;
        Transform myBattlerZone = CardZones.Of(CardZoneType.プレイヤーBゾーン, treatSourceAsPlayer);
        Transform opponentBattlerZone = CardZones.Of(CardZoneType.相手Bゾーン, treatSourceAsPlayer);
        Transform myFieldZone = CardZones.Of(CardZoneType.プレイヤーFゾーン, treatSourceAsPlayer);
        Transform opponentFieldZone = CardZones.Of(CardZoneType.相手Fゾーン, treatSourceAsPlayer);
        Transform myHandZone = CardZones.Of(CardZoneType.プレイヤー手札, treatSourceAsPlayer);
        Transform opponentHandZone = CardZones.Of(CardZoneType.相手手札, treatSourceAsPlayer);

        switch (targetList)
        {
            case TargetList.自分:
                if (source != null)
                    targets.Add(source);
                break;

            case TargetList.技対象:
                if (IsAttackTargetOnField(context?.対象カード))
                    targets.Add(context.対象カード);
                break;

            case TargetList.バトラー全員:
                AddBySide(targets, side, myBattlerZone, opponentBattlerZone, CardKind.バトラー);
                break;

            case TargetList.フィールド1枚:
                AddBySide(targets, side, myFieldZone, opponentFieldZone, CardKind.フィールド);
                break;

            case TargetList.手札:
                AddBySide(targets, side, myHandZone, opponentHandZone, null);
                break;

            case TargetList.バトラー1体:
                if (Instance == null)
                    break;
                int count = Mathf.Max(1, selectCount);
                if (count > 1)
                {
                    targets.AddRange(await Instance.ResolveMultipleTargetsAsync(targetList, side, criteria, context, count, countMode));
                    break;
                }
                var selected = await Instance.ResolveSingleTargetAsync(targetList, side, criteria, context);
                if (selected != null)
                    targets.Add(selected);
                break;

            case TargetList.プレイヤー:
                break;

            default:
                if (context?.対象カード != null)
                    targets.Add(context.対象カード);
                break;
        }

        if (criteria != null && !IsSingleBattlerTarget(targetList))
            targets = targets.Where(card => card != null && card.MeetsCriteria(criteria, context)).ToList();

        return targets;
    }

    /// <summary>
    /// 攻撃対象として効果を受けられる場所にいるか。
    /// 墓地・手札・デッキへ離れたカードや、参照が消えたカードは対象にしない。
    /// </summary>
    public static bool IsAttackTargetOnField(CardController card)
    {
        if (card == null || BattleManager.Instance == null)
            return false;

        ActiveLocation location = card.GetCurrentActiveLocation();
        return location == ActiveLocation.Bゾーン || location == ActiveLocation.Fゾーン;
    }

    static void AddBySide(List<CardController> targets, PlayerType side, Transform ownZone, Transform opponentZone, CardKind? kind)
    {
        if (TargetScope.IncludesOwner(side))
            targets.AddRange(CardsIn(ownZone, kind));
        if (TargetScope.IncludesOpponent(side))
            targets.AddRange(CardsIn(opponentZone, kind));
    }

    static IEnumerable<CardController> CardsIn(Transform zone, CardKind? kind = null)
    {
        if (zone == null)
            return Enumerable.Empty<CardController>();

        return zone.GetComponentsInChildren<CardController>()
            .Where(card => card != null && (kind == null || card.cardKind == kind.Value));
    }

    /// <summary>
    /// 1体選択の対象を決める。
    /// すでに効果対象を満たす TargetCard があればそれを使い、なければ選択UIを開く。
    /// </summary>
    public async Task<CardController> ResolveSingleTargetAsync(
        TargetList targetList,
        PlayerType side,
        SearchCriteria criteria,
        SkillContext context,
        string message = "効果の対象を選択してください")
    {
        var source = context?.発動カード;
        var preset = context?.対象カード;
        bool reusePreset = context == null || context.ReuseTargetCard;
        if (reusePreset && IsSingleBattlerTarget(targetList) && IsAcceptableTarget(preset, targetList, side, criteria, source))
            return preset;

        if (SkillContext.IsOfflineCpuResolving || IsOfflineCpuSource(source))
        {
            var picked = PickRandomTarget(targetList, side, criteria, source);
            await EmphasizeChosenTargets(picked);
            return picked;
        }

        BeginTargetSelection(targetList, side, criteria, source);
        MessageManager.Instance?.ShowMessage(message);
        CardController selected;
        try
        {
            selected = await WaitForTargetSelectionAsync();
        }
        finally
        {
            MessageManager.Instance?.HideMessage();
        }

        await EmphasizeChosenTargets(selected);
        return selected;
    }

    /// <summary>
    /// バトラーを複数選ぶ。
    /// ちょうどは指定数ぶん必須。までは1体以上で、指定数に届く前でも決定できる。
    /// 候補が最低数に満たなければ空を返す。
    /// </summary>
    public async Task<List<CardController>> ResolveMultipleTargetsAsync(
        TargetList targetList,
        PlayerType side,
        SearchCriteria criteria,
        SkillContext context,
        int count,
        TargetCountMode countMode = TargetCountMode.ちょうど,
        string message = null)
    {
        var empty = new List<CardController>();
        int maxCount = Mathf.Max(1, count);
        int minCount = countMode == TargetCountMode.まで ? 1 : maxCount;
        if (BattleManager.Instance == null)
            return empty;

        var source = context?.発動カード;
        var candidates = BattleManager.Instance.ListSelectableSkillTargets(targetList, side, criteria, source);
        if (candidates == null || candidates.Count < minCount)
        {
            Debug.Log(countMode == TargetCountMode.まで
                ? "[TargetSelectionManager] 対象候補がありません"
                : $"[TargetSelectionManager] 対象が{maxCount}体に足りません");
            return empty;
        }

        if (countMode == TargetCountMode.ちょうど && candidates.Count == maxCount)
        {
            await EmphasizeChosenTargets(candidates);
            return candidates;
        }
        if (countMode == TargetCountMode.まで && candidates.Count == 1)
        {
            await EmphasizeChosenTargets(candidates);
            return candidates;
        }

        int selectableMax = Mathf.Min(maxCount, candidates.Count);
        if (SkillContext.IsOfflineCpuResolving || IsOfflineCpuSource(source))
        {
            var picked = PickRandomTargets(candidates, countMode == TargetCountMode.まで ? selectableMax : maxCount);
            await EmphasizeChosenTargets(picked);
            return picked;
        }

        BeginMultiTargetSelection(targetList, side, criteria, source, selectableMax, minCount, countMode);
        string prompt = message;
        if (string.IsNullOrEmpty(prompt))
        {
            prompt = countMode == TargetCountMode.まで
                ? $"バトラーを{maxCount}体まで選択してください"
                : $"バトラーを{maxCount}体選択してください";
        }
        MessageManager.Instance?.ShowMessage(
            prompt,
            countMode == TargetCountMode.まで ? ConfirmCurrentSelection : null);
        RefreshConfirmButton();
        List<CardController> selected;
        try
        {
            selected = await multiTargetTcs.Task;
            await Task.Yield();
        }
        finally
        {
            MessageManager.Instance?.HideMessage();
            EndMultiTargetSelection();
        }

        if (selected == null || selected.Count < minCount)
            return empty;

        await EmphasizeChosenTargets(selected);
        return selected;
    }

    // 対象選択モードを開始するときに呼ぶ
    /// <summary>
    /// 渡した候補から1体選ぶ。1体ならそのまま、CPUならランダム、それ以外は選択UIを開く。
    /// </summary>
    public async Task<CardController> SelectOneFromAsync(IReadOnlyList<CardController> candidates, string message)
    {
        var list = new List<CardController>();
        if (candidates != null)
        {
            foreach (var card in candidates)
            {
                if (card != null && !list.Contains(card))
                    list.Add(card);
            }
        }

        if (list.Count == 0)
            return null;
        if (list.Count == 1)
        {
            await EmphasizeChosenTargets(list[0]);
            return list[0];
        }
        if (SkillContext.IsOfflineCpuResolving)
        {
            var picked = list[Random.Range(0, list.Count)];
            await EmphasizeChosenTargets(picked);
            return picked;
        }

        limitedCandidates = new HashSet<CardController>(list);
        requiredSelectCount = 1;
        minimumSelectCount = 1;
        selectCountMode = TargetCountMode.ちょうど;
        chosenTargets.Clear();
        IsSkillTargetSelectionMode = true;
        targetSelectionTcs = new TaskCompletionSource<CardController>();
        BattleManager.Instance?.ShowSpecificTargetHighlights(list);
        MessageManager.Instance?.ShowMessage(message);
        CardController selected;
        try
        {
            selected = await WaitForTargetSelectionAsync();
        }
        finally
        {
            limitedCandidates = null;
            IsSkillTargetSelectionMode = false;
            MessageManager.Instance?.HideMessage();
            BattleManager.Instance?.ClearTargetHighlights();
        }

        await EmphasizeChosenTargets(selected);
        return selected;
    }

    public void BeginTargetSelection(TargetList list, PlayerType side, SearchCriteria criteria = null, CardController sourceCard = null)
    {
        OpenSelection(list, side, criteria, sourceCard, 1, 1, TargetCountMode.ちょうど, multiple: false);
    }

    public void ConfirmSkillTargetSelection(CardController card)
    {
        if (card == null)
            return;
        if (limitedCandidates != null)
        {
            if (!limitedCandidates.Contains(card))
                return;
        }
        else
        {
            if (!PassesTargetList(card, CurrentSkillTarget, CurrentSkillTargetSide, CurrentSkillTargetSource))
                return;
            if (!PassesCriteria(card, CurrentSkillTargetCriteria, CurrentSkillTargetSource))
                return;
        }

        if (requiredSelectCount > 1)
        {
            ToggleChosenTarget(card);
            return;
        }

        if (BattleManager.Instance != null)
        {
            BattleManager.Instance.ClearTargetHighlights();
        }
        IsSkillTargetSelectionMode = false;
        targetSelectionTcs?.TrySetResult(card);
    }

    void BeginMultiTargetSelection(
        TargetList list,
        PlayerType side,
        SearchCriteria criteria,
        CardController sourceCard,
        int maxCount,
        int minCount,
        TargetCountMode countMode)
    {
        OpenSelection(list, side, criteria, sourceCard, maxCount, minCount, countMode, multiple: true);
    }

    void OpenSelection(
        TargetList list,
        PlayerType side,
        SearchCriteria criteria,
        CardController sourceCard,
        int maxCount,
        int minCount,
        TargetCountMode countMode,
        bool multiple)
    {
        limitedCandidates = null;
        requiredSelectCount = maxCount;
        minimumSelectCount = minCount;
        selectCountMode = countMode;
        chosenTargets.Clear();
        CurrentSkillTarget = list;
        CurrentSkillTargetSide = side;
        CurrentSkillTargetCriteria = criteria;
        CurrentSkillTargetSource = sourceCard;
        IsSkillTargetSelectionMode = true;
        if (multiple)
            multiTargetTcs = new TaskCompletionSource<List<CardController>>();
        else
            targetSelectionTcs = new TaskCompletionSource<CardController>();

        BattleManager.Instance?.ShowSkillTargetHighlights(list, side, criteria, sourceCard);
    }

    public void ConfirmCurrentSelection()
    {
        if (!IsSkillTargetSelectionMode || selectCountMode != TargetCountMode.まで)
            return;
        if (chosenTargets.Count < minimumSelectCount)
            return;

        CompleteMultiTargetSelection();
    }

    void ToggleChosenTarget(CardController card)
    {
        if (chosenTargets.Contains(card))
        {
            chosenTargets.Remove(card);
            card.SetTargetChosen(false);
            RefreshConfirmButton();
            return;
        }

        if (chosenTargets.Count >= requiredSelectCount)
            return;

        chosenTargets.Add(card);
        card.SetTargetChosen(true);
        if (chosenTargets.Count >= requiredSelectCount)
        {
            CompleteMultiTargetSelection();
            return;
        }

        RefreshConfirmButton();
    }

    void CompleteMultiTargetSelection()
    {
        if (!IsSkillTargetSelectionMode || multiTargetTcs == null)
            return;

        var result = new List<CardController>(chosenTargets);
        IsSkillTargetSelectionMode = false;
        MessageManager.Instance?.SetConfirmInteractable(false);
        multiTargetTcs.TrySetResult(result);
    }

    void RefreshConfirmButton()
    {
        bool canConfirm = selectCountMode == TargetCountMode.まで
            && chosenTargets.Count >= minimumSelectCount
            && chosenTargets.Count < requiredSelectCount;
        MessageManager.Instance?.SetConfirmInteractable(canConfirm);
    }

    void EndMultiTargetSelection()
    {
        requiredSelectCount = 1;
        minimumSelectCount = 1;
        selectCountMode = TargetCountMode.ちょうど;
        for (int i = 0; i < chosenTargets.Count; i++)
        {
            if (chosenTargets[i] != null)
                chosenTargets[i].SetTargetChosen(false);
        }
        chosenTargets.Clear();
        if (BattleManager.Instance != null)
            BattleManager.Instance.ClearTargetHighlights();

        var pending = multiTargetTcs;
        multiTargetTcs = null;
        pending?.TrySetResult(new List<CardController>());
    }

    static bool IsOfflineCpuSource(CardController source)
    {
        return source != null
            && !source.isMyCard
            && BattleManager.Instance != null
            && !BattleManager.Instance.IsOnlineMode;
    }

    CardController PickRandomTarget(TargetList targetList, PlayerType side, SearchCriteria criteria, CardController source)
    {
        if (BattleManager.Instance == null)
            return null;

        var candidates = BattleManager.Instance.ListSelectableSkillTargets(targetList, side, criteria, source);
        if (candidates == null || candidates.Count == 0)
        {
            Debug.Log("[TargetSelectionManager] CPUの対象候補がありません");
            return null;
        }

        var picked = candidates[Random.Range(0, candidates.Count)];
        Debug.Log($"[TargetSelectionManager] CPUが対象をランダム選択: {picked.cardName}");
        return picked;
    }

    static List<CardController> PickRandomTargets(List<CardController> candidates, int count)
    {
        var pool = new List<CardController>(candidates);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var swap = pool[i];
            pool[i] = pool[j];
            pool[j] = swap;
        }

        if (pool.Count > count)
            pool.RemoveRange(count, pool.Count - count);

        Debug.Log($"[TargetSelectionManager] CPUが対象をランダム選択: {pool.Count}体");
        return pool;
    }

    bool IsAcceptableTarget(CardController card, TargetList targetList, PlayerType side, SearchCriteria criteria, CardController sourceCard)
    {
        return card != null
            && PassesTargetList(card, targetList, side, sourceCard)
            && PassesCriteria(card, criteria, sourceCard);
    }

    bool PassesTargetList(CardController card, TargetList targetList, PlayerType side, CardController sourceCard)
    {
        if (card == null || BattleManager.Instance == null)
            return false;

        switch (targetList)
        {
            case TargetList.バトラー1体:
                bool own = IsBattlerInZone(card, MyBattlerZone(sourceCard));
                bool opponent = IsBattlerInZone(card, OpponentBattlerZone(sourceCard));
                if (side == PlayerType.プレイヤー)
                    return own;
                if (side == PlayerType.相手)
                    return opponent;
                if (side == PlayerType.両方)
                    return own || opponent;
                return false;
            case TargetList.バトラー全員:
                return false;
            default:
                return true;
        }
    }

    bool PassesCriteria(CardController card, SearchCriteria criteria, CardController sourceCard)
    {
        if (criteria == null)
            return true;

        if (sourceCard != null && criteria.ソースカード条件 != SourceCardType.None)
        {
            var context = new SkillContext(sourceCard, card, card.transform);
            return card.MeetsCriteria(criteria, context);
        }

        return card.MeetsCriteria(criteria);
    }

    static bool IsBattlerInZone(CardController card, Transform zone)
    {
        return card.cardKind == CardKind.バトラー && card.transform.parent == zone;
    }

    static bool TreatSourceAsPlayer(CardController sourceCard)
    {
        return sourceCard == null || sourceCard.isMyCard;
    }

    static Transform MyBattlerZone(CardController sourceCard)
    {
        return CardZones.Of(CardZoneType.プレイヤーBゾーン, TreatSourceAsPlayer(sourceCard));
    }

    static Transform OpponentBattlerZone(CardController sourceCard)
    {
        return CardZones.Of(CardZoneType.相手Bゾーン, TreatSourceAsPlayer(sourceCard));
    }

    public async Task<CardController> WaitForTargetSelectionAsync()
    {
        if (targetSelectionTcs != null)
            return await targetSelectionTcs.Task;
        return null;
    }

    async Task EmphasizeChosenTargets(CardController card)
    {
        if (card == null)
            return;
        await EmphasizeChosenTargets(new[] { card });
    }

    async Task EmphasizeChosenTargets(IEnumerable<CardController> cards)
    {
        if (CardAnimationManager.Instance == null)
            return;
        await CardAnimationManager.Instance.EmphasizeSelectedCards(cards, syncOnline: true);
    }
}

