using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    /// <summary>
    /// デッキ外から出したトークンを、相手のBゾーンへ作る。
    /// targetZoneName は相手から見たゾーン名。
    /// </summary>
    public void SyncTokenSummon(int cardId, string targetZoneName)
    {
        SendRPC("ReceiveTokenSummon", cardId, targetZoneName ?? string.Empty);
    }

    [PunRPC]
    private void ReceiveTokenSummon(int cardId, string targetZoneName)
    {
        if (!IsOnlineMode || BattleManager.Instance == null)
            return;

        _ = PlayReceivedTokenSummon(cardId, targetZoneName);
    }

    async Task PlayReceivedTokenSummon(int cardId, string targetZoneName)
    {
        if (!TryResolveSummonZone(targetZoneName, "トークン", out BattleManager battle, out Transform zone))
            return;

        Transform parent = battle.mainCanvas != null ? battle.mainCanvas.transform : zone;
        CardController card = battle.CreateCard(CardKind.バトラー, cardId, parent);
        if (card == null || !card.トークン)
        {
            if (card != null)
                Destroy(card.gameObject);
            return;
        }

        await battle.SummonRoutine(card, zone, true, SummonSource.生成, openAppearanceChain: true, specialSummon: true);
    }

    /// <summary>
    /// 場のバトラーをコピーしたトークンを、相手のBゾーンへ作る。
    /// targetZoneName は相手から見たゾーン名。
    /// setHp が真のとき、受け側でも現在HPと最大HPを hp にする。
    /// </summary>
    public void SyncCloneTokenSummon(int cardId, string targetZoneName, bool setHp, int hp)
    {
        SendRPC("ReceiveCloneTokenSummon", cardId, targetZoneName ?? string.Empty, setHp, hp);
    }

    [PunRPC]
    private void ReceiveCloneTokenSummon(int cardId, string targetZoneName, bool setHp, int hp)
    {
        if (!IsOnlineMode || BattleManager.Instance == null)
            return;

        _ = PlayReceivedCloneTokenSummon(cardId, targetZoneName, setHp, hp);
    }

    async Task PlayReceivedCloneTokenSummon(int cardId, string targetZoneName, bool setHp, int hp)
    {
        if (!TryResolveSummonZone(targetZoneName, "分身トークン", out BattleManager battle, out Transform zone))
            return;

        await CloneTokenSkill.Summon(battle, cardId, zone, setHp, hp);
    }

    bool TryResolveSummonZone(string targetZoneName, string label, out BattleManager battle, out Transform zone)
    {
        battle = BattleManager.Instance;
        zone = null;
        if (battle == null)
            return false;

        zone = battle.GetZoneByName(targetZoneName);
        if (zone != null)
            return true;

        Debug.LogError($"{label}の召喚先が見つかりません: {targetZoneName}");
        return false;
    }

    /// <summary>
    /// トークンが墓地・手札・デッキへ行かず消えたことを相手へ送る。
    /// 住所は消す前の、送信者から見た場所。
    /// </summary>
    public void SyncTokenVanish(string cardKindStr, int cardID, string zoneName, int zoneIndex, bool playExplosion)
    {
        SendAnimatedRpc("ReceiveTokenVanish", cardKindStr, cardID, zoneName, zoneIndex, playExplosion);
    }

    [PunRPC]
    private void ReceiveTokenVanish(string cardKindStr, int cardID, string zoneName, int zoneIndex, bool playExplosion)
    {
        ReceiveAnimatedCardAction(cardKindStr, cardID, zoneName, zoneIndex, "トークン消滅受信でエラー", card =>
        {
            _ = VanishReceivedToken(card, playExplosion);
        });
    }

    async Task VanishReceivedToken(CardController card, bool playExplosion)
    {
        try
        {
            if (playExplosion && card != null && CardAnimationManager.Instance != null)
                await CardAnimationManager.Instance.PlayDefeatExplosion(card);
            TokenExit.VanishLocal(card);
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }
}
