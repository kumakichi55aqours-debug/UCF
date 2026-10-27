using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public partial class BattleManager
{
    // ターゲット選択モード用の変数
    public bool IsTargetSelectionMode { get; private set; }
    public bool IsAwaitingMoveTargetChoice { get; private set; }
    bool moveTargetChoiceOpen;
    bool moveMultiSelect;
    bool moveTargetCommitting;
    bool resolvingSelectedMove;
    int moveSelectMax = 1;
    int moveSelectMin = 1;
    TargetCountMode moveSelectMode = TargetCountMode.ちょうど;
    readonly List<CardController> chosenMoveTargets = new List<CardController>();
    public CardController selectedAttacker;
    public int selectedMoveIndex;  // 0: 技1, 1: 技2
    private readonly List<CardController> highlightedTargetCards = new List<CardController>();
    bool skillTargetHighlightsSent;

    /// <summary>
    /// 技の対象を待っているときだけ取り消す。エネルギー支払い前なので、技選択のメニューへ戻せる。
    /// 取り消した攻撃者を返す。対象待ちでなければ null。
    /// </summary>
    public CardController CancelMoveTargetChoice()
    {
        if (!IsAwaitingMoveTargetChoice || !moveTargetChoiceOpen)
            return null;

        CardController attacker = selectedAttacker;
        CloseMoveTargetChoice(syncOnline: true);
        return attacker;
    }

    /// <summary>
    /// 相手が対象選択を右クリックで取り消したとき。解決が始まっていたら何もしない。
    /// </summary>
    public void AbortRemoteMoveTargetChoice(CardController attacker)
    {
        if (!moveTargetChoiceOpen || attacker == null || attacker != selectedAttacker)
            return;

        CloseMoveTargetChoice(syncOnline: false);
    }

    void CloseMoveTargetChoice(bool syncOnline)
    {
        CardController attacker = selectedAttacker;
        ClearPendingMoveHit();
        ClearTargetHighlights();
        MessageManager.Instance?.HideMessage();
        EndMoveTargetSelection();

        if (syncOnline && IsOnlineMode && attacker != null && attacker.isMyCard)
            OnlineSyncManager.Instance?.SyncCancelMoveTarget(attacker);
    }

    /// <summary>
    /// 相手が選んだ複数対象を受け取る前に、こちらの選択待ちだけ閉じる。TargetPanel は技のダメージ表示のあとまで残す。
    /// </summary>
    public void PrepareRemoteMoveResolution()
    {
        ResetMoveMultiState();
        moveTargetChoiceOpen = false;
        IsTargetSelectionMode = false;
        IsAwaitingMoveTargetChoice = false;
        MessageManager.Instance?.HideMessage();
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
    }

    void OpenSingleMoveTargetChoice(CardController attacker, int moveIndex, TargetList targetType)
    {
        selectedAttacker = attacker;
        selectedMoveIndex = moveIndex;
        IsTargetSelectionMode = true;
        moveTargetChoiceOpen = true;
        moveMultiSelect = false;
        ShowMoveTargetHighlights(attacker, moveIndex);

        if (!IsOnlineMode || attacker.isMyCard)
        {
            IsAwaitingMoveTargetChoice = true;
            PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
            var moveBattler = attacker?.model as BattlerModel;
            bool opponentSingle = targetType == TargetList.バトラー1体
                && moveBattler != null
                && moveBattler.GetMoveSide(moveIndex) == PlayerType.相手;
            string message = opponentSingle && MoveTargetZoneHasAttackFocus(attacker)
                ? "攻撃集中しているバトラーを選択してください"
                : "技の対象を選択してください";
            MessageManager.Instance.ShowMessage(message);
        }
    }

    async Task BeginMultiMoveSelection(CardController attacker, int moveIndex)
    {
        if (resolvingSelectedMove || attacker == null)
            return;

        var select = MoveTargetEvasion.MoveSelect(attacker, moveIndex);
        var legal = MoveTargetEvasion.ListLegalTargets(attacker, moveIndex);
        if (legal.Count < select.MinimumCount)
        {
            AbortMoveNoTarget(attacker);
            return;
        }

        int selectableMax = Mathf.Min(select.Count, legal.Count);
        bool auto = (select.選択方式 == TargetCountMode.ちょうど && legal.Count == select.Count)
            || (select.選択方式 == TargetCountMode.まで && legal.Count == 1);
        if (auto)
        {
            await ResolveSelectedMoveTargets(attacker, moveIndex, legal, payEnergy: true);
            return;
        }

        selectedAttacker = attacker;
        selectedMoveIndex = moveIndex;
        IsTargetSelectionMode = true;
        moveTargetChoiceOpen = true;
        moveMultiSelect = true;
        moveSelectMax = selectableMax;
        moveSelectMin = select.MinimumCount;
        moveSelectMode = select.選択方式;
        chosenMoveTargets.Clear();
        ShowMoveTargetHighlights(attacker, moveIndex);

        if (IsOnlineMode && !attacker.isMyCard)
            return;

        IsAwaitingMoveTargetChoice = true;
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
        bool focused = MoveTargetsAreFocused(attacker, moveIndex);
        string who = focused ? "攻撃集中しているバトラー" : "バトラー";
        string message = moveSelectMode == TargetCountMode.まで
            ? $"{who}を{select.Count}体まで選択してください"
            : $"{who}を{select.Count}体選択してください";
        MessageManager.Instance?.ShowMessage(
            message,
            moveSelectMode == TargetCountMode.まで ? ConfirmMoveTargetSelection : null);
        RefreshMoveConfirm();
    }

    bool MoveTargetsAreFocused(CardController attacker, int moveIndex)
    {
        var battler = attacker?.model as BattlerModel;
        if (battler == null
            || battler.GetMoveTarget(moveIndex) != TargetList.バトラー1体
            || battler.GetMoveSide(moveIndex) != PlayerType.相手)
            return false;

        var legal = MoveTargetEvasion.ListLegalTargets(attacker, moveIndex);
        return legal.Count > 0 && legal.All(AttackFocusHelper.HasAttackFocus);
    }

    void ToggleMoveTarget(CardController card)
    {
        if (card == null || moveTargetCommitting)
            return;

        if (chosenMoveTargets.Contains(card))
        {
            chosenMoveTargets.Remove(card);
            card.SetTargetChosen(false);
            RefreshMoveConfirm();
            return;
        }

        if (chosenMoveTargets.Count >= moveSelectMax)
            return;

        chosenMoveTargets.Add(card);
        card.SetTargetChosen(true);
        if (chosenMoveTargets.Count >= moveSelectMax)
        {
            _ = CommitMoveTargets();
            return;
        }

        RefreshMoveConfirm();
    }

    void RefreshMoveConfirm()
    {
        bool canConfirm = moveSelectMode == TargetCountMode.まで
            && chosenMoveTargets.Count >= moveSelectMin
            && chosenMoveTargets.Count < moveSelectMax;
        MessageManager.Instance?.SetConfirmInteractable(canConfirm);
    }

    public void ConfirmMoveTargetSelection()
    {
        if (!moveMultiSelect || moveSelectMode != TargetCountMode.まで)
            return;
        if (chosenMoveTargets.Count < moveSelectMin)
            return;

        _ = CommitMoveTargets();
    }

    async Task CommitMoveTargets()
    {
        if (moveTargetCommitting || !moveMultiSelect || selectedAttacker == null)
            return;
        if (chosenMoveTargets.Count < moveSelectMin)
            return;

        moveTargetCommitting = true;
        var attacker = selectedAttacker;
        int moveIndex = selectedMoveIndex;
        var targets = new List<CardController>(chosenMoveTargets);
        try
        {
            moveTargetChoiceOpen = false;
            IsAwaitingMoveTargetChoice = false;
            IsTargetSelectionMode = false;
            MessageManager.Instance?.HideMessage();
            ResetMoveMultiState();
            PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();

            if (IsOnlineMode && attacker.isMyCard)
                OnlineSyncManager.Instance?.SyncSelectMoveTargets(attacker, targets, moveIndex);

            await ResolveSelectedMoveTargets(attacker, moveIndex, targets, payEnergy: true);
        }
        finally
        {
            moveTargetCommitting = false;
        }
    }

    void ResetMoveMultiState()
    {
        moveMultiSelect = false;
        moveSelectMax = 1;
        moveSelectMin = 1;
        moveSelectMode = TargetCountMode.ちょうど;
        for (int i = 0; i < chosenMoveTargets.Count; i++)
        {
            if (chosenMoveTargets[i] != null)
                chosenMoveTargets[i].SetTargetChosen(false);
        }
        chosenMoveTargets.Clear();
    }

    void EndMoveTargetSelection()
    {
        ResetMoveMultiState();
        moveTargetChoiceOpen = false;
        IsTargetSelectionMode = false;
        IsAwaitingMoveTargetChoice = false;
        selectedAttacker = null;
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
    }

    // ターゲット選択（オンライン同期処理を担当）
    public async Task SelectTargetAsync(CardController enemyCard)
    {
        if (!IsTargetSelectionMode || selectedAttacker == null || moveTargetCommitting)
            return;
        if (IsOnlineMode && !selectedAttacker.isMyCard)
            return;
        if (!IsAllowedMoveTarget(enemyCard))
            return;

        if (moveMultiSelect)
        {
            ToggleMoveTarget(enemyCard);
            return;
        }

        IsAwaitingMoveTargetChoice = false;
        PhaseManager.Instance?.RefreshNextPhaseButtonVisibility();
        MessageManager.Instance.HideMessage();

        if (IsOnlineMode)
        {
            OnlineSyncManager.Instance.SyncSelectEnemyTarget(selectedAttacker, enemyCard, selectedMoveIndex);
            _ = SelectTargetRoutine(enemyCard);
            return;
        }
        await SelectTargetRoutine(enemyCard);
    }

    private IEnumerable<CardController> GetCardsInZone(Transform zone, CardKind? requiredKind = null)
    {
        if (zone == null) yield break;

        foreach (var card in zone.GetComponentsInChildren<CardController>())
        {
            if (requiredKind.HasValue && card.cardKind != requiredKind.Value)
                continue;

            yield return card;
        }
    }

    /// <summary>
    /// 候補のハイライトから、選ばれなかったカードだけ消す。選ばれたカードは技が終わるまで残す。
    /// </summary>
    public void RetainChosenTargetHighlights(IReadOnlyList<CardController> chosen)
    {
        var keep = new HashSet<CardController>();
        if (chosen != null)
        {
            foreach (var card in chosen)
            {
                if (card != null)
                    keep.Add(card);
            }
        }

        for (int i = highlightedTargetCards.Count - 1; i >= 0; i--)
        {
            var card = highlightedTargetCards[i];
            if (card != null && keep.Contains(card))
                continue;
            if (card != null)
                card.SetTargetHighlightVisible(false);
            highlightedTargetCards.RemoveAt(i);
        }
    }

    public void ClearTargetHighlights()
    {
        ClearLocalTargetHighlights();
        if (!skillTargetHighlightsSent || OnlineSyncManager.Instance == null || !IsOnlineMode)
            return;

        skillTargetHighlightsSent = false;
        OnlineSyncManager.Instance.SyncClearSkillTargetHighlights();
    }

    public void ShowResolvedTargetHighlights(IReadOnlyList<CardController> cards)
    {
        ClearLocalTargetHighlights();
        HighlightTargetCards(cards);
    }

    public void ClearResolvedTargetHighlights()
    {
        ClearLocalTargetHighlights();
    }

    void ClearLocalTargetHighlights()
    {
        foreach (var card in highlightedTargetCards)
        {
            if (card != null)
            {
                card.SetTargetHighlightVisible(false);
            }
        }
        highlightedTargetCards.Clear();
    }

    private void HighlightTargetCards(IEnumerable<CardController> cards)
    {
        foreach (var card in cards)
        {
            if (card == null) continue;

            card.SetTargetHighlightVisible(true);

            if (!highlightedTargetCards.Contains(card))
            {
                highlightedTargetCards.Add(card);
            }
        }
    }

    bool MoveTargetZoneHasAttackFocus(CardController attacker)
    {
        if (attacker == null) return false;
        Transform targetZone = attacker.isMyCard ? eBattlerZone : pBattlerZone;
        var legal = MoveTargetEvasion.LegalBattlers(GetCardsInZone(targetZone, CardKind.バトラー), TargetList.バトラー1体, PlayerType.相手);
        return legal.Count > 0 && legal.All(AttackFocusHelper.HasAttackFocus);
    }

    bool IsAllowedMoveTarget(CardController candidate)
    {
        if (selectedAttacker == null || candidate == null)
            return false;

        var battler = selectedAttacker.model as BattlerModel;
        if (battler == null)
            return true;

        TargetList targetType = selectedMoveIndex == 0 ? battler.target1 : battler.target2;
        if (MoveTargetEvasion.IsSelectableBattlerTarget(targetType))
            return MoveTargetEvasion.ListLegalTargets(selectedAttacker, selectedMoveIndex).Contains(candidate);

        if (MoveTargetEvasion.IsBattlerTarget(targetType) && MoveTargetEvasion.IsEvading(candidate))
            return false;

        return true;
    }

    private void ShowMoveTargetHighlights(CardController attacker, int moveIndex)
    {
        ClearTargetHighlights();

        if (attacker == null)
            return;

        var battler = attacker.model as BattlerModel;
        if (battler == null)
            return;

        TargetList targetType = battler.GetMoveTarget(moveIndex);
        PlayerType side = battler.GetMoveSide(moveIndex);
        switch (targetType)
        {
            case TargetList.バトラー1体:
                HighlightTargetCards(MoveTargetEvasion.ListLegalTargets(attacker, moveIndex));
                break;
            case TargetList.フィールド1枚:
                if (TargetScope.IncludesOwner(side))
                {
                    Transform ownField = attacker.isMyCard ? pFieldZone : eFieldZone;
                    HighlightTargetCards(GetCardsInZone(ownField, CardKind.フィールド));
                }
                if (TargetScope.IncludesOpponent(side))
                {
                    Transform opponentField = attacker.isMyCard ? eFieldZone : pFieldZone;
                    HighlightTargetCards(GetCardsInZone(opponentField, CardKind.フィールド));
                }
                break;
        }
    }

    private IEnumerable<CardController> GetSkillTargetCandidates(TargetList targetList, PlayerType side, CardController sourceCard)
    {
        bool treatSourceAsPlayer = sourceCard == null || sourceCard.isMyCard;

        Transform myBattlerZone = treatSourceAsPlayer ? pBattlerZone : eBattlerZone;
        Transform opponentBattlerZone = treatSourceAsPlayer ? eBattlerZone : pBattlerZone;
        Transform myFieldZone = treatSourceAsPlayer ? pFieldZone : eFieldZone;
        Transform opponentFieldZone = treatSourceAsPlayer ? eFieldZone : pFieldZone;
        Transform myHandZone = treatSourceAsPlayer ? pHandZone : eHandZone;
        Transform opponentHandZone = treatSourceAsPlayer ? eHandZone : pHandZone;

        switch (targetList)
        {
            case TargetList.バトラー1体:
                return CardsOnSide(side, myBattlerZone, opponentBattlerZone, CardKind.バトラー);
            case TargetList.フィールド1枚:
                return CardsOnSide(side, myFieldZone, opponentFieldZone, CardKind.フィールド);
            case TargetList.手札:
                return CardsOnSide(side, myHandZone, opponentHandZone, null);
            default:
                return Enumerable.Empty<CardController>();
        }
    }

    IEnumerable<CardController> CardsOnSide(PlayerType side, Transform ownZone, Transform opponentZone, CardKind? kind)
    {
        IEnumerable<CardController> cards = Enumerable.Empty<CardController>();
        if (TargetScope.IncludesOwner(side))
            cards = cards.Concat(GetCardsInZone(ownZone, kind));
        if (TargetScope.IncludesOpponent(side))
            cards = cards.Concat(GetCardsInZone(opponentZone, kind));
        return cards;
    }

    private IEnumerable<CardController> ApplyCriteriaFilter(IEnumerable<CardController> candidates, SearchCriteria criteria, CardController sourceCard)
    {
        if (criteria == null)
            return candidates;

        if (sourceCard != null && criteria.ソースカード条件 != SourceCardType.None)
        {
            return candidates.Where(card =>
            {
                var context = new SkillContext(sourceCard, card, card.transform);
                return card.MeetsCriteria(criteria, context);
            });
        }

        return candidates.Where(card => card.MeetsCriteria(criteria));
    }

    public List<CardController> ListSelectableSkillTargets(TargetList targetList, PlayerType side, SearchCriteria criteria, CardController sourceCard)
    {
        return ApplyCriteriaFilter(GetSkillTargetCandidates(targetList, side, sourceCard), criteria, sourceCard)
            .Where(card => card != null)
            .ToList();
    }

    public void ShowSkillTargetHighlights(TargetList targetList, PlayerType side, SearchCriteria criteria = null, CardController sourceCard = null)
    {
        ClearTargetHighlights();
        HighlightTargetCards(ListSelectableSkillTargets(targetList, side, criteria, sourceCard));
        SyncSkillTargetHighlights();
    }

    public void ShowSpecificTargetHighlights(IEnumerable<CardController> cards)
    {
        ClearTargetHighlights();
        HighlightTargetCards(cards);
        SyncSkillTargetHighlights();
    }

    void SyncSkillTargetHighlights()
    {
        if (OnlineSyncManager.Instance == null || !IsOnlineMode)
            return;

        var kinds = new List<string>();
        var ids = new List<int>();
        var zoneNames = new List<string>();
        var indices = new List<int>();
        foreach (var card in highlightedTargetCards)
            CardAddress.TryAdd(card, kinds, ids, zoneNames, indices);

        skillTargetHighlightsSent = true;
        OnlineSyncManager.Instance.SyncSkillTargetHighlights(kinds.ToArray(), ids.ToArray(), zoneNames.ToArray(), indices.ToArray());
    }
}
