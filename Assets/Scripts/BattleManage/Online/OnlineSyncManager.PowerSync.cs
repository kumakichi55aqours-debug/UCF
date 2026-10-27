using Photon.Pun;
using UnityEngine;

/// <summary>
/// カードの基本パワーと、全体パワーボーナスの同期。
/// </summary>
public partial class OnlineSyncManager
{
    /// <summary>
    /// 基本パワーの増減を、変更したカードの位置とともに相手へ伝える。
    /// 受信側は同じ並びのカードへ同じ差分を足す。
    /// </summary>
    public void SyncBasePower(string cardKind, int cardId, string zoneName, int index, int delta1, int delta2)
    {
        if (!IsOnlineMode || (delta1 == 0 && delta2 == 0))
            return;

        SendRPC("ReceiveBasePower", cardKind, cardId, zoneName, index, delta1, delta2);
    }

    [PunRPC]
    private void ReceiveBasePower(string cardKind, int cardId, string zoneName, int index, int delta1, int delta2)
    {
        if (BattleManager.Instance == null)
            return;

        var card = CardAddress.Resolve(cardKind, cardId, zoneName, index);
        if (card == null)
        {
            return;
        }

        card.basePower1 += delta1;
        card.basePower2 += delta2;
        card.RecalculateModifiedPowers();
        card.UpdateDisplay();
    }

    /// <summary>
    /// パワーボーナスのスケジュールを同期
    /// </summary>
    public void SyncSchedulePowerBonus(bool forPlayer, int amount, int durationTurns, int startTurn, string sourceCardName)
    {
        if (IsOnlineMode)
        {
            // オンライン対戦時は、相手側の視点でforPlayerを反転
            // 自分のプレイヤーへのボーナス = 相手側から見た敵へのボーナス
            bool opponentForPlayer = !forPlayer;
            SendRPC("ReceiveSchedulePowerBonus", opponentForPlayer, amount, durationTurns, startTurn, sourceCardName ?? "");
        }
    }

    /// <summary>
    /// パワーボーナスのスケジュールを受信
    /// </summary>
    [PunRPC]
    private void ReceiveSchedulePowerBonus(bool forPlayer, int amount, int durationTurns, int startTurn, string sourceCardName)
    {
        if (IsOnlineMode && BattleManager.Instance != null)
        {
            // syncOnline=falseで呼び出して、再帰的な同期を防ぐ
            BattleManager.Instance.SchedulePowerBonusInternal(forPlayer, amount, durationTurns, startTurn, sourceCardName, false);
        }
    }

    /// <summary>
    /// リソースバフを相手の視点へ同期する。受信側はすぐ適用する。
    /// コストは以降のリセットでも維持し、エネルギーはターン番号の期間だけ維持する。
    /// 持続ターン数0は、そのターンだけ適用してバフ表示には出さない。
    /// </summary>
    public void SyncScheduleResourceBuff(bool forPlayer, int resourceType, int amount, int durationTurns, string sourceCardName)
    {
        if (!IsOnlineMode) return;

        bool opponentForPlayer = !forPlayer;
        SendRPC("ReceiveScheduleResourceBuff", opponentForPlayer, resourceType, amount, durationTurns, sourceCardName ?? "");
    }

    [PunRPC]
    private void ReceiveScheduleResourceBuff(bool forPlayer, int resourceType, int amount, int durationTurns, string sourceCardName)
    {
        if (!IsOnlineMode || BattleManager.Instance == null) return;

        BattleManager.Instance.ScheduleResourceBuff(
            forPlayer,
            (ResourceType)resourceType,
            amount,
            durationTurns,
            sourceCardName,
            false);
    }

    /// <summary>
    /// カードを出すときのコスト補正を、相手の視点へ同期する。
    /// </summary>
    public void SyncSchedulePlayCostBuff(bool forPlayer, int cardKind, int amount, int durationTurns, int startTurn, string sourceCardName)
    {
        if (!IsOnlineMode) return;

        SendRPC(
            "ReceiveSchedulePlayCostBuff",
            !forPlayer,
            cardKind,
            amount,
            durationTurns,
            startTurn,
            sourceCardName ?? "");
    }

    [PunRPC]
    void ReceiveSchedulePlayCostBuff(bool forPlayer, int cardKind, int amount, int durationTurns, int startTurn, string sourceCardName)
    {
        if (!IsOnlineMode || BattleManager.Instance == null) return;

        BattleManager.Instance.SchedulePlayCostBuff(
            forPlayer,
            (CardKind)cardKind,
            amount,
            durationTurns,
            startTurn,
            sourceCardName,
            false);
    }
}
