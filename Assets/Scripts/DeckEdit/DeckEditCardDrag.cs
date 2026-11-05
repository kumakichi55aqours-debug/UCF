using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// デッキ編集のカードを左ドラッグで持つ。
/// デッキ内は並び替え、カードリストへ落とすとその1枚を抜き、リストからデッキへ落とすと1枚増やす。
/// リスト上の横ドラッグは、これまで通り一覧のスクロールに渡す。
/// </summary>
public class DeckEditCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static bool IsDragging { get; private set; }

    const float HeldScale = 1.05f;
    const float HeldAlpha = 0.92f;

    CardController card;
    RectTransform held;
    Transform placeholder;
    ScrollRect scroll;
    CanvasGroup heldGroup;
    float heldAlpha = 1f;
    int originIndex;
    bool fromDeck;
    bool dragging;
    bool scrolling;
    bool holdingSelf;

    void Awake()
    {
        card = GetComponent<CardController>();
    }

    void OnDestroy()
    {
        if (!dragging)
            return;

        dragging = false;
        IsDragging = false;
        if (placeholder != null)
            Destroy(placeholder.gameObject);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (dragging || scrolling || IsDragging || eventData == null)
            return;
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        if (card == null || DeckEditManager.Instance == null)
            return;

        fromDeck = card.zoneType == DeckEditCustomZone.デッキリスト;
        if (fromDeck)
        {
            if (card.transform.parent != DeckEditManager.Instance.deckZone)
                return;

            originIndex = card.transform.GetSiblingIndex();
            Hold(card.transform as RectTransform, true);
            ShowPlaceholder(originIndex);
            BeginHold(eventData);
            return;
        }

        // リストは横スクロール。真横だけ一覧を動かし、デッキへ運ぶ動きはカードを持つ。
        Vector2 delta = eventData.position - eventData.pressPosition;
        scroll = GetComponentInParent<ScrollRect>();
        if (scroll != null && Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 2f)
        {
            scrolling = true;
            scroll.OnBeginDrag(eventData);
            return;
        }

        if (card.cardData == null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        CardController ghost = DeckEditManager.Instance.CreateLooseCard(card.cardData, canvas.transform);
        if (ghost == null)
            return;

        Hold(ghost.transform as RectTransform, false);
        BeginHold(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (scrolling)
        {
            scroll.OnDrag(eventData);
            return;
        }

        if (!dragging || eventData == null)
            return;

        Follow(eventData);
        UpdatePlaceholder(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (scrolling)
        {
            scrolling = false;
            if (scroll != null)
                scroll.OnEndDrag(eventData);
            scroll = null;
            return;
        }

        if (!dragging)
            return;

        DeckEditManager manager = DeckEditManager.Instance;
        int slot = placeholder != null && placeholder.gameObject.activeSelf
            ? placeholder.GetSiblingIndex()
            : originIndex;
        DeckDropZone zone = ZoneUnder(manager, eventData);
        CardDBBase data = card != null ? card.cardData : null;
        bool wasDeck = fromDeck;
        int from = originIndex;

        ReleaseHold();

        if (manager == null)
            return;

        if (wasDeck)
            manager.FinishDeckDrag(from, zone, slot);
        else if (zone == DeckDropZone.Deck)
            manager.AddCardToDeck(data, slot);
    }

    void BeginHold(PointerEventData eventData)
    {
        dragging = true;
        IsDragging = true;
        if (CardDetailManager.Instance != null)
            CardDetailManager.Instance.HideDetail();
        Follow(eventData);
    }

    void Hold(RectTransform target, bool self)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        held = target;
        holdingSelf = self;
        if (canvas != null && target.parent != canvas.transform)
            target.SetParent(canvas.transform, true);

        target.SetAsLastSibling();
        target.localScale = Vector3.one * HeldScale;
        heldGroup = target.GetComponent<CanvasGroup>();
        if (heldGroup != null)
        {
            heldAlpha = heldGroup.alpha;
            heldGroup.alpha = HeldAlpha;
            heldGroup.blocksRaycasts = false;
        }
    }

    void Follow(PointerEventData eventData)
    {
        if (held == null)
            return;

        Canvas canvas = held.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        Camera cam = eventData.pressEventCamera;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvas.transform as RectTransform, eventData.position, cam, out Vector3 world))
            held.position = world;
    }

    void UpdatePlaceholder(PointerEventData eventData)
    {
        DeckEditManager manager = DeckEditManager.Instance;
        if (manager == null)
            return;

        bool overDeck = Over(manager.deckZone as RectTransform, eventData);
        if (fromDeck)
        {
            if (!overDeck && Over(ListArea(manager), eventData))
            {
                HidePlaceholder();
                return;
            }

            int maxSlot = Mathf.Max(0, manager.DeckCount - 1);
            ShowPlaceholder(overDeck ? SlotAt(manager, eventData, maxSlot) : originIndex);
            return;
        }

        if (overDeck && manager.CanAdd(card.cardData))
            ShowPlaceholder(SlotAt(manager, eventData, manager.DeckCount));
        else
            HidePlaceholder();
    }

    void ShowPlaceholder(int index)
    {
        Transform zone = DeckEditManager.Instance.deckZone;
        if (zone == null)
            return;

        if (placeholder == null)
        {
            var slot = new GameObject("DeckInsertSlot", typeof(RectTransform));
            placeholder = slot.transform;
            placeholder.SetParent(zone, false);
        }
        else if (placeholder.parent != zone)
        {
            placeholder.SetParent(zone, false);
        }

        placeholder.gameObject.SetActive(true);
        int last = Mathf.Max(0, zone.childCount - 1);
        placeholder.SetSiblingIndex(Mathf.Clamp(index, 0, last));
    }

    void HidePlaceholder()
    {
        if (placeholder != null)
            placeholder.gameObject.SetActive(false);
    }

    void ReleaseHold()
    {
        dragging = false;
        IsDragging = false;

        if (placeholder != null)
        {
            placeholder.SetParent(null, false);
            Destroy(placeholder.gameObject);
            placeholder = null;
        }

        if (held == null)
            return;

        if (holdingSelf)
        {
            held.gameObject.SetActive(false);
            held.SetParent(null, false);
            Destroy(held.gameObject);
        }
        else
        {
            if (heldGroup != null)
            {
                heldGroup.alpha = heldAlpha;
                heldGroup.blocksRaycasts = true;
            }

            held.SetParent(null, false);
            held.gameObject.SetActive(false);
            Destroy(held.gameObject);
        }

        held = null;
        heldGroup = null;
    }

    static DeckDropZone ZoneUnder(DeckEditManager manager, PointerEventData eventData)
    {
        if (manager == null)
            return DeckDropZone.None;
        if (Over(manager.deckZone as RectTransform, eventData))
            return DeckDropZone.Deck;
        if (Over(ListArea(manager), eventData))
            return DeckDropZone.CardList;
        return DeckDropZone.None;
    }

    static RectTransform ListArea(DeckEditManager manager)
    {
        if (manager.cardListContent == null)
            return null;

        ScrollRect listScroll = manager.cardListContent.GetComponentInParent<ScrollRect>();
        if (listScroll != null && listScroll.viewport != null)
            return listScroll.viewport;

        return manager.cardListContent as RectTransform;
    }

    static bool Over(RectTransform area, PointerEventData eventData)
    {
        return area != null
            && eventData != null
            && RectTransformUtility.RectangleContainsScreenPoint(area, eventData.position, eventData.pressEventCamera);
    }

    // デッキの格子は左上から横並び。ポインタのセルが行き先。
    static int SlotAt(DeckEditManager manager, PointerEventData eventData, int maxIndex)
    {
        if (maxIndex < 0)
            return 0;

        var zone = manager.deckZone as RectTransform;
        var grid = manager.deckZone != null ? manager.deckZone.GetComponent<GridLayoutGroup>() : null;
        if (zone == null || grid == null || eventData == null)
            return maxIndex;

        Camera cam = eventData.pressEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, eventData.position, cam, out Vector2 local))
            return maxIndex;

        Rect rect = zone.rect;
        float x = local.x - rect.xMin - grid.padding.left;
        float y = rect.yMax - local.y - grid.padding.top;
        float strideX = Mathf.Max(1f, grid.cellSize.x + grid.spacing.x);
        float strideY = Mathf.Max(1f, grid.cellSize.y + grid.spacing.y);
        int columns = ColumnCount(grid, rect);

        int col = Mathf.Clamp(Mathf.FloorToInt(x / strideX), 0, columns - 1);
        int row = Mathf.Max(0, Mathf.FloorToInt(y / strideY));
        float intoCell = x - col * strideX;
        int index = row * columns + col;
        if (intoCell > grid.cellSize.x * 0.5f)
            index++;

        return Mathf.Clamp(index, 0, maxIndex);
    }

    static int ColumnCount(GridLayoutGroup grid, Rect rect)
    {
        if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
            return Mathf.Max(1, grid.constraintCount);

        float stride = grid.cellSize.x + grid.spacing.x;
        int columns = Mathf.FloorToInt((rect.width - grid.padding.horizontal + grid.spacing.x + 0.001f) / Mathf.Max(1f, stride));
        return Mathf.Max(1, columns);
    }
}
