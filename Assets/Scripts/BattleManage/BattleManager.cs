using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public partial class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [SerializeField] CardController cardPrefab;
    [SerializeField] GameObject cardBackPrefab;
    [SerializeField] public Canvas mainCanvas;
    [SerializeField] AudioSource seSource;

    [Header("プレイヤー")]
    public Transform pBattlerZone;
    public Transform pTacticsZone;
    public Transform pFieldZone;
    public Transform pHandZone;
    public Transform pDeckZone;
    public Transform pGraveZone;
    public ResourceGroup pResources;
    [SerializeField] private TextMeshProUGUI pDeckCountText;

    [Header("エネミー")]
    public Transform eBattlerZone;
    public Transform eTacticsZone;
    public Transform eFieldZone;
    public Transform eDeckZone;
    public Transform eHandZone;
    public Transform eGraveZone;
    public ResourceGroup eResources;
    [SerializeField] private TextMeshProUGUI eDeckCountText;

    public List<DeckCard> pBattleDeck = new List<DeckCard>();
    public List<DeckCard> eBattleDeck = new List<DeckCard>();
    public List<DeckCard> enemyHand = new List<DeckCard>();
    public int eDeckCount = 0; // オンライン時の相手デッキ枚数
    private string deckFilePath;
    private string enemyDeckFilePath;

    public int curTurn = 1;
    public int curPTurn = 0;
    public int curETurn = 0;

    /// <summary>
    /// オンラインではホスト、オフラインではプレイヤーが先攻なら true。
    /// 先攻側が奇数ターンを担当する。
    /// </summary>
    public bool ActiveSideGoesFirst { get; private set; } = true;

    public void SetActiveSideGoesFirst(bool goesFirst)
    {
        ActiveSideGoesFirst = goesFirst;
    }

    public bool IsActiveSideTurn => (curTurn % 2 == 1) == ActiveSideGoesFirst;

    // オンライン状態管理はOnlineSyncManagerに委譲
    public bool IsOnlineMode => OnlineSyncManager.Instance.IsOnlineMode;
    public bool IsHostPlayer => OnlineSyncManager.Instance.IsHostPlayer;
    public bool IsMyTurn => OnlineSyncManager.Instance.IsMyTurn;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        // 選択されたデッキ名を取得（なければデフォルト）
        string selectedDeckName = PlayerPrefs.GetString("SelectedDeckName", "");
        deckFilePath = ResolveDeckFilePath(selectedDeckName);

        string cpuDeckName = PlayerPrefs.GetString("CpuDeckName", "");
        if (string.IsNullOrEmpty(cpuDeckName))
            cpuDeckName = selectedDeckName;
        enemyDeckFilePath = ResolveDeckFilePath(cpuDeckName);
    }

    private string ResolveDeckFilePath(string deckName)
    {
        if (!string.IsNullOrEmpty(deckName))
        {
            string fileName = $"deck_{SanitizeFileName(deckName)}.json";
            return Path.Combine(Application.streamingAssetsPath, "Decks", fileName);
        }

        return Path.Combine(Application.streamingAssetsPath, "Decks", "deck.json");
    }
    
    // ファイル名として使用できない文字を置換
    private string SanitizeFileName(string fileName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<SkillActivationCompletedEvent>(OnSkillActivationCompleted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SkillActivationCompletedEvent>(OnSkillActivationCompleted);
    }

    // 作戦カードの場合、スキル処理完了後に墓地に移動
    private async void OnSkillActivationCompleted(SkillActivationCompletedEvent e)
    {
        if (e.Card.cardKind == CardKind.作戦)
        {
            if (e.Card.isMyCard)
                await UseTacticsComplete(e.Card);
            else
            {
                if (IsOnlineMode)
                    await OnlineSyncManager.Instance.ExecuteTacticsToGraveyard(e.Card);
                else
                    await UseTacticsComplete(e.Card);
            }
        }
    }

    private async void Start()
    {
        curTurn = 1;
        LoadDeck();

        if (IsOnlineMode) 
        {
            SetupOnlineBattle();
            await OnlineSyncManager.Instance.WaitForBothPlayersReady();
        }
        else
        {
            InitializeEnemyDeck();
            if (!BattleLaunchContext.DisableDeckShuffle)
                ShuffleDeck();
        }

        await ResetAllUsedCounts();
        
        await StartGameAsync();

        pResources.Hp.OnHpToZero += HandlePlayerDefeat;
        eResources.Hp.OnHpToZero += HandleEnemyDefeat;
    }

    // オンライン対戦の初期設定
    private void SetupOnlineBattle()
    {
        OnlineSyncManager.Instance.SyncTurnNumber(curTurn);
        OnlineSyncManager.Instance.SyncDeckCountToOpponent();
    }

    // カード生成
    public CardController CreateCard(CardKind cardKind, int cardID, Transform place)
    {
        CardController card = Instantiate(cardPrefab, place);
        card.Init(cardKind, cardID);
        return card;
    }

    // 全ての使用回数を0にリセット
    public async Task ResetAllUsedCounts()
    {
        var curTurnZones = IsMyTurn ?
            new[] { pBattlerZone, pTacticsZone, pFieldZone } :
            new[] { eBattlerZone, eTacticsZone, eFieldZone };

        foreach (var zone in curTurnZones)
        {
            foreach (CardController card in zone.GetComponentsInChildren<CardController>())
                card.hasUsedMove = false;
        }

        RefreshCanUseMovePanels();

        SkillUseCountStore.ResetAll();

        await Task.Yield();
    }

    void RefreshCanUseMovePanels()
    {
        RefreshZoneCanUseMovePanels(pBattlerZone);
        RefreshZoneCanUseMovePanels(eBattlerZone);
    }

    static void RefreshZoneCanUseMovePanels(Transform zone)
    {
        if (zone == null)
            return;
        foreach (CardController card in zone.GetComponentsInChildren<CardController>())
            card.GetComponent<BattleCardView>()?.UpdateCanUseMovePanel();
    }

    // ゾーン内の並び順番号
    public int GetCardIndexInZone(CardController card, Transform zone)
    {
        if (card == null || zone == null) return -1;
        var cards = zone.GetComponentsInChildren<CardController>();
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == card)
            {
                return i;
            }
        }
        return -1;
    }

    public CardController GetCardAtZoneIndex(Transform zone, int index)
    {
        if (zone == null || index < 0) return null;
        var cards = zone.GetComponentsInChildren<CardController>();
        if (index >= cards.Length) return null;
        return cards[index];
    }

    public bool TryGetCardAddress(CardController card, out string zoneName, out int index)
    {
        zoneName = null;
        index = -1;
        if (card == null) return false;

        var zone = card.transform.parent;
        zoneName = GetZoneName(zone);
        index = GetCardIndexInZone(card, zone);
        return index >= 0 && !string.IsNullOrEmpty(zoneName);
    }

    // 送信側のゾーン名と番号から、受信側の同じ並びのカードを取る
    public CardController ResolveCardFromSender(string senderZoneName, int index)
    {
        if (string.IsNullOrEmpty(senderZoneName) || index < 0) return null;
        return GetCardAtZoneIndex(GetZoneByName(CovertOpponentZoneName(senderZoneName)), index);
    }

    /// <summary>
    /// ゾーン名からTransformを取得
    /// </summary>
    public Transform GetZoneByName(string zoneName)
    {
        switch (zoneName)
        {
            case "PHandZone": return pHandZone;
            case "PBattlerZone": return pBattlerZone;
            case "PTacticsZone": return pTacticsZone;
            case "PFieldZone": return pFieldZone;
            case "PGraveZone": return pGraveZone;
            case "EHandZone": return eHandZone;
            case "EBattlerZone": return eBattlerZone;
            case "ETacticsZone": return eTacticsZone;
            case "EFieldZone": return eFieldZone;
            case "EGraveZone": return eGraveZone;
            default: return null;
        }
    }

    /// <summary>
    /// Transformに対応するゾーン名を取得
    /// </summary>
    public string GetZoneName(Transform zone)
    {
        if (zone == pHandZone) return "PHandZone";
        if (zone == pBattlerZone) return "PBattlerZone";
        if (zone == pTacticsZone) return "PTacticsZone";
        if (zone == pFieldZone) return "PFieldZone";
        if (zone == pGraveZone) return "PGraveZone";
        if (zone == eHandZone) return "EHandZone";
        if (zone == eBattlerZone) return "EBattlerZone";
        if (zone == eTacticsZone) return "ETacticsZone";
        if (zone == eFieldZone) return "EFieldZone";
        if (zone == eGraveZone) return "EGraveZone";
        return zone != null ? zone.name : null;
    }

    /// <summary>
    /// 相手側の視点でのゾーン名へ変換する。PとEの接頭辞を入れ替える。
    /// </summary>
    public static string CovertOpponentZoneName(string zoneName)
    {
        if (string.IsNullOrEmpty(zoneName) || zoneName.Length < 2)
            return zoneName;

        char head = zoneName[0];
        if ((head != 'P' && head != 'E') || !char.IsUpper(zoneName[1]))
            return zoneName;

        return (head == 'P' ? "E" : "P") + zoneName.Substring(1);
    }

    /// <summary>
    /// 相手側の視点でのゾーン名を取得
    /// </summary>
    private string GetOpponentZoneName(Transform targetZone)
    {
        string zoneName = GetZoneName(targetZone);
        if (GetZoneByName(zoneName) == null)
            return zoneName;
        return CovertOpponentZoneName(zoneName);
    }

    /// <summary>
    /// カード操作の制限をUIに反映
    /// </summary>
    public void UpdateCardOperationUI()
    {
        bool canOperate = OnlineSyncManager.Instance.CanPlayerOperateCards();
        foreach (Transform zone in new[] { pHandZone, pBattlerZone, pTacticsZone, pFieldZone })
        {
            foreach (CardController card in zone.GetComponentsInChildren<CardController>())
                SetCardInteractable(card, canOperate);
        }
    }

    /// <summary>
    /// カードの操作可能状態を設定
    /// </summary>
    private void SetCardInteractable(CardController card, bool interactable)
    {
        // Buttonコンポーネントがある場合は無効化
        var button = card.GetComponent<UnityEngine.UI.Button>();
        if (button != null)
        {
            button.interactable = interactable;
        }
        
        // カードの色を変更して視覚的にフィードバック
        var image = card.GetComponent<UnityEngine.UI.Image>();
        if (image != null)
        {
            if (interactable)
            {
                image.color = Color.white;
            }
            else
            {
                image.color = new Color(0.7f, 0.7f, 0.7f, 0.8f); // グレーアウト
            }
        }
        
        // カードのコライダーを無効化
        var collider = card.GetComponent<Collider2D>();
        if (collider != null)
        {
            collider.enabled = interactable;
        }
    }
}
