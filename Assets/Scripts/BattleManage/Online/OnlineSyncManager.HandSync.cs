using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 初期手札の内容を相手へ送り、相手手札の一覧を保持する。
/// </summary>
public partial class OnlineSyncManager
{
    int opponentHandVersion;
    int consumedHandVersion;
    bool opponentInitialHandReady;
    bool opponentSawOurInitialHand;

    /// <summary>
    /// 初期手札の同期。4枚ドロー中に届いた途中の手札では完了にしない。
    /// </summary>
    public async Task SyncInitialHands()
    {
        while (true)
        {
            if (opponentInitialHandReady)
            {
                SendRPC("ReceiveInitialHandReady", true);
            }
            else
            {
                SendDetailedHandInfoToOpponent();
                SyncDeckCountToOpponent();
                SendRPC("ReceiveInitialHandReady", false);
            }

            if (opponentInitialHandReady && opponentSawOurInitialHand)
                break;
            await Task.Delay(100);
        }

        consumedHandVersion = opponentHandVersion;
        BattleManager.Instance.UpdateDeckUI();
    }

    [PunRPC]
    private void ReceiveInitialHandReady(bool senderHasOurHand)
    {
        opponentInitialHandReady = true;
        if (senderHasOurHand)
            opponentSawOurInitialHand = true;
    }

    /// <summary>
    /// 相手の手札を取得
    /// </summary>
    public List<DeckCard> GetOpponentHand()
    {
        return opponentHand;
    }

    /// <summary>
    /// 相手の手札情報を受信済みかどうか
    /// </summary>
    public bool IsOpponentHandReceived => isOpponentHandReceived;

    /// <summary>
    /// 手札の詳細情報を送信
    /// </summary>
    public void SendDetailedHandInfoToOpponent()
    {
        try
        {
            var handInfo = new List<object>();
            CardController[] handCards = BattleManager.Instance.pHandZone.GetComponentsInChildren<CardController>();

            for (int i = 0; i < handCards.Length; i++)
            {
                var card = handCards[i];
                if (card == null)
                    continue;

                handInfo.Add(i);
                handInfo.Add(card.cardKind.ToString());
                handInfo.Add(card.cardID);
            }

            // Photonは可変長引数として展開するため、配列は1引数にラップする
            SendRPC("ReceiveDetailedOpponentHandInfo", new object[] { handInfo.ToArray() });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"手札詳細情報送信でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 手札の詳細情報を受信
    /// </summary>
    [PunRPC]
    private void ReceiveDetailedOpponentHandInfo(object[] handInfo)
    {
        try
        {
            opponentHand.Clear();
            ReadCardTriples(handInfo, (first, second, third) =>
            {
                if (!(first is int) || !(second is string cardKindStr) || !(third is int cardID))
                {
                    return;
                }

                AddParsedDeckCard(opponentHand, cardKindStr, cardID, 1);
            });

            isOpponentHandReceived = true;
            opponentHandVersion++;
            BattleManager.Instance.UpdateEnemyHandUI();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"相手の手札詳細情報受信でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// まだ受け取っていない相手手札が届くまで待つ。先に届いていた分は消さない。
    /// </summary>
    public async Task WaitForOpponentHandInfo()
    {
        int baseline = consumedHandVersion;
        while (opponentHandVersion <= baseline)
            await Task.Delay(50);
        consumedHandVersion = opponentHandVersion;
    }

    // 手札・墓地の一覧RPCは (値, 値, 値) を平坦に並べて送る
    private static void ReadCardTriples(object[] payload, System.Action<object, object, object> read)
    {
        for (int i = 0; i + 2 < payload.Length; i += 3)
            read(payload[i], payload[i + 1], payload[i + 2]);
    }

    private static void AddParsedDeckCard(List<DeckCard> destination, string cardKindStr, int cardID, int cost)
    {
        if (!System.Enum.TryParse(cardKindStr, out CardKind cardKind))
        {
            return;
        }

        var deckCard = new DeckCard(cardKind, cardID, cost);
        deckCard.PopulateDerivedData();
        destination.Add(deckCard);
    }
}
