using System.Collections;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ReturnToTitleButton : MonoBehaviourPunCallbacks
{
    private Button btn;
    private bool isLeaving;

    void Awake()
    {
        btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(ToTitle);
    }

    void Start()
    {
        if (!BattleLaunchContext.ReturnsToDeckEdit)
            return;

        TextMeshProUGUI label = GetComponentInChildren<TextMeshProUGUI>();
        if (label == null)
            return;

        label.text = "デッキ編集に戻る";
        label.enableAutoSizing = true;
        label.fontSizeMin = 18f;
        label.fontSizeMax = 28f;
    }

    /// <summary> Battle/DeckEdit から タイトル </summary>
    public void ToTitle()
    {
        if (isLeaving) return;
        isLeaving = true;
        StartCoroutine(LeaveAfterFade());
    }

    private IEnumerator LeaveAfterFade()
    {
        yield return ButtonPressFade.Wait(btn);

        // ① オンラインルームにいるなら、まずルームを抜ける
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LeaveRoom();
        }
        else
        {
            // ② オフライン、または既にルームを出ているならタイトルへ
            LoadTitleScene();
        }
    }

    public override void OnLeftRoom()
    {
        // LeaveRoom の完了を受け取ってタイトルへ
        LoadTitleScene();
    }

    private void LoadTitleScene()
    {
        // DontDestroyOnLoadしているPhotonViewが残るのを防ぐため、明示的に破棄
        if (GrantSkillSyncManager.Instance != null)
        {
            Destroy(GrantSkillSyncManager.Instance.gameObject);
        }

        // Disconnect中にシーン遷移するとSetPropertiesエラーが出るため、先に無効化
        PhotonNetwork.AutomaticallySyncScene = false;

        // オンラインモードを完全に切断してオフラインモードに戻す
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();
        
        PhotonNetwork.OfflineMode = true;
        BattleLaunchContext.LoadReturnScene();
    }
}
