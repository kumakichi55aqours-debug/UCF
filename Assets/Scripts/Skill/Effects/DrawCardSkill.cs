using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("ドロー")]
public class DrawCardSkill : SkillDB
{
    public int ドロー数;

    [Tooltip("プレイヤーは発動したカードの持ち主。エネミーはその相手。両方ともは、それぞれがドロー数だけ引く")]
    public PlayerType 対象プレイヤー = PlayerType.プレイヤー;

    bool isExecuting;

    public override async Task ActivateEffect(SkillContext context)
    {
        if (isExecuting)
            return;

        isExecuting = true;

        int requestedCount = ドロー数;
        if (context != null && context.IntVariables != null && context.IntVariables.TryGetValue("DrawCount", out var ctxCount))
        {
            if (ctxCount > 0)
                requestedCount = ctxCount;
        }

        try
        {
            if (requestedCount <= 0 || BattleManager.Instance == null)
                return;

            bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
            bool drawOwner = 対象プレイヤー != PlayerType.相手;
            bool drawOpponent = 対象プレイヤー == PlayerType.相手 || 対象プレイヤー == PlayerType.両方;

            if (drawOwner)
                await DrawForSide(ownerIsLocal, requestedCount);
            if (drawOpponent)
                await DrawForSide(!ownerIsLocal, requestedCount);
        }
        finally
        {
            isExecuting = false;
        }
    }

    static async Task DrawForSide(bool localSide, int count)
    {
        if (localSide)
        {
            await DrawLocalPlayer(count);
            return;
        }

        var battle = BattleManager.Instance;
        if (battle != null && battle.IsOnlineMode && OnlineSyncManager.Instance != null)
        {
            await OnlineSyncManager.Instance.RequestOpponentSkillDraw(count);
            return;
        }

        await DrawForCPU(count);
    }

    /// <summary>
    /// このクライアントのデッキから、このクライアントの手札へ引く。
    /// </summary>
    public static async Task DrawLocalPlayer(int requestedCount)
    {
        var battle = BattleManager.Instance;
        if (battle == null || battle.pBattleDeck.Count == 0 || requestedCount <= 0)
            return;

        int actualDrawCount = Mathf.Min(requestedCount, battle.pBattleDeck.Count);
        for (int i = 0; i < actualDrawCount; i++)
        {
            if (battle.pBattleDeck.Count == 0)
                break;

            var drawn = battle.pBattleDeck[0];
            battle.pBattleDeck.RemoveAt(0);

            var card = battle.CreateCard(
                drawn.カード種類,
                drawn.カードID,
                battle.mainCanvas.transform
            );
            card.Init(drawn.カード種類, drawn.カードID);

            await CardAnimationManager.Instance.AnimateDraw(
                battle.pDeckZone.GetComponent<RectTransform>(),
                battle.pHandZone.GetComponent<RectTransform>(),
                card);

            card.transform.SetParent(battle.pHandZone, false);
            card.UpdateDisplay();
            if (!battle.IsOnlineMode)
                await Task.Delay(200);

            if (battle.IsOnlineMode && OnlineSyncManager.Instance != null)
            {
                OnlineSyncManager.Instance.SyncAddToHandAnimation(drawn.カード種類.ToString(), drawn.カードID, (int)SummonSource.墓地, false);
                await OnlineSyncManager.Instance.WaitForOpponentAnimation();
                OnlineSyncManager.Instance.SendDetailedHandInfoToOpponent();
                OnlineSyncManager.Instance.SyncDeckCountToOpponent();
            }
        }
    }

    static async Task DrawForCPU(int requestedCount)
    {
        var battle = BattleManager.Instance;
        if (battle == null || battle.eBattleDeck.Count == 0 || requestedCount <= 0)
            return;

        int actualDrawCount = Mathf.Min(requestedCount, battle.eBattleDeck.Count);
        for (int i = 0; i < actualDrawCount; i++)
        {
            if (battle.eBattleDeck.Count == 0)
                break;

            await battle.EnemyDrawCardAsync();
            await Task.Delay(200);
        }
    }
}
