using System.Collections;
using UnityEngine;
using Photon.Pun;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class OfflineBattleButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI buttonText;
    private OnlineBattleButton onlineBattleButton;
    private Button btn;
    private bool isStarting;
    
    void Awake()
    {
        btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnClick);
        
        // OnlineBattleButtonを検索
        onlineBattleButton = FindObjectOfType<OnlineBattleButton>();
    }
    
    void Update()
    {
        UpdateButtonText();
    }
    
    private void UpdateButtonText()
    {
        if (buttonText == null) return;
        
        // マッチング中またはルーム待機中の場合は「マッチキャンセル」を表示
        if (onlineBattleButton != null && onlineBattleButton.IsMatching)
        {
            buttonText.text = "マッチキャンセル";
        }
        else
        {
            buttonText.text = "CPUバトル";
        }
    }

    private void OnClick()
    {
        // マッチング中またはルーム待機中の場合は中断
        if (onlineBattleButton != null && onlineBattleButton.IsMatching)
        {
            onlineBattleButton.CancelMatching();
            return;
        }
        
        // 自分とCPUのデッキ選択UIを表示
        if (DeckSelectionUI.Instance != null)
        {
            DeckSelectionUI.Instance.Show(OnDecksSelected, "デッキを選択");
        }
        else
        {
            StartOfflineBattle(null, null);
        }
    }

    /// <summary>
    /// デッキ選択時のコールバック
    /// </summary>
    private void OnDecksSelected(string playerDeckName, string cpuDeckName)
    {
        StartOfflineBattle(playerDeckName, cpuDeckName);
    }

    /// <summary>
    /// オフラインバトルを開始
    /// </summary>
    private void StartOfflineBattle(string playerDeckName, string cpuDeckName)
    {
        if (isStarting) return;
        isStarting = true;
        BattleLaunchContext.ClearBattleSettings();

        if (!string.IsNullOrEmpty(playerDeckName))
            PlayerPrefs.SetString("SelectedDeckName", playerDeckName);

        if (string.IsNullOrEmpty(cpuDeckName))
            cpuDeckName = playerDeckName;
        if (string.IsNullOrEmpty(cpuDeckName))
            cpuDeckName = PlayerPrefs.GetString("SelectedDeckName", "");

        PlayerPrefs.SetString("CpuDeckName", cpuDeckName ?? "");
        PlayerPrefs.Save();

        PhotonNetwork.OfflineMode = true;
        StartCoroutine(LoadAfterFade());
    }

    private IEnumerator LoadAfterFade()
    {
        yield return ButtonPressFade.Wait(btn);
        SceneChanger.Load("BattleScene");
    }
}
