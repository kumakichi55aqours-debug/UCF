using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Photon.Pun;
/// <summary>
/// オンライン対戦時のパッシブスキル（GrantSkill）同期を専門に管理するクラス
/// 目的：GrantSkillが発動した時に、①対象のカード、②持続ターン数、③付与されたスキルのインスタンスを同期
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class GrantSkillSyncManager : MonoBehaviourPunCallbacks
{
    #region シングルトン

    public static GrantSkillSyncManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[GrantSkillSyncManager] インスタンスを初期化しました");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region RPC送受信

    /// <summary>
    /// 複数対象への付与を、動かす前の住所でまとめて同期する。
    /// スキル名やタイミングは JSON に含まれる。型名だけは JSON に無いので別で送る。
    /// </summary>
    public void SyncGrantSkillBatch(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices,
        int[] durations, string[] skillTypes, string[] parametersJsons, string sourceCardName)
    {
        if (OnlineSyncManager.Instance?.IsOnlineMode != true) return;
        if (cardKinds == null || cardIds == null || zoneNames == null || indices == null) return;
        if (cardKinds.Length == 0
            || cardKinds.Length != cardIds.Length
            || cardKinds.Length != zoneNames.Length
            || cardKinds.Length != indices.Length)
            return;

        SendRPC("ReceiveGrantSkillBatch",
            cardKinds, cardIds, zoneNames, indices, durations, skillTypes, parametersJsons,
            sourceCardName ?? string.Empty);
    }

    [PunRPC]
    private void ReceiveGrantSkillBatch(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices,
        int[] durations, string[] skillTypes, string[] parametersJsons, string sourceCardName)
    {
        try
        {
            int count = CardAddress.Count(cardKinds, cardIds, zoneNames, indices);
            var targets = new List<CardController>(count);
            for (int i = 0; i < count; i++)
            {
                var card = CardAddress.ResolveAt(cardKinds, cardIds, zoneNames, indices, i);
                if (card == null)
                    Debug.LogWarning($"[GrantSkillSyncManager] 付与対象が見つかりません: {zoneNames[i]} index:{indices[i]}");
                targets.Add(card);
            }

            _ = ExecuteGrantSkillBatch(targets, durations, skillTypes, parametersJsons, sourceCardName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GrantSkillSyncManager] 付与スキル一括受信でエラー: {ex.Message}");
        }
    }

    async Task ExecuteGrantSkillBatch(List<CardController> targets, int[] durations,
        string[] skillTypes, string[] parametersJsons, string sourceCardName)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null) continue;

            int duration = durations != null && i < durations.Length ? durations[i] : 0;
            var grantedSkill = CreateSkillFromJson(At(skillTypes, i), At(parametersJsons, i));
            if (grantedSkill == null) continue;

            await ExecuteGrantSkillOnOpponent(target, grantedSkill, duration, sourceCardName);
        }
    }

    static string At(string[] values, int index)
    {
        if (values == null || index < 0 || index >= values.Length) return string.Empty;
        return values[index] ?? string.Empty;
    }

    /// <summary>
    /// 相手側で付与スキルを実行
    /// </summary>
    private async Task ExecuteGrantSkillOnOpponent(CardController targetCard, SkillDB skillInstance, int duration, string sourceCardName)
    {
        try
        {
            if (skillInstance == null)
            {
                Debug.LogError($"[GrantSkillSyncManager] 付与スキルがnullです");
                return;
            }
            
            OnlineSyncManager.Instance?.BeginOperationBlock();

            SkillContext context = new SkillContext(targetCard, targetCard, targetCard.transform);
            await skillInstance.ActivateEffect(context);
            
            targetCard.AddPassiveEffect(new ActivePassiveEffect(skillInstance, duration, sourceCardName));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GrantSkillSyncManager] 相手側付与スキル実行でエラー: {ex.Message}");
            Debug.LogError($"[GrantSkillSyncManager] スタックトレース: {ex.StackTrace}");
        }
        finally
        {
            OnlineSyncManager.Instance?.EndOperationBlock();
        }
    }

    /// <summary>
    /// RPC送信
    /// </summary>
    private void SendRPC(string methodName, params object[] parameters)
    {
        if (OnlineSyncManager.Instance?.IsOnlineMode == true)
        {
            try
            {
                photonView.RPC(methodName, RpcTarget.Others, parameters);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GrantSkillSyncManager] RPC送信でエラー: {ex.Message}");
            }
        }
    }

    #endregion

    #region シリアライズ/デシリアライズ

    /// <summary>
    /// JSON文字列からスキルデータを作成
    /// </summary>
    private SkillDB CreateSkillFromJson(string skillType, string parametersJson)
    {
        try
        {
            var skill = GrantSkillSerializer.Deserialize(skillType, parametersJson);
            if (skill == null) return null;

            skill.上限回数 = 1;
            skill.強制発動 = false;
            skill.使用回数 = 0;
            return skill;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GrantSkillSyncManager] スキルデータ作成でエラー: {ex.Message}");
            return null;
        }
    }

    #endregion
}
