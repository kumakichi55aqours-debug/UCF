using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class DeckEditManager : MonoBehaviour
{
    public static DeckEditManager Instance { get; private set; }

    const string NewDeckName = "新規デッキ";
    const string SelectedDeckKey = "SelectedDeckName";
    const string DisableShufflePrefKey = "SolitaireDisableDeckShuffle";

    static List<CardDBBase> returnDeck;
    static string returnDeckName;
    static bool returnDeckModified;

    [SerializeField] CardController cardPrefab;

    [Header("Zones")]
    public Transform deckZone;
    public Transform cardListContent;

    [Header("UI Elements")]
    public TMP_InputField searchInput;
    public TMP_Dropdown cardKindDropdown;
    public TMP_Dropdown cardTypeDropdown;
    public TMP_Dropdown cardCostDropdown;
    public TMP_Dropdown cardElementDropdown;
    public TMP_Dropdown cardElementTargetDropdown;
    public Button searchButton;
    public Button clearFilterButton;
    public Button saveButton;
    public Button sortButton;
    public Button clearButton;
    public Button deleteDeckButton;
    public Button solitaireButton;
    public Toggle shuffleOffToggle;
    public TMP_InputField deckNameInput;
    public TMP_Dropdown deckSelectDropdown;

    public List<CardDBBase> availableCards = new();
    public List<CardDBBase> currentDeck = new();

    string deckDirectory;
    string currentDeckName = "デフォルト";
    bool isDeckModified;
    bool solitaireStarting;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        deckDirectory = Path.Combine(Application.streamingAssetsPath, "Decks");
        Directory.CreateDirectory(deckDirectory);
    }

    void Start()
    {
        LoadAvailableCards();
        InitializeUI();
        RefreshDeckList();
        LoadInitialDeck();
        SortCardList();
    }

    void InitializeUI()
    {
        Bind(clearButton, ClearDeck);
        Bind(saveButton, SaveDeck);
        Bind(deleteDeckButton, DeleteCurrentDeck);

        if (deckSelectDropdown != null)
        {
            deckSelectDropdown.onValueChanged.RemoveAllListeners();
            deckSelectDropdown.onValueChanged.AddListener(OnDeckSelected);
        }

        if (deckNameInput != null)
        {
            deckNameInput.SetTextWithoutNotify(currentDeckName);
            deckNameInput.onValueChanged.RemoveAllListeners();
            deckNameInput.onValueChanged.AddListener(OnDeckNameChanged);
        }

        EnsureClearFilterButton();
        Bind(clearFilterButton, ClearFilters);
        Bind(solitaireButton, StartSolitaire);
        if (shuffleOffToggle != null)
        {
            shuffleOffToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(DisableShufflePrefKey, 0) == 1);
            shuffleOffToggle.onValueChanged.AddListener(SaveShuffleOff);
        }
    }

    static void Bind(Button button, UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    void LoadAvailableCards()
    {
        availableCards.Clear();
        availableCards.AddRange(Resources.LoadAll<BattlerDB>("CardDBList/バトラー"));
        availableCards.AddRange(Resources.LoadAll<TacticsDB>("CardDBList/作戦"));
        availableCards.AddRange(Resources.LoadAll<FieldDB>("CardDBList/フィールド"));
    }

    void ShowCards(List<CardDBBase> cards, Transform parent, DeckEditCustomZone zone)
    {
        if (cards == null || parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);

        foreach (CardDBBase cardData in cards)
        {
            CardController card = Instantiate(cardPrefab, parent);
            card.cardData = cardData;
            card.zoneType = zone;
            card.Init(cardData.cardKind, cardData.cardId);
            card.deckCardView.Show(card.model);
        }
    }

    void ShowDeck()
    {
        ShowCards(currentDeck, deckZone, DeckEditCustomZone.デッキリスト);
    }

    void ShowCardList(List<CardDBBase> cards)
    {
        ShowCards(cards, cardListContent, DeckEditCustomZone.カードリスト);
    }

    public void AddCardToDeck(CardDBBase cardData)
    {
        if (cardData == null)
            return;

        currentDeck.Add(cardData);
        isDeckModified = true;
        ShowDeck();
    }

    public void RemoveCardFromDeck(CardController card)
    {
        if (card == null)
            return;

        currentDeck.Remove(card.cardData);
        isDeckModified = true;
        ShowDeck();
    }

    public void SaveDeck()
    {
        if (deckNameInput != null && !string.IsNullOrWhiteSpace(deckNameInput.text))
            currentDeckName = deckNameInput.text.Trim();

        if (string.IsNullOrEmpty(currentDeckName))
        {
            Debug.LogWarning("デッキ名が入力されていません。");
            return;
        }

        var deckData = new List<DeckCard>(currentDeck.Count);
        foreach (CardDBBase card in currentDeck)
        {
            CardTypeList cardType = card is BattlerDB battler ? battler.type : CardTypeList.None;
            deckData.Add(new DeckCard(card.cardKind, card.cardId, card.cost, card.category, cardType));
        }

        File.WriteAllText(DeckPath(currentDeckName), JsonConvert.SerializeObject(deckData, Formatting.Indented));
        isDeckModified = false;
        RememberSelectedDeck();
        RefreshDeckList();

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    public void LoadDeck()
    {
        if (deckSelectDropdown == null || deckSelectDropdown.options.Count == 0)
            return;

        LoadDeckByName(deckSelectDropdown.options[deckSelectDropdown.value].text);
    }

    public void LoadDeckByName(string deckName)
    {
        if (string.IsNullOrEmpty(deckName))
        {
            Debug.LogWarning("デッキ名が指定されていません。");
            return;
        }

        if (isDeckModified)
            Debug.LogWarning("現在のデッキに未保存の変更があります。");

        currentDeck.Clear();
        string path = DeckPath(deckName);
        if (File.Exists(path))
        {
            List<DeckCard> loadedDeck = JsonConvert.DeserializeObject<List<DeckCard>>(File.ReadAllText(path));
            if (loadedDeck != null)
            {
                foreach (DeckCard deckCard in loadedDeck)
                {
                    CardDBBase cardData = availableCards.Find(c =>
                        c.cardId == deckCard.カードID && c.cardKind == deckCard.カード種類);
                    if (cardData == null)
                        continue;

                    cardData.category = deckCard.カテゴリ;
                    currentDeck.Add(cardData);
                }
            }
        }
        else
        {
            Debug.LogWarning("デッキファイルが存在しません: " + path);
        }

        ApplyDeckName(deckName);
        isDeckModified = false;
        ShowDeck();
        RememberSelectedDeck();
    }

    public void SearchCards()
    {
        string searchTerm = searchInput != null ? searchInput.text.ToLower() : "";
        CardKind selectedKind = cardKindDropdown != null ? (CardKind)cardKindDropdown.value : CardKind.None;
        CardTypeList selectedType = cardTypeDropdown != null ? (CardTypeList)cardTypeDropdown.value : CardTypeList.None;
        ElementList selectedElement = cardElementDropdown != null ? (ElementList)cardElementDropdown.value : ElementList.None;
        int elementTarget = cardElementTargetDropdown != null ? cardElementTargetDropdown.value : 0;

        var filtered = new List<CardDBBase>();
        foreach (CardDBBase card in availableCards)
        {
            if (!MatchesSearchText(card, searchTerm))
                continue;
            if (selectedKind != CardKind.None && card.cardKind != selectedKind)
                continue;
            if (!MatchesType(card, selectedType))
                continue;
            if (!MatchesCost(card))
                continue;
            if (!MatchesElement(card, selectedElement, elementTarget))
                continue;

            filtered.Add(card);
        }

        filtered.Sort(CompareCards);
        ShowCardList(filtered);
    }

    public void ClearFilters()
    {
        if (searchInput != null)
            searchInput.SetTextWithoutNotify("");

        ResetDropdown(cardKindDropdown);
        ResetDropdown(cardTypeDropdown);
        ResetDropdown(cardCostDropdown);
        ResetDropdown(cardElementDropdown);
        ResetDropdown(cardElementTargetDropdown);
        SortCardList();
    }

    static void ResetDropdown(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
            return;

        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
    }

    void EnsureClearFilterButton()
    {
        if (clearFilterButton != null || searchButton == null)
            return;

        GameObject clone = Instantiate(searchButton.gameObject, searchButton.transform.parent);
        clone.name = "ClearFilterButton";
        clearFilterButton = clone.GetComponent<Button>();
        clearFilterButton.onClick = new Button.ButtonClickedEvent();

        RectTransform rect = clone.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(250f, 40f);
        if (cardElementDropdown != null)
        {
            RectTransform elementRow = cardElementDropdown.transform.parent as RectTransform;
            rect.anchorMin = elementRow.anchorMin;
            rect.anchorMax = elementRow.anchorMax;
            rect.pivot = new Vector2(1f, elementRow.pivot.y);
            float elementLeft = elementRow.anchoredPosition.x - elementRow.sizeDelta.x * elementRow.pivot.x;
            rect.anchoredPosition = new Vector2(elementLeft - 16f, elementRow.anchoredPosition.y);
        }

        foreach (TextMeshProUGUI text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = "フィルタークリア";
            text.fontSize = 22f;
            text.rectTransform.sizeDelta = new Vector2(240f, 36f);
        }
    }

    public void SortCards()
    {
        currentDeck.Sort(CompareCards);
        ShowDeck();
    }

    public void SortCardList()
    {
        availableCards.Sort(CompareCards);
        ShowCardList(availableCards);
    }

    public void ClearDeck()
    {
        currentDeck.Clear();
        isDeckModified = true;
        ShowDeck();
    }

    public void DeleteCurrentDeck()
    {
        if (string.IsNullOrEmpty(currentDeckName) || currentDeckName == NewDeckName)
        {
            Debug.LogWarning("削除するデッキが選択されていません。");
            return;
        }

        string path = DeckPath(currentDeckName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("削除するデッキファイルが存在しません: " + path);
            return;
        }

        try
        {
            File.Delete(path);
            currentDeck.Clear();
            ApplyDeckName(NewDeckName);
            isDeckModified = false;
            ShowDeck();
            RefreshDeckList();
            RememberSelectedDeck();

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"デッキ削除エラー: {ex.Message}");
        }
    }

    public int GetDeckCount()
    {
        return currentDeck.Count;
    }

    public bool IsValidDeck()
    {
        return currentDeck.Count >= 20 && currentDeck.Count <= 30;
    }

    static int CompareCards(CardDBBase a, CardDBBase b)
    {
        int kind = a.cardKind.CompareTo(b.cardKind);
        if (kind != 0)
            return kind;

        return a.cardId.CompareTo(b.cardId);
    }

    // ドロップダウンの0番は「全て」。1番以降の表示は 0, 1, 2... なので、値そのものはコストではない。
    bool MatchesCost(CardDBBase card)
    {
        if (cardCostDropdown == null || cardCostDropdown.value <= 0)
            return true;

        string label = cardCostDropdown.options[cardCostDropdown.value].text;
        if (int.TryParse(label, out int cost))
            return card.cost == cost;

        return card.cost == cardCostDropdown.value - 1;
    }

    static bool MatchesSearchText(CardDBBase card, string searchTerm)
    {
        if (string.IsNullOrEmpty(searchTerm))
            return true;

        if (ContainsText(card.cardName, searchTerm) || ContainsText(card.skillDescription, searchTerm))
            return true;

        if (card is BattlerDB battler)
            return ContainsText(battler.description1, searchTerm) || ContainsText(battler.description2, searchTerm);

        return false;
    }

    static bool ContainsText(string text, string searchTerm)
    {
        return text != null && text.ToLower().Contains(searchTerm);
    }

    static bool MatchesType(CardDBBase card, CardTypeList selectedType)
    {
        if (selectedType == CardTypeList.None)
            return true;

        return card is BattlerDB battler && battler.type == selectedType;
    }

    static bool MatchesElement(CardDBBase card, ElementList selectedElement, int elementTarget)
    {
        if (selectedElement == ElementList.None)
            return true;
        if (!(card is BattlerDB battler))
            return false;

        if (elementTarget == 1)
            return battler.得意属性 == selectedElement;
        if (elementTarget == 2)
            return battler.弱点属性 == selectedElement;

        return battler.moveElement1 == selectedElement || battler.moveElement2 == selectedElement;
    }

    void LoadInitialDeck()
    {
        if (TryRestoreStashedDeck())
            return;

        string lastSelected = PlayerPrefs.GetString(SelectedDeckKey, "");
        List<string> deckList = GetDeckList();

        if (!string.IsNullOrEmpty(lastSelected) && deckList.Contains(lastSelected))
        {
            LoadDeckByName(lastSelected);
            SelectDeckOption(deckList.IndexOf(lastSelected));
            return;
        }

        if (deckSelectDropdown != null && deckSelectDropdown.options.Count > 0)
            LoadDeckByName(deckSelectDropdown.options[0].text);
        else
            ShowDeck();
    }

    void SelectDeckOption(int index)
    {
        if (deckSelectDropdown != null && index >= 0)
            deckSelectDropdown.SetValueWithoutNotify(index);
    }

    List<string> GetDeckList()
    {
        var deckList = new List<string>();
        if (!Directory.Exists(deckDirectory))
            return deckList;

        foreach (string file in Directory.GetFiles(deckDirectory, "deck_*.json"))
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.StartsWith("deck_"))
                deckList.Add(fileName.Substring("deck_".Length));
        }

        return deckList;
    }

    void RefreshDeckList()
    {
        if (deckSelectDropdown == null)
            return;

        List<string> deckList = GetDeckList();
        deckSelectDropdown.ClearOptions();

        if (deckList.Count == 0)
        {
            deckSelectDropdown.AddOptions(new List<string> { NewDeckName });
            return;
        }

        deckSelectDropdown.AddOptions(deckList);
        SelectDeckOption(deckList.IndexOf(currentDeckName));
    }

    void OnDeckSelected(int index)
    {
        if (deckSelectDropdown == null || deckSelectDropdown.options.Count == 0)
            return;

        string selected = deckSelectDropdown.options[index].text;
        if (selected == NewDeckName)
        {
            currentDeck.Clear();
            ApplyDeckName(NewDeckName);
            isDeckModified = false;
            ShowDeck();
            RememberSelectedDeck();
            return;
        }

        LoadDeckByName(selected);
    }

    void OnDeckNameChanged(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return;

        currentDeckName = newName.Trim();
        isDeckModified = true;
    }

    void ApplyDeckName(string deckName)
    {
        currentDeckName = deckName;
        if (deckNameInput != null)
            deckNameInput.SetTextWithoutNotify(deckName);
    }

    void RememberSelectedDeck()
    {
        PlayerPrefs.SetString(SelectedDeckKey, currentDeckName);
        PlayerPrefs.Save();
    }

    string DeckPath(string deckName)
    {
        return Path.Combine(deckDirectory, "deck_" + SanitizeFileName(deckName) + ".json");
    }

    static string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');

        return fileName;
    }

    void SaveShuffleOff(bool shuffleOff)
    {
        PlayerPrefs.SetInt(DisableShufflePrefKey, shuffleOff ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 編集中の並びのままオフライン対戦へ入る。プレイヤーは先攻になり、終了後はデッキ編集へ戻る。
    /// </summary>
    public void StartSolitaire()
    {
        if (solitaireStarting)
            return;

        solitaireStarting = true;
        StartCoroutine(LoadBattleAfterFade());
    }

    IEnumerator LoadBattleAfterFade()
    {
        yield return ButtonPressFade.Wait(solitaireButton);
        StashCurrentDeck();
        RememberSelectedDeck();
        bool shuffleOff = shuffleOffToggle != null && shuffleOffToggle.isOn;
        BattleLaunchContext.BeginSolitaire(SnapshotDeck(currentDeck), shuffleOff);
        PhotonNetwork.OfflineMode = true;
        SceneChanger.Load("BattleScene");
    }

    void StashCurrentDeck()
    {
        returnDeck = new List<CardDBBase>(currentDeck);
        returnDeckName = currentDeckName;
        returnDeckModified = isDeckModified;
    }

    bool TryRestoreStashedDeck()
    {
        if (returnDeck == null)
            return false;

        currentDeck.Clear();
        currentDeck.AddRange(returnDeck);
        ApplyDeckName(string.IsNullOrEmpty(returnDeckName) ? NewDeckName : returnDeckName);
        isDeckModified = returnDeckModified;
        returnDeck = null;
        RefreshDeckList();
        ShowDeck();
        return true;
    }

    static List<DeckCard> SnapshotDeck(List<CardDBBase> cards)
    {
        var list = new List<DeckCard>();
        if (cards == null)
            return list;

        foreach (CardDBBase card in cards)
        {
            if (card == null)
                continue;

            CardTypeList cardType = card is BattlerDB battler ? battler.type : CardTypeList.None;
            List<CardCategoryList> category = card.category != null
                ? new List<CardCategoryList>(card.category)
                : new List<CardCategoryList>();
            list.Add(new DeckCard(card.cardKind, card.cardId, card.cost, category, cardType));
        }

        return list;
    }
}
