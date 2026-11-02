using System;
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
    const int MaxCopiesPerCard = 3;

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
    public Button clearFilterButton;
    public Button saveButton;
    public Button clearButton;
    public Button deleteDeckButton;
    public Button solitaireButton;
    public Toggle shuffleOffToggle;
    public TMP_InputField deckNameInput;
    public TMP_Dropdown deckSelectDropdown;

    public List<CardDBBase> availableCards = new();
    public List<CardDBBase> currentDeck = new();

    // ドロップダウンの並び。超・滅・無はカードが無いので入れない。
    readonly List<CardTypeList> typeFilterOptions = new();

    string deckDirectory;
    string currentDeckName = NewDeckName;
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

        Bind(clearFilterButton, ClearFilters);
        FillTypeDropdown();
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
        if (cardData == null || CountCopies(cardData) >= MaxCopiesPerCard)
            return;

        currentDeck.Add(cardData);
        isDeckModified = true;
        ShowDeck();
    }

    int CountCopies(CardDBBase cardData)
    {
        int count = 0;
        foreach (CardDBBase card in currentDeck)
        {
            if (card != null && card.cardKind == cardData.cardKind && card.cardId == cardData.cardId)
                count++;
        }

        return count;
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

        File.WriteAllText(DeckPath(currentDeckName), JsonConvert.SerializeObject(ToDeckCards(currentDeck), Formatting.Indented));
        isDeckModified = false;
        RememberSelectedDeck();
        RefreshDeckList();
        RefreshAssetsInEditor();
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
        else if (deckName != NewDeckName)
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
        CardTypeList selectedType = SelectedType();
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
            return;

        string path = DeckPath(currentDeckName);
        if (!File.Exists(path))
            return;

        File.Delete(path);
        BeginNewDeck();
        RefreshDeckList();
        RefreshAssetsInEditor();
    }

    static int CompareCards(CardDBBase a, CardDBBase b)
    {
        int kind = a.cardKind.CompareTo(b.cardKind);
        if (kind != 0)
            return kind;

        return a.cardId.CompareTo(b.cardId);
    }

    // 0番は「全て」。以降の表示文字がコスト。
    bool MatchesCost(CardDBBase card)
    {
        if (cardCostDropdown == null || cardCostDropdown.value <= 0)
            return true;

        return int.TryParse(cardCostDropdown.options[cardCostDropdown.value].text, out int cost)
            && card.cost == cost;
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

    void FillTypeDropdown()
    {
        if (cardTypeDropdown == null)
            return;

        cardTypeDropdown.onValueChanged.RemoveAllListeners();
        cardTypeDropdown.ClearOptions();
        typeFilterOptions.Clear();
        var labels = new List<string>();
        foreach (CardTypeList type in Enum.GetValues(typeof(CardTypeList)))
        {
            if (type == CardTypeList.超 || type == CardTypeList.滅 || type == CardTypeList.無)
                continue;

            typeFilterOptions.Add(type);
            labels.Add(type == CardTypeList.None ? "全て" : type.ToString());
        }
        cardTypeDropdown.AddOptions(labels);
        cardTypeDropdown.SetValueWithoutNotify(0);
        cardTypeDropdown.onValueChanged.AddListener(_ => SearchCards());
    }

    CardTypeList SelectedType()
    {
        if (cardTypeDropdown == null)
            return CardTypeList.None;

        int index = cardTypeDropdown.value;
        if (index < 0 || index >= typeFilterOptions.Count)
            return CardTypeList.None;

        return typeFilterOptions[index];
    }

    static bool MatchesType(CardDBBase card, CardTypeList selectedType)
    {
        if (selectedType == CardTypeList.None)
            return true;
        if (card is BattlerDB battler)
            return battler.type == selectedType;
        if (card is TacticsDB tactics)
            return tactics.type == selectedType;
        return false;
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
            deckList.Add(Path.GetFileNameWithoutExtension(file).Substring("deck_".Length));

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
            BeginNewDeck();
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

    void BeginNewDeck()
    {
        currentDeck.Clear();
        ApplyDeckName(NewDeckName);
        isDeckModified = false;
        ShowDeck();
        RememberSelectedDeck();
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
        BattleLaunchContext.BeginSolitaire(ToDeckCards(currentDeck), shuffleOff);
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

    static List<DeckCard> ToDeckCards(List<CardDBBase> cards)
    {
        var list = new List<DeckCard>();
        if (cards == null)
            return list;

        foreach (CardDBBase card in cards)
        {
            if (card == null)
                continue;

            CardTypeList cardType = CardTypeList.None;
            if (card is BattlerDB battler)
                cardType = battler.type;
            else if (card is TacticsDB tactics)
                cardType = tactics.type;
            List<CardCategoryList> category = card.category != null
                ? new List<CardCategoryList>(card.category)
                : new List<CardCategoryList>();
            list.Add(new DeckCard(card.cardKind, card.cardId, card.cost, category, cardType));
        }

        return list;
    }

    static void RefreshAssetsInEditor()
    {
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }
}
