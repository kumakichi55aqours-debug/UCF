using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// デッキ枚数と、対戦開始時のデッキ内容の同期。
/// 各自が LoadDeck でシャッフルした自分のデッキを相手へ送り、受け取った側は eBattleDeck にそのまま置く。
/// </summary>
public partial class OnlineSyncManager
{
    // デッキ枚数を相手に送信
    public void SyncDeckCountToOpponent()
    {
        int deckCount = BattleManager.Instance.pBattleDeck.Count;
        SendRPC("ReceiveOpponentDeckCount", deckCount);
    }

    // 相手のデッキ枚数を受信
    [PunRPC]
    private void ReceiveOpponentDeckCount(int deckCount)
    {
        BattleManager.Instance.eDeckCount = deckCount;
        BattleManager.Instance.UpdateDeckUI();
    }

    /// <summary>
    /// デッキの中身を同期する。
    /// クライアントが自分のデッキを送り、ホストは受け取ってから自分のデッキを返す。
    /// </summary>
    public async Task SyncDecks()
    {
        if (IsHostPlayer)
        {
            // 待ちを作る前に届いたデッキは完了のまま残す。ここで消すと再送されない。
            await WaitForDeckSync(asHost: true);
            SendDeckToClient();
        }
        else
        {
            deckSyncCompleted.Reset();
            SendDeckToHost();
            await WaitForDeckSync(asHost: false);
        }
    }


    // デッキ同期完了を待機
    private async Task WaitForDeckSync(bool asHost)
    {
        if (!IsOnlineMode || IsHostPlayer != asHost)
            return;

        await deckSyncCompleted.WaitForCompletion();
    }

    private void SendDeckToHost()
    {
        if (IsHostPlayer)
            return;

        SendDeck("ReceiveClientDeck", "クライアントのデッキ送信でエラー");
    }

    private void SendDeckToClient()
    {
        if (!IsOnlineMode || !IsHostPlayer)
            return;

        SendDeck("ReceiveHostDeck", "ホストのデッキ送信でエラー");
    }

    // デッキ送信用
    private void SendDeck(string rpcName, string errorLabel)
    {
        try
        {
            // params object[] は配列を展開する。デッキ全体を引数1個に包む。
            object[] deck = EncodeDeck(BattleManager.Instance.pBattleDeck);
            SendRPC(rpcName, new object[] { deck });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{errorLabel}: {ex.Message}");
        }
    }

    // デッキ受信用
    private void ApplyOpponentDeck(object[] deck, bool acceptOnHost, string errorLabel)
    {
        if (IsHostPlayer != acceptOnHost)
            return;

        try
        {
            SetEnemyDeck(deck);
            CompleteDeckSync();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{errorLabel}: {ex.Message}");
        }
    }

    // デッキ受信用（ホスト用）クライアントのデッキを受信
    [PunRPC]
    private void ReceiveClientDeck(object[] deck)
    {
        ApplyOpponentDeck(deck, acceptOnHost: true, "クライアントのデッキ受信でエラー");
    }

    // デッキ受信用（クライアント用）ホストのデッキを受信
    [PunRPC]
    private void ReceiveHostDeck(object[] deck)
    {
        ApplyOpponentDeck(deck, acceptOnHost: false, "ホストのデッキ受信でエラー");
    }

    // pBattleDeckを送信タイプに変換
    private object[] EncodeDeck(List<DeckCard> deck)
    {
        var deckInfo = new List<object>();
        foreach (var card in deck)
        {
            if (card == null)
                continue;

            deckInfo.Add(new object[] { card.カード種類.ToString(), card.カードID, card.コスト });
        }
        return deckInfo.ToArray();
    }

    // 受信されたものをeBattleDeckに変換
    private void SetEnemyDeck(object[] deckInfo)
    {
        BattleManager.Instance.eBattleDeck.Clear();
        if (deckInfo == null)
            return;

        foreach (object cardInfo in deckInfo)
        {
            if (!(cardInfo is object[] cardData) || cardData.Length < 3
                || !(cardData[0] is string cardKindStr) || !(cardData[1] is int cardID) || !(cardData[2] is int cost))
            {
                continue;
            }

            if (!System.Enum.TryParse(cardKindStr, out CardKind cardKind))
            {
                continue;
            }

            var deckCard = new DeckCard(cardKind, cardID, cost);
            deckCard.PopulateDerivedData();
            BattleManager.Instance.eBattleDeck.Add(deckCard);
        }
    }

    // デッキ同期完了処理
    private void CompleteDeckSync()
    {
        if (deckSyncCompleted.IsComplete)
            return;

        BattleManager.Instance.eDeckCount = BattleManager.Instance.eBattleDeck.Count;
        BattleManager.Instance.UpdateDeckUI();
        deckSyncCompleted.SetComplete();
    }

    TaskCompletionSource<bool> skillDrawFinished;

    /// <summary>
    /// 相手のクライアントに、相手自身のデッキから指定枚数引かせる。
    /// </summary>
    public async Task RequestOpponentSkillDraw(int count)
    {
        if (!IsOnlineMode || count <= 0)
            return;

        skillDrawFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginOperationBlock();
        try
        {
            SendRPC("ReceiveSkillDrawRequest", count);
            await skillDrawFinished.Task;
        }
        finally
        {
            EndOperationBlock();
        }
    }

    [PunRPC]
    void ReceiveSkillDrawRequest(int count)
    {
        _ = ResolveSkillDrawRequest(count);
    }

    async Task ResolveSkillDrawRequest(int count)
    {
        BeginOperationBlock();
        try
        {
            await DrawCardSkill.DrawLocalPlayer(count);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] スキルドローの受信でエラー: {ex.Message}");
        }
        finally
        {
            EndOperationBlock();
            SendRPC("ReceiveSkillDrawFinished");
        }
    }

    [PunRPC]
    void ReceiveSkillDrawFinished()
    {
        skillDrawFinished?.TrySetResult(true);
    }
}
