using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class OnlineBattleButton : MonoBehaviourPunCallbacks
{
    [SerializeField] private TextMeshProUGUI buttonText;
    [SerializeField] private GameObject loadingIndicator;
    [SerializeField] private float rotationSpeed = 360f; // 1秒で1回転

    private Button btn;
    private bool isMatchingActive = false;

    /// <summary>
    /// マッチング中かどうか
    /// </summary>
    public bool IsMatching => isMatchingActive;

    void Awake()
    {
        btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnClick);

        PhotonNetwork.OfflineMode = true;
        UpdateUI();
    }

    void Update()
    {
        // ローディング中は画像を回転
        if (loadingIndicator != null && loadingIndicator.activeSelf)
        {
            loadingIndicator.transform.Rotate(0f, 0f, +rotationSpeed * Time.deltaTime);
        }
    }

    private void OnClick()
    {
        if (isMatchingActive) return;
        
        // デッキ選択UIを表示
        if (DeckSelectionUI.Instance != null)
        {
            DeckSelectionUI.Instance.Show(OnDeckSelected, "デッキを選択");
        }
        else
        {
            Debug.LogWarning("DeckSelectionUIが見つかりません。デフォルトデッキで開始します。");
            StartMatching(null);
        }
    }

    /// <summary>
    /// デッキ選択時のコールバック
    /// </summary>
    private void OnDeckSelected(string deckName)
    {
        StartMatching(deckName);
    }

    private void StartMatching(string deckName)
    {
        isMatchingActive = true;
        UpdateUI();

        PhotonNetwork.OfflineMode = false;
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.GameVersion = "1.0.0";
        PhotonNetwork.NetworkingClient.AppVersion = PhotonNetwork.GameVersion;

        PrepareDeckInfoForMatching(deckName);
        PhotonNetwork.ConnectUsingSettings();
    }

    /// <summary>
    /// マッチングをキャンセル
    /// </summary>
    public void CancelMatching()
    {
        if (!isMatchingActive) return;

        Debug.Log("マッチングを中断します");
        isMatchingActive = false;
        UpdateUI();

        // 切断処理
        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom();
        else if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();
        else
            PhotonNetwork.OfflineMode = true;
    }

    private void UpdateUI()
    {
        if (buttonText != null)
        {
            if (isMatchingActive)
                buttonText.text = PhotonNetwork.InRoom ? "ルーム待機中…" : "マッチング中…";
            else
                buttonText.text = "オンライン";
        }

        if (loadingIndicator != null)
            loadingIndicator.SetActive(isMatchingActive);

        if (isMatchingActive)
            ButtonPressFade.Apply(btn);
        else if (btn != null)
            btn.interactable = true;
    }

    #region Photon Callbacks

    public override void OnConnectedToMaster()
    {
        if (!isMatchingActive) return;

        Debug.Log($"マスターサーバーに接続 (Region: {PhotonNetwork.CloudRegion})");
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        if (!isMatchingActive) return;

        Debug.Log("既存ルームなし → 新規作成");
        var roomOptions = new RoomOptions
        {
            MaxPlayers = 2,
            IsVisible = true,
            IsOpen = true,
            PublishUserId = true,
            CleanupCacheOnLeave = true,
            EmptyRoomTtl = 0
        };
        PhotonNetwork.CreateRoom($"Room_{System.DateTime.Now:HHmmss}_{Random.Range(1000, 9999)}", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        if (!isMatchingActive) return;

        Debug.Log($"ルーム参加: {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/2)");
        UpdateUI();
        TryStartBattle();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"プレイヤー参加: {newPlayer.ActorNumber}");
        TryStartBattle();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("ルーム退出");
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"切断: {cause}");
        isMatchingActive = false;
        PhotonNetwork.OfflineMode = true;
        UpdateUI();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"ルーム作成失敗: {message}");
        ResetState();
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"ルーム参加失敗: {message}");
        ResetState();
    }

    #endregion

    private void TryStartBattle()
    {
        if (PhotonNetwork.CurrentRoom?.PlayerCount == 2 && PhotonNetwork.IsMasterClient)
        {
            PlayerPrefs.SetInt("HostIsFirstPlayer", 1);
            PlayerPrefs.Save();
            BattleLaunchContext.ClearBattleSettings();
            PhotonNetwork.LoadLevel("BattleScene");
        }
    }

    private void ResetState()
    {
        isMatchingActive = false;
        PhotonNetwork.OfflineMode = true;
        UpdateUI();
    }

    private void PrepareDeckInfoForMatching(string deckName = null)
    {
        try
        {
            // デッキ名が指定されていない場合はPlayerPrefsから取得
            if (string.IsNullOrEmpty(deckName))
            {
                deckName = PlayerPrefs.GetString("SelectedDeckName", "");
            }
            
            string deckPath;
            if (!string.IsNullOrEmpty(deckName))
            {
                // 指定されたデッキ名でファイルを読み込む
                string fileName = $"deck_{SanitizeFileName(deckName)}.json";
                deckPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Decks", fileName);
            }
            else
            {
                // デフォルトのデッキファイルを読み込む
                deckPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Decks", "deck.json");
            }
            
            if (System.IO.File.Exists(deckPath))
            {
                PlayerPrefs.SetString("PlayerDeckData", System.IO.File.ReadAllText(deckPath));
                PlayerPrefs.Save();
            }
            else
            {
                Debug.LogWarning("デッキファイルが見つかりません: " + deckPath);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"デッキ準備エラー: {ex.Message}");
        }
    }
    
    // ファイル名として使用できない文字を置換
    private string SanitizeFileName(string fileName)
    {
        char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }
}
