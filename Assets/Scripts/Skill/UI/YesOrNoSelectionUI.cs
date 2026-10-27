using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;
using System;

public class YesOrNoSelectionUI : MonoBehaviour
{
    public static YesOrNoSelectionUI Instance { get; private set; }

    [SerializeField] private GameObject messageText;
    [SerializeField] private GameObject yesOrNoSelectionUI;
    [SerializeField] private TextMeshProUGUI yOrNText;
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;

    public bool IsShowing =>
        (messageText != null && messageText.activeInHierarchy)
        || (yesOrNoSelectionUI != null && yesOrNoSelectionUI.activeInHierarchy);

    private CardController currentCard;
    private SkillDB currentSkill;
    private string customMessage;
    private Func<Task> onYesCallback;
    private Func<Task> onNoCallback;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    // カードとスキル情報を受け取る（Yesコールバックのみ）
    public void ShowSelectionUI(CardController card, SkillDB skill, Func<Task> onYes)
    {
        ShowSelectionUI(card, skill, onYes, null);
    }

    // カードとスキル情報を受け取る（YesとNoのコールバック）
    public void ShowSelectionUI(CardController card, SkillDB skill, Func<Task> onYes, Func<Task> onNo)
    {
        currentCard = card;
        currentSkill = skill;
        customMessage = null;
        PrepareAndShow(onYes, onNo);
    }

    // メッセージのみを指定して表示
    public void ShowSelectionUI(string message, Func<Task> onYes, Func<Task> onNo)
    {
        currentCard = null;
        currentSkill = null;
        customMessage = message;
        PrepareAndShow(onYes, onNo);
    }

    private void PrepareAndShow(Func<Task> onYes, Func<Task> onNo)
    {
        onYesCallback = onYes;
        onNoCallback = onNo;

        messageText.SetActive(true);
        yesOrNoSelectionUI.SetActive(true);
        yesButton.gameObject.SetActive(true);
        noButton.gameObject.SetActive(true);

        if (!string.IsNullOrEmpty(customMessage))
        {
            yOrNText.text = customMessage;
        }
        else if (currentCard != null && currentSkill != null)
        {
            yOrNText.text = $"{currentCard.cardName} の{currentSkill.スキル名}効果を使用できます。使用しますか？";
        }
        else if (currentSkill != null)
        {
            yOrNText.text = $"{currentSkill.スキル名}を使用しますか？";
        }
        else
        {
            yOrNText.text = "効果を使用しますか？";
        }
    }

    public async void OnYesButton()
    {
        messageText.SetActive(false);
        yesOrNoSelectionUI.SetActive(false);
        yesButton.gameObject.SetActive(false);
        noButton.gameObject.SetActive(false);

        try
        {
            if (onYesCallback != null)
            {
                await onYesCallback();
            }

        }
        finally
        {
            OnlineSyncManager.Instance?.EndOperationBlock();

            if (BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode)
            {
                OnlineSyncManager.Instance?.SyncEndOpponentDecisionBlock();
            }

            ClearState();
        }
    }

    public async void OnNoButton()
    {
        messageText.SetActive(false);
        yesOrNoSelectionUI.SetActive(false);
        yesButton.gameObject.SetActive(false);
        noButton.gameObject.SetActive(false);

        try
        {
            // Noコールバックがあれば実行
            if (onNoCallback != null)
            {
                await onNoCallback();
            }
        }
        finally
        {
            OnlineSyncManager.Instance?.EndOperationBlock();

            if (BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode)
            {
                OnlineSyncManager.Instance?.SyncEndOpponentDecisionBlock();
            }

            ClearState();
        }
    }

    public async void WithoutSelectSkill(CardController card, SkillDB skill)
    {
        currentCard = card;
        currentSkill = skill;

        if (currentSkill != null)
        {
            SkillContext context = new SkillContext(currentCard, currentCard, currentCard.transform);
            await currentSkill.ActivateEffect(context);
        }
    }

    private void ClearState()
    {
        currentCard = null;
        currentSkill = null;
        customMessage = null;
        onYesCallback = null;
        onNoCallback = null;
    }
}
