using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using DG.Tweening;
using TMPro;

public class CardContextMenu : MonoBehaviour
{
    public static CardContextMenu Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private Canvas parentCanvas;
    [SerializeField] private RectTransform contextMenuPanel;
    [SerializeField] private VerticalLayoutGroup buttonContainer;

    [Header("Buttons")]
    [SerializeField] private Button summonButton;
    [SerializeField] private Button useFromHandButton;
    [SerializeField] private Button useHandSkillButton;
    [SerializeField] private Button useMoveButton;
    [SerializeField] private Button useSkillButton;
    [SerializeField] private Button grantedSkillButton;
    [SerializeField] private Button move1Button;
    [SerializeField] private Button move2Button;
    [SerializeField] private Button checkGraveButton;
    
    [Header("Move Button Text")]
    [SerializeField] private TextMeshProUGUI move1ButtonText;
    [SerializeField] private TextMeshProUGUI move2ButtonText;

    private CardController currentCard;
    private bool isUsingSkill;
    // 墓地の山を開いたときの「スキルを使用」は、その場で発動せず一覧を出す
    private bool skillButtonListsGrave;

    /// <summary>
    /// 処理中かどうかをチェック（召喚演出、技の処理、スキルの処理中など）
    /// </summary>
    private bool IsProcessing()
    {
        // スキル使用中は処理中止
        if (isUsingSkill)
            return true;

        // チェーン上の効果処理中（確認、効果、そのアニメーション完了まで）
        if (SkillChainManager.Instance != null && SkillChainManager.Instance.IsResolvingSkills)
            return true;

        // アニメーション表示中は処理中止
        if (DOTween.TotalPlayingTweens() > 0)
            return true;

        // スキル対象選択中は処理中止
        if (TargetSelectionManager.Instance != null && TargetSelectionManager.Instance.IsSkillTargetSelectionMode)
            return true;

        // YesOrNoSelectionUI表示中は処理中止
        if (YesOrNoSelectionUI.Instance != null && YesOrNoSelectionUI.Instance.IsShowing)
            return true;

        if (OpponentSelectUI.Instance != null && OpponentSelectUI.Instance.IsShowing)
            return true;

        // プレゼンテーション一時停止中（手札確認など）
        if (OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.PresentationBlocking)
            return true;

        return false;
    }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        contextMenuPanel.gameObject.SetActive(false);

        // 各ボタンの onClick 登録
        summonButton.onClick.AddListener(OnSummon);
        useFromHandButton.onClick.AddListener(OnUseFromHand);
        useHandSkillButton.onClick.AddListener(OnUseHandSkill);
        useMoveButton.onClick.AddListener(OnUseMoveOption);
        move1Button.onClick.AddListener(() => OnUseMove(0));
        move2Button.onClick.AddListener(() => OnUseMove(1));
        useSkillButton.onClick.AddListener(OnUseZoneSkill);
        checkGraveButton.onClick.AddListener(OnCheckGrave);
        EnsureGrantedSkillButton();
        if (grantedSkillButton != null)
            grantedSkillButton.onClick.AddListener(OnShowGrantedSkills);
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(1))
            return;

        CardController attacker = BattleManager.Instance != null
            ? BattleManager.Instance.CancelMoveTargetChoice()
            : null;
        if (attacker != null)
        {
            RestoreMoveSelection(attacker);
            return;
        }

        DismissTransientWindows();
    }

    /// <summary>
    /// 技の対象選択を取り消したあと、技1・技2を選ぶメニューを出し直す。
    /// </summary>
    public void RestoreMoveSelection(CardController card)
    {
        if (card == null)
            return;

        ShowBattleOptions(card, card.transform.position);
        if (currentCard != card || contextMenuPanel == null || !contextMenuPanel.gameObject.activeSelf)
            return;

        OnUseMoveOption();
    }

    /// <summary>
    /// 確認用ウィンドウとカードメニューをまとめて閉じる。
    /// 付与スキル一覧・墓地一覧・右クリックメニューが対象。
    /// </summary>
    public static void DismissTransientWindows()
    {
        if (Instance != null)
            Instance.HideMenu();
        GrantedSkillListUI.HideIfAny();
        GraveViewUI.HideIfAny();
    }

    // 手札ボタン表示
    public void ShowHandOptions(CardController card, Vector3 worldPos)
    {
        if (PhaseManager.Instance.CurrentPhase != TurnPhase.メインフェイズ) return;
        if (!OnlineSyncManager.Instance.CanPlayerOperateCards()) return;
        if (IsProcessing()) return;

        currentCard = card;
        DismissTransientWindows();
        HideAllButtons();

        if (card.cardKind == CardKind.バトラー || card.cardKind == CardKind.フィールド)
        {
            if (CanNormalSummonFromHand(card))
                summonButton.gameObject.SetActive(true);
            if (HasCanUseSkill(card, ActiveTiming.起動効果, ActiveLocation.手札))
                useHandSkillButton.gameObject.SetActive(true);
        }
        else if (CanPlayTacticsFromHand(card))
        {
            useFromHandButton.gameObject.SetActive(true);
        }

        ShowIfAnyButton(worldPos);
    }

    // 手札からの通常登場。Bゾーンが5体未満で、フィールドならFゾーンが空のとき。
    private static bool CanNormalSummonFromHand(CardController card)
    {
        if (!CanPayToEnterPlay(card)) return false;
        if (CannotNormalSummonSkill.Blocks(card)) return false;
        if (BattleManager.Instance.pBattlerZone.childCount >= 5) return false;
        if (card.cardKind == CardKind.フィールド)
            return BattleManager.Instance.pFieldZone.childCount == 0;
        return true;
    }

    // 作戦カードは、支払えて登場時スキルを手札から使えるときだけ。
    private static bool CanPlayTacticsFromHand(CardController card)
    {
        return CanPayToEnterPlay(card)
            && HasCanUseSkill(card, ActiveTiming.登場時, ActiveLocation.手札);
    }

    private static bool CanPayToEnterPlay(CardController card)
    {
        if (UniqueCardRestrictionHelper.IsRestrictedByUniqueCardRule(card))
            return false;
        return card.GetModifiedCost() <= BattleManager.Instance.pResources.Cost.currentCost;
    }

    // バトルフェイズ、ゾーンボタン表示
    public void ShowBattleOptions(CardController card, Vector3 worldPos)
    {
        if (PhaseManager.Instance.CurrentPhase == TurnPhase.バトルフェイズ)
        {
            // オンライン対戦時のターン制御
            if (!OnlineSyncManager.Instance.CanPlayerOperateCards()) return;

            // 処理中（召喚演出、技の処理、スキルの処理中など）の場合は表示しない
            if (IsProcessing()) return;

            currentCard = card;
            DismissTransientWindows();
            HideAllButtons();

            // 1ターン目以外 && 技未使用か。気絶中は技を宣言できない
            if (!card.hasUsedMove && !card.IsActionBlocked && BattleManager.Instance.curTurn != 1)
                useMoveButton.gameObject.SetActive(true);

            if (HasGrantedSkills(card) && grantedSkillButton != null)
                grantedSkillButton.gameObject.SetActive(true);

            ShowIfAnyButton(worldPos);
        }
        else if (PhaseManager.Instance.CurrentPhase == TurnPhase.メインフェイズ)
        {
            // オンライン対戦時のターン制御
            if (!OnlineSyncManager.Instance.CanPlayerOperateCards()) return;

            // 処理中（召喚演出、技の処理、スキルの処理中など）の場合は表示しない
            if (IsProcessing()) return;

            currentCard = card;
            DismissTransientWindows();
            HideAllButtons();

            if (!card.IsActionBlocked && HasCanUseSkill(card, ActiveTiming.起動効果, card.GetCurrentActiveLocation()))
                useSkillButton.gameObject.SetActive(true);

            if (HasGrantedSkills(card) && grantedSkillButton != null)
                grantedSkillButton.gameObject.SetActive(true);

            ShowIfAnyButton(worldPos);
        }
    }

    // 墓地ボタン表示。発動できるカードが墓地にあれば「スキルを使用」で一覧を開く
    public void ShowGraveOptions(CardController card, Vector3 worldPos)
    {
        // 処理中（召喚演出、技の処理、スキルの処理中など）の場合は表示しない
        if (IsProcessing()) return;

        currentCard = card;
        DismissTransientWindows();
        HideAllButtons();
        checkGraveButton.gameObject.SetActive(true);

        bool ownGrave = card != null
            && BattleManager.Instance != null
            && card.transform.parent == BattleManager.Instance.pGraveZone;
        if (ownGrave && CanOperateOwnGraveSkills() && CollectActivatableGraveCards().Count > 0)
        {
            skillButtonListsGrave = true;
            useSkillButton.gameObject.SetActive(true);
        }

        LayoutAndShow(worldPos);
    }

    // 墓地スキル一覧で選んだカード。本体に対して「スキルを使用」を出す
    public void ShowGraveSkillOptions(CardController card, Vector3 worldPos)
    {
        if (IsProcessing()) return;
        if (!CanOperateOwnGraveSkills()) return;
        if (!CanActivateFromOwnGrave(card)) return;

        currentCard = card;
        HideMenu();
        HideAllButtons();
        useSkillButton.gameObject.SetActive(true);
        LayoutAndShow(worldPos);
    }

    // 技ボタン表示
    private void OnUseMoveOption()
    {
        useMoveButton.gameObject.SetActive(false);

        if (currentCard != null)
        {
            var battler = currentCard.model as BattlerModel;
            if (battler != null)
            {
                // 技1の制限とエネルギー量をチェック
                bool move1Restricted = currentCard.IsSkillRestricted(0, battler.moveElement1);
                int move1Cost = currentCard.GetMoveEnergyCost(0);
                bool move1EnergySufficient = BattleManager.Instance.pResources.Energy.currentEnergy >= move1Cost;
                bool move1Targetable = MoveTargetEvasion.HasLegalTarget(currentCard, 0);
                bool move1Available = !move1Restricted && move1EnergySufficient && move1Targetable;

                // 技2の制限とエネルギー量をチェック
                bool move2Restricted = currentCard.IsSkillRestricted(1, battler.moveElement2);
                int move2Cost = currentCard.GetMoveEnergyCost(1);
                bool move2EnergySufficient = BattleManager.Instance.pResources.Energy.currentEnergy >= move2Cost;
                bool move2Targetable = MoveTargetEvasion.HasLegalTarget(currentCard, 1);
                bool move2Available = !move2Restricted && move2EnergySufficient && move2Targetable;

                // 技1ボタンの状態を設定
                move1Button.gameObject.SetActive(true);
                move1Button.interactable = move1Available;
                UpdateMoveButtonText(move1ButtonText, battler.moveName1, move1Cost, move1Available);

                // 技2ボタンの状態を設定
                move2Button.gameObject.SetActive(true);
                move2Button.interactable = move2Available;
                UpdateMoveButtonText(move2ButtonText, battler.moveName2, move2Cost, move2Available);
            }
        }
    }

    // 自分のメインフェイズで、自分の墓地の起動効果を操作できる
    private static bool CanOperateOwnGraveSkills()
    {
        return PhaseManager.Instance != null
            && PhaseManager.Instance.CurrentPhase == TurnPhase.メインフェイズ
            && OnlineSyncManager.Instance != null
            && OnlineSyncManager.Instance.CanPlayerOperateCards();
    }

    private static bool CanActivateFromOwnGrave(CardController card)
    {
        if (card == null || BattleManager.Instance == null) return false;
        if (card.transform.parent != BattleManager.Instance.pGraveZone) return false;
        return HasCanUseSkill(card, ActiveTiming.起動効果, ActiveLocation.墓地);
    }

    private static List<CardController> CollectActivatableGraveCards()
    {
        var result = new List<CardController>();
        Transform zone = BattleManager.Instance != null ? BattleManager.Instance.pGraveZone : null;
        if (zone == null) return result;

        foreach (CardController card in zone.GetComponentsInChildren<CardController>())
        {
            if (CanActivateFromOwnGrave(card))
                result.Add(card);
        }

        return result;
    }

    // スキル使用可能条件判定。墓地のカードは行動不能にもスローにもならない
    private static bool HasCanUseSkill(CardController card, ActiveTiming timing, ActiveLocation location)
    {
        if (card == null || card.skills == null) return false;
        if (location != ActiveLocation.墓地)
        {
            if (card.IsActionBlocked) return false;
            if (timing == ActiveTiming.起動効果 && card.IsSlowed) return false;
        }

        var ctx = new SkillContext(card, card, card.transform);
        return card.skills.Any(s => s != null && s.CanUseAt(timing, location, ctx));
    }

    private static bool HasGrantedSkills(CardController card)
    {
        return card?.passiveEffects != null && card.passiveEffects.Count > 0;
    }

    private void EnsureGrantedSkillButton()
    {
        if (grantedSkillButton != null) return;
        if (useSkillButton == null) return;

        var clone = Instantiate(useSkillButton.gameObject, useSkillButton.transform.parent);
        clone.name = "GrantedSkillButton";
        grantedSkillButton = clone.GetComponent<Button>();
        grantedSkillButton.onClick = new Button.ButtonClickedEvent();

        var label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = "付与スキル";

        clone.SetActive(false);
    }

    // リセット処理
    private void HideAllButtons()
    {
        if (buttonContainer == null) return;
        foreach (Transform child in buttonContainer.transform)
            child.gameObject.SetActive(false);
    }

    // 冗長ボタン削除処理
    private void ShowIfAnyButton(Vector3 worldPos)
    {
        bool anyVisible = buttonContainer != null
            && buttonContainer.transform.Cast<Transform>().Any(t => t.gameObject.activeSelf);

        if (anyVisible)
            LayoutAndShow(worldPos);
        else
            contextMenuPanel.gameObject.SetActive(false);
    }

    // 正確座標処理
    private void LayoutAndShow(Vector3 worldPos)
    {
        // ワールド座標をCanvas座標に変換
        RectTransform canvasRect = parentCanvas.transform as RectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, worldPos, parentCanvas.worldCamera, out Vector2 localPos);

        contextMenuPanel.SetParent(parentCanvas.transform, false);
        contextMenuPanel.SetAsLastSibling();
        contextMenuPanel.anchoredPosition = localPos + new Vector2(64, 48);
        buttonContainer.gameObject.SetActive(true);
        contextMenuPanel.gameObject.SetActive(true);
    }

    // 共通クローズ処理
    private void Hide()
    {
        DismissTransientWindows();
    }

    public static void HideMenuIfAny()
    {
        if (Instance != null)
            Instance.HideMenu();
    }

    private void HideMenu()
    {
        skillButtonListsGrave = false;
        contextMenuPanel.gameObject.SetActive(false);
        HideAllButtons();
    }

    //--- ボタンコールバック ---

    // バトラー/フィールド登場処理
    private async void OnSummon()
    {
        Hide();
        await BattleManager.Instance.SummonBattler(currentCard);
    }

    // 作戦カード発動処理
    private async void OnUseFromHand()
    {
        Hide();
        await BattleManager.Instance.UseTactics(currentCard);
    }

    // 手札スキル発動処理
    private async void OnUseHandSkill()
    {
        Hide();
        if (currentCard == null) return;

        isUsingSkill = true;
        try
        {
            await currentCard.UseZoneActivatedSkill();
        }
        finally
        {
            isUsingSkill = false;
        }
    }

    // 技発動処理
    private async void OnUseMove(int moveId)
    {
        Hide();
        await BattleManager.Instance.UseMoveAsync(currentCard, moveId);
    }

    // 技ボタンのテキストを更新
    private void UpdateMoveButtonText(TextMeshProUGUI buttonText, string moveName, int energyCost, bool isAvailable)
    {
        if (buttonText == null) return;

        buttonText.text = $"{moveName} (エネルギー: {energyCost})";
        buttonText.color = isAvailable ? Color.white : Color.gray;
    }

    private void OnShowGrantedSkills()
    {
        var card = currentCard;
        Hide();
        if (card == null) return;
        GrantedSkillListUI.Show(card);
    }

    private async void OnUseZoneSkill()
    {
        if (skillButtonListsGrave)
        {
            List<CardController> cards = CollectActivatableGraveCards();
            Hide();
            if (cards.Count > 0 && GraveViewUI.Instance != null)
                GraveViewUI.Instance.ShowActivatableCards(cards);
            return;
        }

        Hide();
        if (currentCard == null) return;

        isUsingSkill = true;
        try
        {
            await currentCard.UseZoneActivatedSkill();
        }
        finally
        {
            isUsingSkill = false;
        }
    }

    // 押した墓地のカード一覧を開く
    private void OnCheckGrave()
    {
        Hide();
        if (currentCard == null || GraveViewUI.Instance == null)
            return;

        bool isPlayerGrave = currentCard.transform.parent == BattleManager.Instance.pGraveZone;
        GraveViewUI.Instance.ShowGraveUI(isPlayerGrave);
    }
}
