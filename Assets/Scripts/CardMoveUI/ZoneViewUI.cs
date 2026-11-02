using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 墓地と異次元の確認一覧。どちらのゾーンも同じパネルに出す。
/// </summary>
public class ZoneViewUI : MonoBehaviour
{
    public static ZoneViewUI Instance { get; private set; }

    [Header("UI要素")]
    [SerializeField] private GameObject zoneUI;
    [SerializeField] private Transform zoneContent;
    [SerializeField] private Button closeButton;

    [Header("カードプレビュー用Prefab")]
    [SerializeField] private CardController cardPreviewPrefab;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        zoneUI.SetActive(false);
        closeButton.onClick.AddListener(Hide);
    }

    public static void HideIfAny()
    {
        if (Instance != null)
            Instance.Hide();
    }

    /// <summary>
    /// 押されたゾーンのカードを一覧にする。オンラインの相手ゾーンで表示が無いときは、同期済みの一覧を使う。
    /// </summary>
    public void Show(Transform zone)
    {
        var battle = BattleManager.Instance;
        if (battle == null || zone == null)
            return;

        IReadOnlyList<DeckCard> fallback = null;
        var sync = OnlineSyncManager.Instance;
        if (sync != null && sync.IsOnlineMode)
        {
            if (zone == battle.eGraveZone)
                fallback = sync.GetOpponentGrave();
            else if (zone == battle.eDimensionZone)
                fallback = sync.GetOpponentDimension();
        }

        ShowPile(zone, fallback);
    }

    void ShowPile(Transform zone, IReadOnlyList<DeckCard> onlineFallback)
    {
        zoneUI.SetActive(true);
        PopupDrag.Use(zoneUI);
        ClearDisplay();

        if (zone != null && zone.childCount > 0)
        {
            ShowCardsInZone(zone);
            return;
        }

        if (onlineFallback == null)
            return;

        foreach (DeckCard deckCard in onlineFallback)
        {
            if (deckCard == null)
                continue;

            CardController preview = Instantiate(cardPreviewPrefab, zoneContent);
            preview.Init(deckCard.カード種類, deckCard.カードID);
        }
    }

    void ShowCardsInZone(Transform zone)
    {
        if (zone == null)
            return;

        foreach (CardController card in zone.GetComponentsInChildren<CardController>())
        {
            if (card == null)
                continue;

            CardController preview = Instantiate(cardPreviewPrefab, zoneContent);
            preview.Init(card.cardKind, card.cardID);
        }
    }

    /// <summary>
    /// 墓地から今発動できるカードだけを、確認一覧と同じ形で出す。
    /// 見た目は複製で、クリック先は墓地にある本体。
    /// </summary>
    public void ShowActivatableCards(IReadOnlyList<CardController> cards)
    {
        zoneUI.SetActive(true);
        PopupDrag.Use(zoneUI);
        ClearDisplay();
        if (cards == null)
            return;

        for (int i = 0; i < cards.Count; i++)
        {
            CardController card = cards[i];
            if (card == null)
                continue;

            CardController preview = Instantiate(cardPreviewPrefab, zoneContent);
            preview.Init(card.cardKind, card.cardID);
            preview.BindGraveSkillChoice(card);
        }
    }

    void ClearDisplay()
    {
        foreach (Transform child in zoneContent)
            Destroy(child.gameObject);
    }

    public void Hide()
    {
        zoneUI.SetActive(false);
        CardContextMenu.HideMenuIfAny();
    }
}
