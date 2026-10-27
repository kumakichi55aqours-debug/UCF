using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    public void SyncCleanse(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int categories, int[] statusTypes)
    {
        if (!IsOnlineMode || cardKinds == null || cardIds == null || zoneNames == null || indices == null)
            return;
        if (cardKinds.Length == 0
            || cardKinds.Length != cardIds.Length
            || cardKinds.Length != zoneNames.Length
            || cardKinds.Length != indices.Length)
            return;

        SendRPC("ReceiveCleanse", cardKinds, cardIds, zoneNames, indices, categories, statusTypes ?? System.Array.Empty<int>());
    }

    [PunRPC]
    private void ReceiveCleanse(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int categories, int[] statusTypes)
    {
        if (BattleManager.Instance == null)
            return;

        var category = (CleanseCategory)categories;
        var filter = ToStatusFilter(statusTypes);
        int count = CardAddress.Count(cardKinds, cardIds, zoneNames, indices);
        for (int i = 0; i < count; i++)
        {
            var card = CardAddress.ResolveAt(cardKinds, cardIds, zoneNames, indices, i);
            if (card == null)
            {
                continue;
            }

            card.ApplyCleanse(category, filter);
        }

        if (CardController.CleanseNeedsRecalc(category))
            PersistentEffectManager.Instance?.QueueRecalculation();
    }

    static List<StatusAilmentType> ToStatusFilter(int[] statusTypes)
    {
        if (statusTypes == null || statusTypes.Length == 0)
            return null;

        var filter = new List<StatusAilmentType>();
        foreach (int type in statusTypes)
        {
            if (!System.Enum.IsDefined(typeof(StatusAilmentType), type))
                continue;
            filter.Add((StatusAilmentType)type);
        }
        return filter.Count > 0 ? filter : null;
    }
}
