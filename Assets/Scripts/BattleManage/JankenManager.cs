using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 対戦開始時のじゃんけん。勝った側が先攻か後攻を選ぶ。
/// 表示物はシーン上の UI を Inspector で編集する。手の画像は各ボタンの Image か、下の Sprite に割り当てる。
/// </summary>
public class JankenManager : MonoBehaviour
{
    public static JankenManager Instance { get; private set; }
    public const string AutoFirstPlayerPrefKey = "AutoFirstPlayer";

    const int HandRock = 0;
    const int HandScissors = 1;
    const int HandPaper = 2;
    const string HandImageChildName = "JankenHand";

    [Header("表示UI")]
    [SerializeField] GameObject root;
    [SerializeField] TextMeshProUGUI messageText;
    [SerializeField] Image myPortrait;
    [SerializeField] Image opponentPortrait;
    [SerializeField] TextMeshProUGUI myHandName;
    [SerializeField] TextMeshProUGUI opponentHandName;
    [SerializeField] Button rockButton;
    [SerializeField] Button scissorsButton;
    [SerializeField] Button paperButton;
    [SerializeField] Button firstButton;
    [SerializeField] Button secondButton;

    [Header("じゃんけん画像")]
    [SerializeField] Sprite rockSprite;
    [SerializeField] Sprite scissorsSprite;
    [SerializeField] Sprite paperSprite;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(this);

        if (root != null)
            root.SetActive(false);
        SetOverlay(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public async Task ResolveFirstPlayerAsync()
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return;

        if (!battle.IsOnlineMode && (BattleLaunchContext.ForceFirstPlayer || PlayerPrefs.GetInt(AutoFirstPlayerPrefKey, 0) == 1))
        {
            battle.SetActiveSideGoesFirst(true);
            return;
        }

        if (root == null || rockButton == null || scissorsButton == null || paperButton == null)
        {
            battle.SetActiveSideGoesFirst(true);
            return;
        }

        Show();
        try
        {
            if (battle.IsOnlineMode)
                await PlayOnline();
            else
                await PlayOffline();
        }
        finally
        {
            Hide();
        }
    }

    async Task PlayOffline()
    {
        var battle = BattleManager.Instance;
        while (true)
        {
            int cpu = UnityEngine.Random.Range(0, 3);
            int mine = await SelectHand("じゃんけんをして先攻を決めます", cpu);

            int result = CompareHands(mine, cpu);
            if (result == 0)
            {
                SetMessage("あいこです。もう一度選んでください");
                await Task.Delay(900);
                continue;
            }

            if (result > 0)
            {
                SetMessage("あなたの勝ちです");
                await Task.Delay(700);
                bool goFirst = await ChooseOrder("先攻か後攻を選んでください");
                battle.SetActiveSideGoesFirst(goFirst);
                SetMessage(goFirst ? "あなたは先攻です" : "あなたは後攻です");
            }
            else
            {
                battle.SetActiveSideGoesFirst(false);
                SetMessage("CPUの勝ちです。あなたは後攻になります");
            }

            await Task.Delay(1000);
            return;
        }
    }

    async Task PlayOnline()
    {
        var battle = BattleManager.Instance;
        var sync = OnlineSyncManager.Instance;
        sync.ResetJankenSession();
        await sync.WaitUntilPeerReadyForJanken();

        int round = 0;
        while (true)
        {
            sync.PrepareJankenRound(round);
            int mine = await SelectHand("じゃんけんをして先攻を決めます");
            SetMessage("相手の選択を待っています");
            sync.SubmitJankenHand(mine, round);
            int other = await sync.WaitForOpponentJankenHand();
            Reveal(mine, other);

            int result = CompareHands(mine, other);
            if (result == 0)
            {
                SetMessage("あいこです。もう一度選んでください");
                await Task.Delay(900);
                round++;
                continue;
            }

            bool hostGoesFirst;
            if (result > 0)
            {
                SetMessage("あなたの勝ちです");
                await Task.Delay(700);
                bool goFirst = await ChooseOrder("先攻か後攻を選んでください");
                hostGoesFirst = sync.IsHostPlayer == goFirst;
                sync.SubmitJankenFirstChoice(hostGoesFirst);
            }
            else
            {
                SetMessage("相手が先攻・後攻を選んでいます");
                hostGoesFirst = await sync.WaitForJankenFirstChoice();
            }

            battle.SetActiveSideGoesFirst(hostGoesFirst);
            bool localFirst = sync.IsHostPlayer == hostGoesFirst;
            SetMessage(localFirst ? "あなたは先攻です" : "あなたは後攻です");
            await Task.Delay(1000);
            return;
        }
    }

    /// <summary>自分が勝てば 1、負ければ -1、あいこは 0。</summary>
    static int CompareHands(int mine, int other)
    {
        if (mine == other)
            return 0;
        if ((mine + 1) % 3 == other)
            return 1;
        return -1;
    }

    static string HandName(int hand)
    {
        switch (hand)
        {
            case HandRock: return "グー";
            case HandScissors: return "チョキ";
            default: return "パー";
        }
    }

    Sprite SpriteFor(int hand)
    {
        Sprite assigned = hand == HandRock ? rockSprite : hand == HandScissors ? scissorsSprite : paperSprite;
        if (assigned != null)
            return assigned;

        Button button = ButtonFor(hand);
        if (button != null && button.targetGraphic is Image image)
            return image.sprite;
        return null;
    }

    Button ButtonFor(int hand)
    {
        switch (hand)
        {
            case HandRock: return rockButton;
            case HandScissors: return scissorsButton;
            default: return paperButton;
        }
    }

    void Show()
    {
        ApplyAssignedSprites();
        if (root != null)
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
        }
        SetOverlay(true);
    }

    void Hide()
    {
        if (root != null)
            root.SetActive(false);
        SetOverlay(false);
    }

    void SetOverlay(bool visible)
    {
        Image overlay = GetComponent<Image>();
        if (overlay != null)
            overlay.enabled = visible;
    }

    void ApplyAssignedSprites()
    {
        ApplySprite(rockButton, rockSprite);
        ApplySprite(scissorsButton, scissorsSprite);
        ApplySprite(paperButton, paperSprite);
    }

    static void ApplySprite(Button button, Sprite sprite)
    {
        if (button == null || sprite == null)
            return;
        Image image = button.targetGraphic as Image;
        if (image == null)
            image = button.GetComponent<Image>();
        if (image == null)
            return;
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = Color.white;
    }

    Task<int> SelectHand(string message, int simultaneousOpponentHand = -1)
    {
        SetMessage(message);
        SetPortrait(myPortrait, myHandName, -1);
        SetPortrait(opponentPortrait, opponentHandName, -1);
        SetHandsVisible(true);
        SetOrderVisible(false);
        Highlight(-1);

        var tcs = new TaskCompletionSource<int>();
        WireHand(rockButton, HandRock, tcs, simultaneousOpponentHand);
        WireHand(scissorsButton, HandScissors, tcs, simultaneousOpponentHand);
        WireHand(paperButton, HandPaper, tcs, simultaneousOpponentHand);
        return tcs.Task;
    }

    void WireHand(Button button, int hand, TaskCompletionSource<int> tcs, int simultaneousOpponentHand)
    {
        if (button == null)
            return;
        button.onClick.RemoveAllListeners();
        button.interactable = true;
        button.onClick.AddListener(() =>
        {
            if (tcs.Task.IsCompleted)
                return;
            SetHandsInteractable(false);
            SetPortrait(myPortrait, myHandName, hand);
            if (simultaneousOpponentHand >= 0)
                SetPortrait(opponentPortrait, opponentHandName, simultaneousOpponentHand);
            Highlight(hand);
            tcs.TrySetResult(hand);
        });
    }

    void Reveal(int mine, int other)
    {
        SetPortrait(myPortrait, myHandName, mine);
        SetPortrait(opponentPortrait, opponentHandName, other);
        SetHandsInteractable(false);
        Highlight(mine);
    }

    Task<bool> ChooseOrder(string message)
    {
        SetMessage(message);
        SetHandsVisible(false);
        SetOrderVisible(true);
        var tcs = new TaskCompletionSource<bool>();
        WireOrder(firstButton, true, tcs);
        WireOrder(secondButton, false, tcs);
        return tcs.Task;
    }

    void WireOrder(Button button, bool goFirst, TaskCompletionSource<bool> tcs)
    {
        if (button == null)
            return;
        button.onClick.RemoveAllListeners();
        button.interactable = true;
        button.onClick.AddListener(() =>
        {
            SetOrderVisible(false);
            tcs.TrySetResult(goFirst);
        });
    }

    void SetMessage(string message)
    {
        if (messageText != null)
            messageText.text = message;
    }

    void SetHandsVisible(bool visible)
    {
        if (rockButton != null)
            rockButton.gameObject.SetActive(visible);
        if (scissorsButton != null)
            scissorsButton.gameObject.SetActive(visible);
        if (paperButton != null)
            paperButton.gameObject.SetActive(visible);
    }

    void SetHandsInteractable(bool interactable)
    {
        if (rockButton != null)
            rockButton.interactable = interactable;
        if (scissorsButton != null)
            scissorsButton.interactable = interactable;
        if (paperButton != null)
            paperButton.interactable = interactable;
    }

    void SetOrderVisible(bool visible)
    {
        if (firstButton != null)
            firstButton.gameObject.SetActive(visible);
        if (secondButton != null)
            secondButton.gameObject.SetActive(visible);
    }

    void Highlight(int hand)
    {
        SetOutline(rockButton, hand == HandRock);
        SetOutline(scissorsButton, hand == HandScissors);
        SetOutline(paperButton, hand == HandPaper);
    }

    static void SetOutline(Button button, bool enabled)
    {
        if (button == null)
            return;
        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
            outline.enabled = enabled;
    }

    void SetPortrait(Image image, TextMeshProUGUI name, int hand)
    {
        if (image == null)
            return;

        if (hand < 0)
        {
            RemoveHandImage(image.transform);
            if (name != null)
                name.text = "？";
            return;
        }

        Image handImage = GetOrCreateHandImage(image.transform);
        Sprite sprite = SpriteFor(hand);
        handImage.sprite = sprite;
        handImage.preserveAspect = true;
        handImage.color = Color.white;
        handImage.enabled = sprite != null;
        if (name != null)
            name.text = HandName(hand);
    }

    static void RemoveHandImage(Transform portrait)
    {
        Transform child = portrait.Find(HandImageChildName);
        if (child != null)
            Destroy(child.gameObject);
    }

    static Image GetOrCreateHandImage(Transform portrait)
    {
        Transform child = portrait.Find(HandImageChildName);
        if (child != null)
        {
            Image existing = child.GetComponent<Image>();
            if (existing != null)
                return existing;
        }

        var go = new GameObject(HandImageChildName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(portrait, false);
        go.transform.SetAsFirstSibling();

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
