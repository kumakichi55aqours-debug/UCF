using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 直前のスキルが渡した選択結果から、指定枚数だけを自分か相手に選ばせて次へ渡す。
/// カードの移動はしない。選ばれなかったカードは選択から外れるだけで、元の場所に残る。
/// </summary>
[System.Serializable]
[SkillDisplayName("選択から抽出")]
public class PickFromSelectionSkill : SkillDB
{
    [SerializeField] public int 抽出数 = 1;
    // プレイヤーは発動側、相手はその反対。ランダムがオンならUIを出さず無作為。
    [SerializeField] public PlayerType 選択プレイヤー = PlayerType.相手;
    [SerializeField] public bool ランダム = false;

    enum ChoiceMode
    {
        LocalUi,
        Remote,
        Random
    }

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context == null)
            return;

        if (!context.TryConsumeSelection(out var candidates) || candidates == null)
        {
            context.SetSelection(null);
            return;
        }

        var pool = new List<SelectionItem>();
        foreach (var item in candidates)
        {
            if (item != null)
                pool.Add(item);
        }

        int keep = Mathf.Max(0, 抽出数);
        List<SelectionItem> kept;
        if (keep == 0 || pool.Count == 0)
        {
            kept = null;
        }
        else if (pool.Count <= keep)
        {
            kept = pool;
        }
        else
        {
            int[] indices = await ChooseIndices(context, pool, keep);
            kept = TakeByIndices(pool, indices, keep);
        }

        context.SetSelection(kept);
    }

    async Task<int[]> ChooseIndices(SkillContext context, List<SelectionItem> pool, int keep)
    {
        switch (ResolveMode(context))
        {
            case ChoiceMode.LocalUi:
                if (CardSelectionUI.Instance == null)
                {
                    Debug.LogWarning("[選択から抽出] CardSelectionUIがないため、無作為に残します。");
                    return null;
                }

                var chosen = await CardSelectionUI.Instance.ShowIndexedAsync(pool, keep);
                return chosen != null ? chosen.ToArray() : null;

            case ChoiceMode.Remote:
                return await RequestRemotePick(context, pool, keep);

            default:
                Debug.Log($"[選択から抽出] 無作為に{keep}枚を残します。");
                return null;
        }
    }

    ChoiceMode ResolveMode(SkillContext context)
    {
        if (ランダム)
            return ChoiceMode.Random;

        bool ownerIsMine = context.発動カード == null || context.発動カード.isMyCard;
        bool chooserIsOwner = 選択プレイヤー != PlayerType.相手;
        bool chooserIsLocal = chooserIsOwner ? ownerIsMine : !ownerIsMine;

        if (!chooserIsLocal)
        {
            bool online = BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode;
            if (online && ownerIsMine)
                return ChoiceMode.Remote;
            return ChoiceMode.Random;
        }

        if (chooserIsOwner && SkillContext.IsOfflineCpuResolving)
            return ChoiceMode.Random;

        return ChoiceMode.LocalUi;
    }

    static async Task<int[]> RequestRemotePick(SkillContext context, List<SelectionItem> pool, int keep)
    {
        var sync = OnlineSyncManager.Instance;
        if (sync == null || !sync.IsOnlineMode || context.発動カード == null)
        {
            Debug.LogWarning("[選択から抽出] 相手への選択要求ができないため、無作為に残します。");
            return null;
        }

        var kinds = new int[pool.Count];
        var ids = new int[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            GetIdentity(pool[i], out CardKind kind, out int id);
            kinds[i] = (int)kind;
            ids[i] = id;
        }

        sync.BeginOperationBlock();
        sync.SyncBeginOpponentDecisionBlock();
        try
        {
            return await sync.RequestOpponentCardPick(context.発動カード, kinds, ids, keep);
        }
        finally
        {
            sync.EndOperationBlock();
            sync.SyncEndOpponentDecisionBlock();
        }
    }

    static void GetIdentity(SelectionItem item, out CardKind kind, out int id)
    {
        if (item != null && item.source == SelectionItem.SourceType.Existing && item.existingCard != null)
        {
            kind = item.existingCard.cardKind;
            id = item.existingCard.cardID;
            return;
        }

        if (item?.deckData != null)
        {
            kind = item.deckData.カード種類;
            id = item.deckData.カードID;
            return;
        }

        kind = CardKind.None;
        id = 0;
    }

    static List<SelectionItem> TakeByIndices(List<SelectionItem> pool, IReadOnlyList<int> indices, int keep)
    {
        var picked = new List<SelectionItem>();
        var used = new bool[pool.Count];
        if (indices != null)
        {
            foreach (int index in indices)
            {
                if ((uint)index >= (uint)pool.Count || used[index])
                    continue;

                used[index] = true;
                picked.Add(pool[index]);
                if (picked.Count >= keep)
                    return picked;
            }
        }

        var rest = new List<int>();
        for (int i = 0; i < pool.Count; i++)
        {
            if (!used[i])
                rest.Add(i);
        }

        for (int i = rest.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int swap = rest[i];
            rest[i] = rest[j];
            rest[j] = swap;
        }

        foreach (int index in rest)
        {
            if (picked.Count >= keep)
                break;
            picked.Add(pool[index]);
        }

        return picked;
    }
}
