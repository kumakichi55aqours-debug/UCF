using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GraveViewUI : MonoBehaviour
{
    public static GraveViewUI Instance { get; private set; }

    [Header("UI要素")]
    [SerializeField] private GameObject graveUI; // 墓地リストパネル
    [SerializeField] private Transform graveContent; // ScrollViewのContent
    [SerializeField] private Button closeButton; // 閉じるボタン

    [Header("カードプレビュー用Prefab")]
    [SerializeField] private CardController cardPreviewPrefab;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        graveUI.SetActive(false);
        closeButton.onClick.AddListener(HideGraveUI);
    }

    public static void HideIfAny()
    {
        if (Instance != null)
            Instance.HideGraveUI();
    }

    /// <summary>
    /// 墓地のカードリストを表示
    /// </summary>
    /// <param name="isPlayerGrave">trueならプレイヤーの墓地、falseなら敵の墓地</param>
    public void ShowGraveUI(bool isPlayerGrave)
    {
        graveUI.SetActive(true);
        ClearGraveUIDisplay();

        if (isPlayerGrave)
        {
            ShowCardsInZone(BattleManager.Instance.pGraveZone);
            return;
        }

        Transform enemyGrave = BattleManager.Instance.eGraveZone;
        if (enemyGrave != null && enemyGrave.childCount > 0)
        {
            ShowCardsInZone(enemyGrave);
            return;
        }

        // オンラインで場のカードがまだ無いときは、受信済みの相手墓地を出す
        if (OnlineSyncManager.Instance != null && OnlineSyncManager.Instance.IsOnlineMode)
        {
            foreach (DeckCard deckCard in OnlineSyncManager.Instance.GetOpponentGrave())
            {
                if (deckCard == null)
                    continue;

                CardController preview = Instantiate(cardPreviewPrefab, graveContent);
                preview.Init(deckCard.カード種類, deckCard.カードID);
            }
        }
    }

    void ShowCardsInZone(Transform graveZone)
    {
        if (graveZone == null)
            return;

        foreach (CardController card in graveZone.GetComponentsInChildren<CardController>())
        {
            if (card == null)
                continue;

            CardController preview = Instantiate(cardPreviewPrefab, graveContent);
            preview.Init(card.cardKind, card.cardID);
        }
    }

    /// <summary>
    /// 墓地から今発動できるカードだけを、確認一覧と同じ形で出す。
    /// 見た目は複製で、クリック先は墓地にある本体。
    /// </summary>
    public void ShowActivatableCards(IReadOnlyList<CardController> cards)
    {
        graveUI.SetActive(true);
        ClearGraveUIDisplay();
        if (cards == null)
            return;

        for (int i = 0; i < cards.Count; i++)
        {
            CardController card = cards[i];
            if (card == null)
                continue;

            CardController preview = Instantiate(cardPreviewPrefab, graveContent);
            preview.Init(card.cardKind, card.cardID);
            preview.BindGraveSkillChoice(card);
        }
    }

    /// <summary>
    /// 墓地のカードリストをクリア
    /// </summary>
    private void ClearGraveUIDisplay()
    {
        foreach (Transform child in graveContent)
        {
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// 墓地パネルを閉じる
    /// </summary>
    public void HideGraveUI()
    {
        graveUI.SetActive(false);
        CardContextMenu.HideMenuIfAny();
    }
}
