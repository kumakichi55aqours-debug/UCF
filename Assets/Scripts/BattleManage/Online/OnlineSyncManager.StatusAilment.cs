using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    public void SyncApplyStatus(string cardKind, int cardId, string zoneName, int index, int type, int turns, string sourceName)
    {
        if (!IsOnlineMode) return;
        SendRPC("ReceiveApplyStatus", cardKind, cardId, zoneName, index, type, turns, sourceName ?? string.Empty);
    }

    [PunRPC]
    private void ReceiveApplyStatus(string cardKind, int cardId, string zoneName, int index, int type, int turns, string sourceName)
    {
        if (BattleManager.Instance == null || !System.Enum.IsDefined(typeof(StatusAilmentType), type))
            return;

        WithResolvedCard(cardKind, cardId, zoneName, index, "ReceiveApplyStatusでエラー", card =>
        {
            // 付与RPCとは別に状態だけ届いたときも、残っている状態異常は外してから1つにする。
            card.RemoveGrantedStatusAilments();
            card.ApplyStatus((StatusAilmentType)type, turns, sourceName, syncOnline: false);
        });
    }
}
