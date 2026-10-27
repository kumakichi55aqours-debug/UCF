using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// デッキ選択UIを管理するクラス
/// </summary>
public class DeckSelectionUI : MonoBehaviour
{
    public static DeckSelectionUI Instance { get; private set; }

    [Header("UI Elements")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Dropdown deckDropdown;
    [SerializeField] private TMP_Dropdown cpuDeckDropdown;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI playerDeckLabel;
    [SerializeField] private TextMeshProUGUI cpuDeckLabel;
    [SerializeField] private Toggle autoFirstToggle;

    private string selectedDeckName;
    private string selectedCpuDeckName;
    private System.Action<string> onDeckSelected;
    private System.Action<string, string> onDecksSelected;
    private const float OnlinePanelHeight = 310f;

    private bool selectingCpuDeck;
    private string deckDirectory;
    private RectTransform panelRect;
    private Vector2 panelDefaultSize;
    private Vector2 panelDefaultPos;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        deckDirectory = Path.Combine(Application.streamingAssetsPath, "Decks");
        panelRect = panel != null ? panel.GetComponent<RectTransform>() : null;
        if (panelRect != null)
        {
            panelDefaultSize = panelRect.sizeDelta;
            panelDefaultPos = panelRect.anchoredPosition;
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(OnCancel);
        }

        if (deckDropdown != null)
        {
            deckDropdown.onValueChanged.RemoveAllListeners();
            deckDropdown.onValueChanged.AddListener(OnDeckSelected);
        }

        if (cpuDeckDropdown != null)
        {
            cpuDeckDropdown.onValueChanged.RemoveAllListeners();
            cpuDeckDropdown.onValueChanged.AddListener(OnCpuDeckSelected);
        }

        ApplyLayout(false);
    }

    /// <summary>
    /// デッキ選択UIを表示（プレイヤーのみ）
    /// </summary>
    public void Show(System.Action<string> onSelected, string title = "デッキを選択してください")
    {
        ShowInternal(onSelected, null, false, title);
    }

    /// <summary>
    /// デッキ選択UIを表示（プレイヤーとCPU）
    /// </summary>
    public void Show(System.Action<string, string> onSelected, string title = "デッキを選択してください")
    {
        ShowInternal(null, onSelected, true, title);
    }

    /// <summary>
    /// デッキ選択UIを非表示
    /// </summary>
    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
        onDeckSelected = null;
        onDecksSelected = null;
        selectingCpuDeck = false;
        ApplyLayout(false);
    }

    private void ShowInternal(System.Action<string> onSelected, System.Action<string, string> onBothSelected, bool showCpuDeck, string title)
    {
        if (panel == null)
            return;

        onDeckSelected = onSelected;
        onDecksSelected = onBothSelected;
        selectingCpuDeck = showCpuDeck;

        if (titleText != null)
        {
            titleText.text = title;
        }

        ApplyLayout(showCpuDeck);
        panel.SetActive(true);

        string lastSelectedDeckName = PlayerPrefs.GetString("SelectedDeckName", "");
        selectedDeckName = RefreshDeckList(deckDropdown, lastSelectedDeckName);
        if (showCpuDeck)
        {
            selectedCpuDeckName = RefreshDeckList(cpuDeckDropdown, lastSelectedDeckName);
            if (autoFirstToggle != null)
                autoFirstToggle.isOn = PlayerPrefs.GetInt(JankenManager.AutoFirstPlayerPrefKey, 0) == 1;
        }

        UpdateConfirmInteractable();
        StartCoroutine(VerifyDropdownSelection(deckDropdown, lastSelectedDeckName, false));
        if (showCpuDeck)
            StartCoroutine(VerifyDropdownSelection(cpuDeckDropdown, lastSelectedDeckName, true));
    }

    private void ApplyLayout(bool showCpuDeck)
    {
        if (cpuDeckDropdown != null)
            cpuDeckDropdown.gameObject.SetActive(showCpuDeck);
        if (cpuDeckLabel != null)
            cpuDeckLabel.gameObject.SetActive(showCpuDeck);
        if (autoFirstToggle != null)
            autoFirstToggle.gameObject.SetActive(showCpuDeck);

        if (panelRect == null)
            return;

        float height = showCpuDeck ? panelDefaultSize.y : OnlinePanelHeight;
        float shrink = panelDefaultSize.y - height;
        panelRect.sizeDelta = new Vector2(panelDefaultSize.x, height);
        panelRect.anchoredPosition = panelDefaultPos + new Vector2(0f, shrink * (1f - panelRect.pivot.y));
    }

    private string RefreshDeckList(TMP_Dropdown dropdown, string preferredDeckName)
    {
        if (dropdown == null) return null;

        List<string> deckList = GetDeckList();
        dropdown.ClearOptions();

        if (deckList.Count == 0)
        {
            dropdown.AddOptions(new List<string> { "デッキがありません" });
            return null;
        }

        dropdown.AddOptions(deckList);
        int targetIndex = FindDeckIndex(deckList, preferredDeckName);

        UnityEngine.Events.UnityAction<int> listener = dropdown == cpuDeckDropdown
            ? OnCpuDeckSelected
            : OnDeckSelected;
        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.value = targetIndex;
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(listener);
        return deckList[targetIndex];
    }

    private static int FindDeckIndex(List<string> deckList, string preferredDeckName)
    {
        if (string.IsNullOrEmpty(preferredDeckName))
            return 0;

        for (int i = 0; i < deckList.Count; i++)
        {
            if (deckList[i] == preferredDeckName)
                return i;
        }

        return 0;
    }

    private List<string> GetDeckList()
    {
        List<string> deckList = new List<string>();

        if (!Directory.Exists(deckDirectory))
        {
            return deckList;
        }

        string[] files = Directory.GetFiles(deckDirectory, "deck_*.json");
        foreach (string file in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.StartsWith("deck_"))
            {
                string deckName = fileName.Substring(5);
                deckList.Add(deckName);
            }
        }

        return deckList;
    }

    private void OnDeckSelected(int index)
    {
        selectedDeckName = ReadDropdownSelection(deckDropdown, index);
        UpdateConfirmInteractable();
    }

    private void OnCpuDeckSelected(int index)
    {
        selectedCpuDeckName = ReadDropdownSelection(cpuDeckDropdown, index);
        UpdateConfirmInteractable();
    }

    private static string ReadDropdownSelection(TMP_Dropdown dropdown, int index)
    {
        if (dropdown == null || dropdown.options.Count == 0 || index < 0 || index >= dropdown.options.Count)
            return null;

        string selected = dropdown.options[index].text;
        return selected != "デッキがありません" ? selected : null;
    }

    private void UpdateConfirmInteractable()
    {
        if (confirmButton == null) return;

        bool playerReady = !string.IsNullOrEmpty(selectedDeckName);
        bool cpuReady = !selectingCpuDeck || !string.IsNullOrEmpty(selectedCpuDeckName);
        confirmButton.interactable = playerReady && cpuReady;
    }

    private void OnConfirm()
    {
        if (string.IsNullOrEmpty(selectedDeckName))
        {
            Debug.LogWarning("デッキが選択されていません");
            return;
        }

        if (selectingCpuDeck && string.IsNullOrEmpty(selectedCpuDeckName))
        {
            Debug.LogWarning("CPUデッキが選択されていません");
            return;
        }

        PlayerPrefs.SetString("SelectedDeckName", selectedDeckName);
        if (selectingCpuDeck)
        {
            PlayerPrefs.SetString("CpuDeckName", selectedCpuDeckName);
            if (autoFirstToggle != null)
                PlayerPrefs.SetInt(JankenManager.AutoFirstPlayerPrefKey, autoFirstToggle.isOn ? 1 : 0);
        }
        PlayerPrefs.Save();

        if (selectingCpuDeck)
            onDecksSelected?.Invoke(selectedDeckName, selectedCpuDeckName);
        else
            onDeckSelected?.Invoke(selectedDeckName);

        Hide();
    }

    private void OnCancel()
    {
        Hide();
    }

    private IEnumerator VerifyDropdownSelection(TMP_Dropdown dropdown, string expectedDeckName, bool isCpuDeck)
    {
        yield return null;

        if (dropdown == null || dropdown.options.Count == 0)
            yield break;

        if (string.IsNullOrEmpty(expectedDeckName))
            yield break;

        int currentValue = dropdown.value;
        string currentText = dropdown.options[currentValue].text;
        if (currentText == expectedDeckName)
            yield break;

        for (int i = 0; i < dropdown.options.Count; i++)
        {
            if (dropdown.options[i].text != expectedDeckName)
                continue;

            UnityEngine.Events.UnityAction<int> listener = isCpuDeck ? OnCpuDeckSelected : OnDeckSelected;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.value = i;
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(listener);

            if (isCpuDeck)
                selectedCpuDeckName = expectedDeckName;
            else
                selectedDeckName = expectedDeckName;
            UpdateConfirmInteractable();
            break;
        }
    }
}
