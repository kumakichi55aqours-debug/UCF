using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardSelectionUI : MonoBehaviour
{
    public static CardSelectionUI Instance { get; private set; }
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform content;
    [SerializeField] private Button confirmButton;
    [SerializeField] private CardController previewPrefab;

    private List<SelectionItem> selectedItems = new();
    private int maxSelect;
    private TaskCompletionSource<List<SelectionItem>> tcs;
    private bool rejectSameKind;
    private bool rejectSameType;
    // 真なら、1枚以上選べば上限に届かなくても確定できる。
    private bool allowFewer;
    // 選ばれているカードを見て、この候補を選べなくする。真なら押せない。
    private Func<IReadOnlyList<SelectionItem>, SelectionItem, bool> rejectWhen;
    private readonly List<ShownCard> shownCards = new();

    class ShownCard
    {
        public SelectionItem item;
        public Button button;
        public CanvasGroup group;
    }

    void Awake()
    {
        Instance = this;
        panel.SetActive(false);
    }

    public Transform Content => content;

    public async Task<List<SelectionItem>> ShowAsync(
        IEnumerable<CardController> existing,
        IEnumerable<DeckCard> deckItems,
        int count,
        bool rejectSameKind = false,
        bool letCpuChoose = true,
        bool rejectSameType = false,
        Func<IReadOnlyList<SelectionItem>, SelectionItem, bool> rejectWhen = null,
        bool allowFewer = false)
    {
        // CPUの効果でも、選ぶ側が人間のときはUIを出す。
        if (letCpuChoose && SkillContext.IsOfflineCpuResolving)
            return PickRandom(existing, deckItems, count, rejectSameKind, rejectSameType);

        this.rejectSameKind = rejectSameKind;
        this.rejectSameType = rejectSameType;
        this.rejectWhen = rejectWhen;
        this.allowFewer = allowFewer;
        shownCards.Clear();
        maxSelect = Mathf.Max(1, count);
        selectedItems.Clear();
        panel.SetActive(true);
        PopupDrag.Use(panel);

        // クリア
        foreach (Transform c in content)
            Destroy(c.gameObject);

        confirmButton.interactable = false;
        confirmButton.onClick.RemoveAllListeners();
        confirmButton.onClick.AddListener(OnConfirm);

        // 1) 実在カードを表示
        foreach (var card in existing)
        {
            var go = Instantiate(previewPrefab.gameObject, content);
            var ctrl = go.GetComponent<CardController>();
            ctrl.Init(card.cardKind, card.cardID);
            ctrl.model = card.model;

            // ゾーンラベルを表示する処理
            var labelRoot = go.transform.Find("ZoneLabel")?.gameObject;
            if (labelRoot != null)
            {
                // ZoneLabel をアクティブに
                labelRoot.SetActive(true);

                // その子要素の ZoneText を探す
                var textTransform = labelRoot.transform.Find("ZoneText");
                if (textTransform != null)
                {
                    var txt = textTransform.GetComponent<TextMeshProUGUI>();
                    if (txt != null)
                    {
                        txt.text = GetZoneName(card.transform.parent);
                    }
                }
            }
            // ゾーンラベル表示処理終了

            var item = new SelectionItem
            {
                source = SelectionItem.SourceType.Existing,
                existingCard = card
            };
            HookButton(go, item);
            TrackShown(go, item);
        }

        // 2) デッキのカードを表示
        foreach (var d in deckItems)
        {
            var go = Instantiate(previewPrefab.gameObject, content);
            var ctrl = go.GetComponent<CardController>();
            ctrl.Init(d.カード種類, d.カードID);

            // ゾーンラベルをデッキ用に表示
            var labelRoot = go.transform.Find("ZoneLabel")?.gameObject;
            if (labelRoot != null)
            {
                labelRoot.SetActive(true);
                var textTransform = labelRoot.transform.Find("ZoneText");
                if (textTransform != null)
                {
                    var txt = textTransform.GetComponent<TextMeshProUGUI>();
                    if (txt != null) txt.text = "デッキ";
                }
            }
            // ゾーンラベル表示処理終了

            var item = new SelectionItem
            {
                source = SelectionItem.SourceType.Deck,
                deckData = d
            };
            HookButton(go, item);
            TrackShown(go, item);
        }

        RefreshPickLocks();
        confirmButton.interactable = CanConfirm();

        tcs = new TaskCompletionSource<List<SelectionItem>>();
        try
        {
            var result = await tcs.Task;
            panel.SetActive(false);
            return result;
        }
        finally
        {
            rejectSameKind = false;
            rejectSameType = false;
            rejectWhen = null;
            allowFewer = false;
            shownCards.Clear();
        }
    }

    /// <summary>
    /// 渡された候補をそのまま並べ、選ばれた元リストの番号を返す。
    /// 呼び出し側が自分か相手かを決める。ここでは無作為に置き換えない。
    /// </summary>
    public async Task<List<int>> ShowIndexedAsync(IReadOnlyList<SelectionItem> items, int count, bool allowFewer = false)
    {
        if (items == null || items.Count == 0)
            return new List<int>();

        rejectSameKind = false;
        rejectSameType = false;
        rejectWhen = null;
        this.allowFewer = allowFewer;
        shownCards.Clear();
        int take = Mathf.Min(Mathf.Max(1, count), items.Count);
        maxSelect = take;
        selectedItems.Clear();
        panel.SetActive(true);
        PopupDrag.Use(panel);

        List<SelectionItem> result = null;
        var indexOf = new Dictionary<SelectionItem, int>();
        try
        {
            foreach (Transform child in content)
                Destroy(child.gameObject);

            confirmButton.interactable = false;
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);

            for (int i = 0; i < items.Count; i++)
            {
                var sourceItem = items[i];
                if (sourceItem == null)
                    continue;

                var go = Instantiate(previewPrefab.gameObject, content);
                var ctrl = go.GetComponent<CardController>();
                string label = "候補";

                if (sourceItem.source == SelectionItem.SourceType.Existing && sourceItem.existingCard != null)
                {
                    var card = sourceItem.existingCard;
                    ctrl.Init(card.cardKind, card.cardID);
                    ctrl.model = card.model;
                    string zoneName = GetZoneName(card.transform.parent);
                    if (!string.IsNullOrEmpty(zoneName))
                        label = zoneName;
                }
                else if (sourceItem.deckData != null)
                {
                    ctrl.Init(sourceItem.deckData.カード種類, sourceItem.deckData.カードID);
                    label = "デッキ";
                }

                SetZoneLabel(go, label);

                var shown = new SelectionItem
                {
                    source = sourceItem.source,
                    existingCard = sourceItem.existingCard,
                    deckData = sourceItem.deckData
                };
                indexOf[shown] = i;
                HookButton(go, shown);
            }

            if (indexOf.Count <= take && !(allowFewer && indexOf.Count > 1))
            {
                return new List<int>(indexOf.Values);
            }

            tcs = new TaskCompletionSource<List<SelectionItem>>();
            result = await tcs.Task;
        }
        finally
        {
            panel.SetActive(false);
            this.allowFewer = false;
        }

        var indices = new List<int>();
        if (result == null)
            return indices;

        foreach (var item in result)
        {
            if (item != null && indexOf.TryGetValue(item, out int index))
                indices.Add(index);
        }

        return indices;
    }

    static void SetZoneLabel(GameObject go, string label)
    {
        var labelRoot = go.transform.Find("ZoneLabel")?.gameObject;
        if (labelRoot == null)
            return;

        labelRoot.SetActive(true);
        var textTransform = labelRoot.transform.Find("ZoneText");
        if (textTransform == null)
            return;

        var txt = textTransform.GetComponent<TextMeshProUGUI>();
        if (txt != null)
            txt.text = label;
    }

    static List<SelectionItem> PickRandom(
        IEnumerable<CardController> existing,
        IEnumerable<DeckCard> deckItems,
        int count,
        bool rejectSameKind = false,
        bool rejectSameType = false)
    {
        var pool = new List<SelectionItem>();
        if (existing != null)
        {
            foreach (var card in existing)
            {
                if (card == null) continue;
                pool.Add(new SelectionItem
                {
                    source = SelectionItem.SourceType.Existing,
                    existingCard = card
                });
            }
        }

        if (deckItems != null)
        {
            foreach (var deck in deckItems)
            {
                if (deck == null) continue;
                pool.Add(new SelectionItem
                {
                    source = SelectionItem.SourceType.Deck,
                    deckData = deck
                });
            }
        }

        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            var swap = pool[i];
            pool[i] = pool[j];
            pool[j] = swap;
        }

        if (rejectSameKind)
        {
            var unique = new List<SelectionItem>();
            var seen = new HashSet<long>();
            foreach (var item in pool)
            {
                if (!item.TryGetIdentity(out CardKind kind, out int id))
                    continue;
                long key = ((long)kind << 32) | (uint)id;
                if (!seen.Add(key))
                    continue;
                unique.Add(item);
            }
            pool = unique;
        }

        if (rejectSameType)
        {
            var uniqueType = new List<SelectionItem>();
            var seenType = new HashSet<int>();
            foreach (var item in pool)
            {
                if (!item.TryGetBattlerType(out CardTypeList type))
                    continue;
                if (!seenType.Add((int)type))
                    continue;
                uniqueType.Add(item);
            }
            pool = uniqueType;
        }

        int take = Mathf.Min(Mathf.Max(0, count), pool.Count);
        if (take < pool.Count)
            pool.RemoveRange(take, pool.Count - take);

        return pool;
    }

    /// <summary>
    /// めくったカードを上から順に出す。
    /// choose が真のときは、選べるカードのうち count 枚を選ばせる。
    /// 偽のときは確認だけさせて、選べるカードをすべて返す。
    /// </summary>
    public async Task<List<SelectionItem>> ShowOrderedDeckAsync(
        IReadOnlyList<DeckCard> cards,
        IReadOnlyList<bool> selectable,
        int count,
        bool choose)
    {
        var pool = new List<DeckCard>();
        if (cards != null)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                if (IsSelectable(selectable, i))
                    pool.Add(cards[i]);
            }
        }

        int take = Mathf.Min(Mathf.Max(0, count), pool.Count);
        if (SkillContext.IsOfflineCpuResolving)
            return PickRandom(null, pool, choose ? take : pool.Count);

        if (cards == null || cards.Count == 0)
            return new List<SelectionItem>();

        rejectSameKind = false;
        rejectSameType = false;
        rejectWhen = null;
        allowFewer = false;
        shownCards.Clear();
        maxSelect = choose ? Mathf.Max(1, take) : Mathf.Max(1, pool.Count);
        selectedItems.Clear();
        panel.SetActive(true);
        PopupDrag.Use(panel);

        foreach (Transform child in content)
            Destroy(child.gameObject);

        confirmButton.onClick.RemoveAllListeners();
        confirmButton.onClick.AddListener(OnConfirm);
        confirmButton.interactable = !choose || take == 0;

        for (int i = 0; i < cards.Count; i++)
        {
            var deckCard = cards[i];
            if (deckCard == null) continue;

            bool canSelect = IsSelectable(selectable, i);
            var go = Instantiate(previewPrefab.gameObject, content);
            var ctrl = go.GetComponent<CardController>();
            ctrl.Init(deckCard.カード種類, deckCard.カードID);

            var labelRoot = go.transform.Find("ZoneLabel")?.gameObject;
            if (labelRoot != null)
            {
                labelRoot.SetActive(true);
                var textTransform = labelRoot.transform.Find("ZoneText");
                if (textTransform != null)
                {
                    var txt = textTransform.GetComponent<TextMeshProUGUI>();
                    if (txt != null)
                        txt.text = canSelect ? "デッキ" : "対象外";
                }
            }

            var item = new SelectionItem
            {
                source = SelectionItem.SourceType.Deck,
                deckData = deckCard
            };

            if (!canSelect || !choose)
            {
                var button = go.GetComponent<Button>();
                if (button != null)
                    button.interactable = false;

                if (!canSelect)
                {
                    var group = go.GetComponent<CanvasGroup>();
                    if (group == null)
                        group = go.AddComponent<CanvasGroup>();
                    group.alpha = 0.45f;
                    group.blocksRaycasts = false;
                }
                else
                {
                    selectedItems.Add(item);
                    var panelGO = go.transform.Find("CanUseMovePanel");
                    if (panelGO != null)
                        panelGO.gameObject.SetActive(true);
                }
            }
            else
            {
                HookButton(go, item);
            }
        }

        if (!choose)
            confirmButton.interactable = true;

        tcs = new TaskCompletionSource<List<SelectionItem>>();
        var result = await tcs.Task;
        panel.SetActive(false);
        return result;
    }

    static bool IsSelectable(IReadOnlyList<bool> selectable, int index)
    {
        return selectable == null || index >= selectable.Count || selectable[index];
    }

    private string GetZoneName(Transform parent)
    {
        var bm = BattleManager.Instance;
        if (parent == bm.pBattlerZone || bm.pTacticsZone || bm.pFieldZone) return "自ゾーン";
        else if (parent == bm.eBattlerZone || bm.eTacticsZone || bm.eFieldZone) return "敵ゾーン";
        else if (parent == bm.pHandZone) return "手札";
        else if (parent == bm.pGraveZone) return "墓地";
        else if (parent == bm.pDimensionZone) return "異次元";
        else if (parent == bm.eHandZone) return "敵手札";
        else if (parent == bm.eGraveZone) return "敵墓地";
        else if (parent == bm.eDimensionZone) return "敵異次元";
        else return "";
    }

    private void HookButton(GameObject go, SelectionItem item)
    {
        var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
        btn.onClick.AddListener(() => OnClick(item, go));
    }

    private void OnClick(SelectionItem item, GameObject go)
    {
        if (selectedItems.Contains(item))
        {
            selectedItems.Remove(item);
            var panelGO = go.transform.Find("CanUseMovePanel");
            if (panelGO != null) panelGO.gameObject.SetActive(false);
        }
        else if (selectedItems.Count < maxSelect && !IsBlocked(item))
        {
            selectedItems.Add(item);
            var panelGO = go.transform.Find("CanUseMovePanel");
            if (panelGO != null) panelGO.gameObject.SetActive(true);
        }
        RefreshPickLocks();
        confirmButton.interactable = CanConfirm();
    }

    void TrackShown(GameObject go, SelectionItem item)
    {
        if (go == null || !LocksSelection())
            return;

        shownCards.Add(new ShownCard
        {
            item = item,
            button = go.GetComponent<Button>(),
            group = go.GetComponent<CanvasGroup>()
        });
    }

    bool LocksSelection()
    {
        return rejectSameKind || rejectSameType || rejectWhen != null;
    }

    bool IsBlocked(SelectionItem item)
    {
        return IsBlockedBySameKind(item) || IsBlockedBySameType(item) || IsRejected(item);
    }

    bool IsBlockedBySameKind(SelectionItem item)
    {
        if (!rejectSameKind || item == null)
            return false;

        foreach (var selected in selectedItems)
        {
            if (selected != item && selected.IsSameCard(item))
                return true;
        }
        return false;
    }

    bool IsBlockedBySameType(SelectionItem item)
    {
        if (!rejectSameType || item == null || !item.TryGetBattlerType(out CardTypeList type))
            return false;

        foreach (var selected in selectedItems)
        {
            if (selected == item || !selected.TryGetBattlerType(out CardTypeList selectedType))
                continue;
            if (selectedType == type)
                return true;
        }
        return false;
    }

    bool IsRejected(SelectionItem item)
    {
        return rejectWhen != null && item != null && rejectWhen(selectedItems, item);
    }

    bool AllShownBlocked()
    {
        bool any = false;
        foreach (var shown in shownCards)
        {
            if (shown.item == null)
                continue;
            any = true;
            if (!IsBlocked(shown.item))
                return false;
        }
        return any;
    }

    void RefreshPickLocks()
    {
        if (!LocksSelection())
            return;

        foreach (var shown in shownCards)
        {
            if (shown.button == null)
                continue;

            bool selected = selectedItems.Contains(shown.item);
            bool blocked = !selected && IsBlocked(shown.item);
            shown.button.interactable = !blocked;

            if (!blocked && shown.group == null)
                continue;

            var group = shown.group;
            if (group == null)
            {
                group = shown.button.GetComponent<CanvasGroup>();
                if (group == null)
                    group = shown.button.gameObject.AddComponent<CanvasGroup>();
                shown.group = group;
            }
            group.alpha = blocked ? 0.45f : 1f;
        }
    }

    bool CanConfirm()
    {
        if (selectedItems.Count == maxSelect)
            return true;
        if (allowFewer && selectedItems.Count > 0)
            return true;
        // 最初から全部押せないときは、空のまま閉じて効果を終わらせる。
        if (selectedItems.Count == 0 && AllShownBlocked())
            return true;
        // 異なるタイプは2体とも押す。候補が1体でも、そこを自動では確定しない。
        if (rejectSameType)
            return false;
        if (!rejectSameKind || selectedItems.Count == 0 || selectedItems.Count >= maxSelect)
            return false;

        foreach (var shown in shownCards)
        {
            if (shown.item == null || selectedItems.Contains(shown.item))
                continue;
            if (!IsBlockedBySameKind(shown.item))
                return false;
        }
        return true;
    }

    private void OnConfirm()
    {
        tcs.TrySetResult(new List<SelectionItem>(selectedItems));
    }
}
