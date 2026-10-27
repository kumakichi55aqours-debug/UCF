using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// カードに付与されているスキルの一覧。付与元カード名と残ターンを表示する。
/// </summary>
public class GrantedSkillListUI : MonoBehaviour
{
    public static GrantedSkillListUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private Transform content;
    [SerializeField] private GrantedSkillRow rowTemplate;
    [SerializeField] private TextMeshProUGUI emptyText;
    [SerializeField] private Button closeButton;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        if (panel != null)
            panel.SetActive(false);
        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void Show(CardController card)
    {
        if (Instance == null || card == null) return;
        Instance.ShowInternal(card);
    }

    public static void HideIfAny()
    {
        if (Instance != null)
            Instance.Hide();
    }

    public void Hide()
    {
        ClearRows();
        if (panel != null)
            panel.SetActive(false);
    }

    private void ShowInternal(CardController card)
    {
        if (panel != null)
            panel.SetActive(true);

        ClearRows();

        var effects = card.passiveEffects;
        bool hasEffect = effects != null && effects.Count > 0;
        if (emptyText != null)
            emptyText.gameObject.SetActive(!hasEffect);

        if (!hasEffect || content == null || rowTemplate == null)
            return;

        foreach (var effect in effects)
        {
            string source = string.IsNullOrEmpty(effect?.SourceCardName) ? "不明" : effect.SourceCardName;
            string turns = effect == null
                ? "-"
                : (effect.RemainingTurns < 0 ? "∞" : effect.RemainingTurns.ToString());

            var row = Instantiate(rowTemplate, content);
            row.gameObject.SetActive(true);
            row.Set(source, turns);
        }
    }

    private void ClearRows()
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }
}
