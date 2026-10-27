using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// ダメージ、回復、スキル対象ハイライトの同期。
/// </summary>
public partial class OnlineSyncManager
{
    /// <summary>
    /// ダメージエフェクトを同期（スキル効果用）
    /// </summary>
    public void SyncDamageEffect(string cardKindStr, int cardID, string zoneName, int zoneIndex, int damageAmount)
    {
        if (!IsOnlineMode)
            return;

        SendAnimatedRpc("ReceiveDamageEffect", cardKindStr, cardID, zoneName, zoneIndex, damageAmount);
    }

    /// <summary>
    /// ダメージ値を同期（スキル効果用）
    /// </summary>
    public void SyncDamageValue(string cardKindStr, int cardID, string zoneName, int zoneIndex, int damageAmount)
    {
        if (!IsOnlineMode)
            return;

        SendRPC("ReceiveDamageValue", cardKindStr, cardID, zoneName, zoneIndex, damageAmount);
    }

    /// <summary>
    /// 複数対象のダメージを、動かす前の住所でまとめて同期する
    /// </summary>
    public void SyncAreaDamage(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int[] amounts)
    {
        if (amounts == null || !HasCardAddresses(cardKinds, cardIds, zoneNames, indices) || zoneNames.Length != amounts.Length)
            return;

        SendAnimatedRpc("ReceiveAreaDamage", cardKinds, cardIds, zoneNames, indices, amounts);
    }

    /// <summary>
    /// プレイヤーHPへの直接ダメージを同期する。数値は送信者から見た自分と相手。
    /// </summary>
    public void SyncDirectHpDamage(int damageToSender, int damageToReceiver)
    {
        if (!IsOnlineMode || (damageToSender <= 0 && damageToReceiver <= 0))
            return;

        SendAnimatedRpc("ReceiveDirectHpDamage", damageToSender, damageToReceiver);
    }

    /// <summary>
    /// 複数対象の回復を、ゾーン番号と回復後HPで同期する
    /// </summary>
    public void SyncAreaHeal(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int[] newHps, int[] shownAmounts)
    {
        if (newHps == null || shownAmounts == null || !HasCardAddresses(cardKinds, cardIds, zoneNames, indices))
            return;
        if (zoneNames.Length != newHps.Length || zoneNames.Length != shownAmounts.Length)
            return;

        SendAnimatedRpc("ReceiveAreaHeal", cardKinds, cardIds, zoneNames, indices, newHps, shownAmounts);
    }

    /// <summary>
    /// スキル対象選択中のハイライトを、相手側の同じカードへ出す
    /// </summary>
    public void SyncSkillTargetHighlights(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        if (!IsOnlineMode)
            return;

        SendRPC(
            "ReceiveSkillTargetHighlights",
            cardKinds ?? System.Array.Empty<string>(),
            cardIds ?? System.Array.Empty<int>(),
            zoneNames ?? System.Array.Empty<string>(),
            indices ?? System.Array.Empty<int>());
    }

    public void SyncClearSkillTargetHighlights()
    {
        if (!IsOnlineMode)
            return;

        SendRPC("ReceiveClearSkillTargetHighlights");
    }

    [PunRPC]
    private void ReceiveDamageValue(string cardKindStr, int cardID, string zoneName, int zoneIndex, int damageAmount)
    {
        try
        {
            CardController targetCard = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
            if (targetCard == null)
                return;

            targetCard.curHp -= damageAmount;
            targetCard.UpdateDisplay();
            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ダメージ値受信でエラー: {ex.Message}");
        }
    }

    [PunRPC]
    private void ReceiveDamageEffect(string cardKindStr, int cardID, string zoneName, int zoneIndex, int damageAmount)
    {
        ReceiveAnimatedCardAction(
            cardKindStr,
            cardID,
            zoneName,
            zoneIndex,
            "ダメージエフェクト受信でエラー",
            targetCard =>
            {
                _ = ShowDamageEffectOnOpponent(targetCard, damageAmount);
            });
    }

    [PunRPC]
    private void ReceiveDirectHpDamage(int damageToSender, int damageToReceiver)
    {
        _ = ApplyDirectHpDamage(damageToSender, damageToReceiver);
    }

    [PunRPC]
    private void ReceiveAreaDamage(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int[] amounts)
    {
        try
        {
            var cards = new List<CardController>();
            var values = new List<int>();
            CollectResolvedCards(cardKinds, cardIds, zoneNames, indices, amounts, cards, values);
            _ = ApplyAreaDamage(cards, values);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveAreaDamageでエラー: {ex.Message}");
            SendOpponentAnimationComplete();
        }
    }

    [PunRPC]
    private void ReceiveAreaHeal(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int[] newHps, int[] shownAmounts)
    {
        try
        {
            var cards = new List<CardController>();
            var hpValues = new List<int>();
            var amounts = new List<int>();
            int count = CardAddress.Count(cardKinds, cardIds, zoneNames, indices);
            if (newHps != null)
                count = Mathf.Min(count, newHps.Length);
            if (shownAmounts != null)
                count = Mathf.Min(count, shownAmounts.Length);

            for (int i = 0; i < count; i++)
            {
                CardController card = CardAddress.ResolveAt(cardKinds, cardIds, zoneNames, indices, i);
                if (card == null)
                    continue;

                cards.Add(card);
                hpValues.Add(newHps[i]);
                amounts.Add(shownAmounts[i]);
            }

            _ = ApplyAreaHeal(cards, hpValues, amounts);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ReceiveAreaHealでエラー: {ex.Message}");
            SendOpponentAnimationComplete();
        }
    }

    [PunRPC]
    private void ReceiveSkillTargetHighlights(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        if (BattleManager.Instance == null)
            return;

        var cards = new List<CardController>();
        int count = CardAddress.Count(cardKinds, cardIds, zoneNames, indices);
        for (int i = 0; i < count; i++)
        {
            CardController card = CardAddress.ResolveAt(cardKinds, cardIds, zoneNames, indices, i);
            if (card != null)
                cards.Add(card);
        }

        BattleManager.Instance.ShowResolvedTargetHighlights(cards);
    }

    [PunRPC]
    private void ReceiveClearSkillTargetHighlights()
    {
        BattleManager.Instance?.ClearResolvedTargetHighlights();
    }

    private async Task ApplyDirectHpDamage(int damageToSender, int damageToReceiver)
    {
        try
        {
            var battle = BattleManager.Instance;
            if (battle == null)
                return;

            if (damageToSender > 0)
            {
                battle.eResources?.Hp?.TakeDamage(damageToSender);
                if (battle.eFieldZone != null)
                    await battle.ShowEffect(battle.eFieldZone.position, damageToSender, Color.yellow);
                await Task.Delay(800);
            }

            if (damageToReceiver > 0)
            {
                battle.pResources?.Hp?.TakeDamage(damageToReceiver);
                if (battle.pFieldZone != null)
                    await battle.ShowEffect(battle.pFieldZone.position, damageToReceiver, Color.yellow);
                await Task.Delay(800);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ApplyDirectHpDamageでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    private async Task ApplyAreaDamage(List<CardController> cards, List<int> amounts)
    {
        try
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                int damage = amounts[i];
                card.curHp -= damage;
                card.UpdateDisplay();
                if (damage > 0)
                    BattleManager.Instance?.RequestStunResolve(card);
                await ShowDamageEffectOnOpponent(card, damage, false);
            }

            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ApplyAreaDamageでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    private async Task ApplyAreaHeal(List<CardController> cards, List<int> newHps, List<int> shownAmounts)
    {
        try
        {
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                int maxHp = GetMaxHp(card);
                int healedHp = Mathf.Max(0, newHps[i]);
                card.curHp = maxHp > 0 ? Mathf.Min(healedHp, maxHp) : healedHp;
                card.UpdateDisplay();
                if (shownAmounts[i] > 0)
                    await ShowDamageEffectOnOpponent(card, shownAmounts[i], false, Color.green);
            }

            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ApplyAreaHealでエラー: {ex.Message}");
        }
        finally
        {
            SendOpponentAnimationComplete();
        }
    }

    /// <summary>
    /// 相手側でダメージエフェクトを表示
    /// </summary>
    private async Task ShowDamageEffectOnOpponent(CardController targetCard, int damageAmount, bool notifyComplete = true, Color? effectColor = null)
    {
        try
        {
            DamageEffect dmgEffectPrefab = Resources.Load<DamageEffect>("DamageEffect");
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (dmgEffectPrefab == null || canvas == null)
                return;

            DamageEffect damageEffect = Object.Instantiate(dmgEffectPrefab, canvas.transform);
            RectTransform canvasRect = canvas.transform as RectTransform;
            Vector2 screenCenter = new Vector2(Screen.width / 2 + 255, Screen.height / 2);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenCenter, null, out Vector2 localPoint);

            RectTransform effectRect = damageEffect.GetComponent<RectTransform>();
            if (effectRect != null)
                effectRect.anchoredPosition = localPoint;
            else
                damageEffect.transform.position = Camera.main.ScreenToWorldPoint(new Vector3(Screen.width / 2, Screen.height / 2, 10f));

            damageEffect.PlayEffect(damageEffect.transform.position, targetCard.transform.position, damageAmount, effectColor ?? Color.yellow);
            await Task.Delay(300);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] ShowDamageEffectOnOpponentでエラー: {ex.Message}");
        }
        finally
        {
            if (notifyComplete)
                SendOpponentAnimationComplete();
        }
    }

    static int GetMaxHp(CardController card)
    {
        if (card != null && card.model is BattlerModel battler && battler.maxHp > 0)
            return battler.maxHp;
        if (card != null && card.model is FieldModel field && field.maxHp > 0)
            return field.maxHp;
        return 0;
    }

    // 墓地同期からも使う。カード住所の解決と、アニメ完了待ち付きのRPC。
    private void SendAnimatedRpc(string rpcName, params object[] args)
    {
        if (!IsOnlineMode)
            return;

        RegisterOpponentAnimationWait();
        SendRPC(rpcName, args);
    }

    private bool HasCardAddresses(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices)
    {
        return IsOnlineMode && CardAddress.Count(cardKinds, cardIds, zoneNames, indices) > 0
            && cardKinds.Length == cardIds.Length
            && cardKinds.Length == zoneNames.Length
            && cardKinds.Length == indices.Length;
    }

    private void ReceiveAnimatedCardAction(
        string cardKindStr,
        int cardID,
        string zoneName,
        int zoneIndex,
        string errorLabel,
        System.Action<CardController> onFound)
    {
        try
        {
            CardController targetCard = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
            if (targetCard == null)
            {
                SendOpponentAnimationComplete();
                return;
            }

            onFound(targetCard);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineSyncManager] {errorLabel}: {ex.Message}");
            SendOpponentAnimationComplete();
        }
    }

    private void CollectResolvedCards(string[] cardKinds, int[] cardIds, string[] zoneNames, int[] indices, int[] amounts, List<CardController> cards, List<int> values)
    {
        int count = CardAddress.Count(cardKinds, cardIds, zoneNames, indices);
        if (amounts != null)
            count = Mathf.Min(count, amounts.Length);

        for (int i = 0; i < count; i++)
        {
            CardController card = CardAddress.ResolveAt(cardKinds, cardIds, zoneNames, indices, i);
            if (card == null)
                continue;

            cards.Add(card);
            values?.Add(amounts[i]);
        }
    }
}
