using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HelpWindow : MonoBehaviour
{
    [SerializeField] HelpCatalog catalog;
    [SerializeField] RectTransform subjectContent;
    [SerializeField] RectTransform subSubjectContent;
    [SerializeField] Button subjectButtonTemplate;
    [SerializeField] TextMeshProUGUI bodyText;
    [SerializeField] RectTransform pageRoot;
    [SerializeField] RectTransform pageContent;
    [SerializeField] ScrollRect pageScroll;
    [SerializeField] Button closeButton;
    [SerializeField] Button backdropButton;
    [SerializeField] Color selectedSubjectColor = new Color(0.45f, 0.62f, 0.95f, 1f);

    readonly List<Button> spawnedMains = new List<Button>();
    readonly List<Button> spawnedSubs = new List<Button>();
    ScrollRect bodyScroll;
    GameObject currentPage;
    int selectedMain;
    int selectedSub;
    Color normalSubjectColor = Color.white;

    public HelpCatalog Catalog => catalog;

    void Awake()
    {
        if (catalog == null)
            catalog = Resources.Load<HelpCatalog>("HelpCatalog");

        if (subjectButtonTemplate != null)
        {
            Image image = subjectButtonTemplate.GetComponent<Image>();
            if (image != null)
                normalSubjectColor = image.color;
            subjectButtonTemplate.gameObject.SetActive(false);
        }

        if (bodyText != null)
            bodyScroll = bodyText.GetComponentInParent<ScrollRect>(true);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        if (backdropButton != null)
            backdropButton.onClick.AddListener(Close);
    }

    void OnEnable()
    {
        Rebuild();
    }

    public void Toggle()
    {
        if (gameObject.activeSelf)
            Close();
        else
            Open();
    }

    public void Open()
    {
        selectedMain = 0;
        selectedSub = 0;
        bool wasActive = gameObject.activeSelf;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        if (wasActive)
            Rebuild();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void Rebuild()
    {
        if (subjectContent == null || subjectButtonTemplate == null)
            return;

        ClearSpawned(spawnedMains);
        ClearSpawned(spawnedSubs);

        int mainCount = MainCount();
        if (selectedMain < 0 || selectedMain >= mainCount)
            selectedMain = 0;

        for (int i = 0; i < mainCount; i++)
        {
            int index = i;
            spawnedMains.Add(CreateButton(
                subjectContent,
                catalog.subjects[i].subject,
                () => SelectMain(index),
                i == selectedMain));
        }

        RebuildSubs();
        ShowBody();
    }

    void SelectMain(int index)
    {
        selectedMain = index;
        selectedSub = 0;
        Paint(spawnedMains, selectedMain);
        RebuildSubs();
        ShowBody();
    }

    void SelectSub(int index)
    {
        selectedSub = index;
        Paint(spawnedSubs, selectedSub);
        ShowBody();
    }

    void RebuildSubs()
    {
        ClearSpawned(spawnedSubs);
        if (subSubjectContent == null)
            return;

        List<HelpEntry> subs = CurrentSubs();
        int count = subs != null ? subs.Count : 0;
        if (selectedSub < 0 || selectedSub >= count)
            selectedSub = 0;

        for (int i = 0; i < count; i++)
        {
            int index = i;
            spawnedSubs.Add(CreateButton(
                subSubjectContent,
                subs[i].subject,
                () => SelectSub(index),
                i == selectedSub));
        }
    }

    Button CreateButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick, bool selected)
    {
        Button button = Instantiate(subjectButtonTemplate, parent);
        button.gameObject.SetActive(true);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.text = label;

        ApplySubjectColor(button, selected);
        return button;
    }

    void ShowBody()
    {
        ClearPage();

        HelpEntry entry = CurrentEntry();
        bool usePage = entry != null && entry.pagePrefab != null && pageRoot != null;
        if (bodyScroll != null)
            bodyScroll.gameObject.SetActive(!usePage);
        if (pageRoot != null)
            pageRoot.gameObject.SetActive(usePage);

        if (usePage)
        {
            ShowPage(entry);
            return;
        }

        if (bodyText == null)
            return;

        bodyText.richText = true;
        bodyText.text = entry != null ? entry.text ?? "" : "";
        LayoutRebuilder.ForceRebuildLayoutImmediate(bodyText.rectTransform);
        if (bodyScroll != null)
            bodyScroll.verticalNormalizedPosition = 1f;
    }

    void ShowPage(HelpEntry entry)
    {
        RectTransform parent = pageContent != null ? pageContent : pageRoot;
        currentPage = Instantiate(entry.pagePrefab, parent);
        currentPage.SetActive(true);

        RectTransform rect = currentPage.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 10f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        HelpPage page = currentPage.GetComponent<HelpPage>();
        if (page != null)
            page.Apply(entry.text);

        FitPageHeight(rect);
        if (pageScroll != null)
            pageScroll.verticalNormalizedPosition = 1f;
    }

    void FitPageHeight(RectTransform page)
    {
        if (page == null)
            return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(page);

        float width = page.rect.width;
        if (width < 1f && pageContent != null)
            width = pageContent.rect.width;

        float height = 10f;
        RectTransform[] rects = page.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i] == page)
                continue;

            Vector3[] corners = new Vector3[4];
            rects[i].GetWorldCorners(corners);
            for (int c = 0; c < 4; c++)
                height = Mathf.Max(height, -page.InverseTransformPoint(corners[c]).y);
        }

        TMP_Text[] texts = page.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text tmp = texts[i];
            float textWidth = tmp.rectTransform.rect.width;
            if (textWidth < 1f)
                textWidth = Mathf.Max(1f, width - tmp.margin.x - tmp.margin.z);
            float preferred = tmp.GetPreferredValues(textWidth, Mathf.Infinity).y;
            float top = page.InverseTransformPoint(
                tmp.rectTransform.TransformPoint(new Vector3(0f, tmp.rectTransform.rect.yMax, 0f))).y;
            height = Mathf.Max(height, -top + preferred);
        }

        page.sizeDelta = new Vector2(0f, height);
        if (pageContent == null)
            return;

        pageContent.anchorMin = new Vector2(0f, 1f);
        pageContent.anchorMax = new Vector2(1f, 1f);
        pageContent.pivot = new Vector2(0.5f, 1f);
        pageContent.anchoredPosition = Vector2.zero;
        pageContent.sizeDelta = new Vector2(0f, height);
    }

    void ClearPage()
    {
        if (currentPage == null)
            return;

        DestroyImmediate(currentPage);
        currentPage = null;
    }

    void ClearSpawned(List<Button> buttons)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i] != null)
                DestroyImmediate(buttons[i].gameObject);
        }
        buttons.Clear();
    }

    void Paint(List<Button> buttons, int selected)
    {
        for (int i = 0; i < buttons.Count; i++)
            ApplySubjectColor(buttons[i], i == selected);
    }

    void ApplySubjectColor(Button button, bool selected)
    {
        if (button == null)
            return;

        Image image = button.targetGraphic as Image;
        if (image == null)
            image = button.GetComponent<Image>();
        if (image == null)
            return;

        image.color = selected ? selectedSubjectColor : normalSubjectColor;
    }

    int MainCount()
    {
        return catalog != null && catalog.subjects != null ? catalog.subjects.Count : 0;
    }

    List<HelpEntry> CurrentSubs()
    {
        if (catalog == null || catalog.subjects == null)
            return null;
        if (selectedMain < 0 || selectedMain >= catalog.subjects.Count)
            return null;
        return catalog.subjects[selectedMain].subSubjects;
    }

    HelpEntry CurrentEntry()
    {
        List<HelpEntry> subs = CurrentSubs();
        if (subs == null || selectedSub < 0 || selectedSub >= subs.Count)
            return null;
        return subs[selectedSub];
    }
}
