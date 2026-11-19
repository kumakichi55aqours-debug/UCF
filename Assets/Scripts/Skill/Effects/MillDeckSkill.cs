using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// デッキの上から指定枚数のカードを墓地へ送る。
/// 対象プレイヤーは発動カードの持ち主を基準に判定する。
/// </summary>
[System.Serializable]
[SkillDisplayName("デッキ墓地送り")]
public class MillDeckSkill : SkillDB
{
    [Header("墓地送り設定")]
    public int 墓地送り枚数 = 3;

    public PlayerType 対象プレイヤー = PlayerType.プレイヤー;

    public override async Task ActivateEffect(SkillContext context)
    {
        var battle = BattleManager.Instance;

        if (battle == null || 墓地送り枚数 <= 0)
            return;

        // 発動カードの持ち主を基準に、自分側か相手側かを判定
        bool ownerIsLocal =
            context?.発動カード == null ||
            context.発動カード.isMyCard;

        bool targetIsLocal =
            対象プレイヤー == PlayerType.相手
                ? !ownerIsLocal
                : ownerIsLocal;

        // 1. 自分のデッキを対象にする場合
        if (targetIsLocal)
        {
            var deck = battle.pBattleDeck;

            if (deck == null || deck.Count == 0)
                return;

            var topCards = deck
                .Take(Mathf.Min(墓地送り枚数, deck.Count))
                .ToList();

            await ToGraveSkill.SendOwnDeckCardsToGrave(topCards);
            return;
        }

        // 2. 相手のデッキを対象にする場合
        if (battle.IsOnlineMode)
        {
            // オンラインでは相手自身の端末に処理を依頼
            var sync = OnlineSyncManager.Instance;

            if (sync == null)
                return;

            await sync.RequestOpponentTopDeckToGrave(墓地送り枚数);
            return;
        }

        // 3. オフラインではCPU側のデッキを直接処理
        await SendEnemyTopDeckToGrave(
            battle,
            墓地送り枚数);
    }

    /// <summary>
    /// オフライン用。CPU側のデッキ上部からカードを墓地へ送る。
    /// </summary>
    async Task SendEnemyTopDeckToGrave(
        BattleManager battle,
        int count)
    {
        var deck = battle.eBattleDeck;

        if (deck == null || deck.Count == 0)
            return;

        int take = Mathf.Min(count, deck.Count);

        var cards = new List<DeckCard>(take);

        // デッキの中身を先に取り除く
        for (int i = 0; i < take; i++)
        {
            cards.Add(deck[0]);
            deck.RemoveAt(0);
        }

        // アニメーション完了までデッキ枚数の減少を管理
        battle.ReserveDeckDepartures(false, take);

        try
        {
            foreach (var deckData in cards)
            {
                if (deckData == null)
                {
                    battle.CompleteDeckDeparture(false);
                    continue;
                }

                CardController card = null;

                try
                {
                    card = battle.CreateCard(
                        deckData.カード種類,
                        deckData.カードID,
                        battle.eDeckZone);

                    if (card == null)
                        continue;

                    // トークンは墓地に残さず消滅させる
                    if (card.トークン)
                    {
                        TokenExit.VanishLocal(card);
                        continue;
                    }

                    // デッキが0枚になっても移動中のカードが消えないようにする
                    if (battle.mainCanvas != null)
                    {
                        card.transform.SetParent(
                            battle.mainCanvas.transform,
                            true);
                    }

                    // デッキを離れたタイミングで枚数を更新
                    battle.CompleteDeckDeparture(false);

                    await CardAnimationManager.Instance.AnimateDiscard(
                        battle.eDeckZone as RectTransform,
                        battle.eGraveZone as RectTransform,
                        card);

                    battle.UpdateDeckUI();
                }
                finally
                {
                    // カード生成に失敗した場合などの予約解除
                    // 既にComplete済みの場合は追加で減らさない
                }
            }
        }
        finally
        {
            // 未完了の予約を解除
            battle.CancelDeckDepartures(false, int.MaxValue);
            battle.UpdateDeckUI();
        }
    }
}
