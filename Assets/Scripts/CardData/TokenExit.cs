using UnityEngine;

/// <summary>
/// トークンは墓地・手札・デッキ・異次元へ着かず、その場で消える。移動のトリガーは出さない。
/// </summary>
public static class TokenExit
{
    public static bool IsToken(CardController card)
    {
        return card != null && card.トークン;
    }

    /// <summary>
    /// この端末のトークンを消す。相手への通知はしない。
    /// </summary>
    public static void VanishLocal(CardController card)
    {
        if (card == null)
            return;

        card.RemoveAllPassiveEffects();
        Object.Destroy(card.gameObject);
        PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    /// <summary>
    /// トークンを消し、オンラインなら相手の同じカードも消す。
    /// playExplosion が真のとき、相手側でも撃破の爆発を出してから消す。
    /// </summary>
    public static void VanishSynced(CardController card, bool playExplosion = false)
    {
        if (!IsToken(card))
            return;

        BattleManager battle = BattleManager.Instance;
        if (battle != null
            && battle.IsOnlineMode
            && OnlineSyncManager.Instance != null
            && CardAddress.TryFrom(card, out CardAddress address))
        {
            OnlineSyncManager.Instance.SyncTokenVanish(
                address.Kind.ToString(),
                address.Id,
                address.ZoneName,
                address.Index,
                playExplosion);
        }

        VanishLocal(card);
    }
}
