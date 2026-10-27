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
        if (BattleManager.Instance == null) return;
        var card = CardAddress.Resolve(cardKind, cardId, zoneName, index);
        if (card == null)
        {
            return;
        }

        if (!System.Enum.IsDefined(typeof(StatusAilmentType), type))
            return;

        card.ApplyStatus((StatusAilmentType)type, turns, sourceName, syncOnline: false);
    }
}
