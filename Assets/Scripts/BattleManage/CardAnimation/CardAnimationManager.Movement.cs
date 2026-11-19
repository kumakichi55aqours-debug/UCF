using System.Threading.Tasks;
using UnityEngine;
using DG.Tweening;

public partial class CardAnimationManager
{
    public async Task AnimateDraw(RectTransform fromZone, RectTransform toZone, CardController card)
    {
        if (!IsCardValid(card) || toZone == null) return;
        PlaySe(drawSe, drawSeVolume);
        card.transform.position = fromZone?.position ?? card.transform.position;
        await BaseCardAnimation(card, toZone.position, drawAnimationDuration);
    }

    public async Task AnimateSummon(RectTransform fromZone, RectTransform toZone, CardController card)
    {
        if (!IsCardValid(card) || toZone == null) return;

        try
        {
            if (await BaseCardAnimation(card, toZone.position, animationDuration))
                SetCardParent(card, toZone);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateSummon でエラー: {ex.Message}");
        }
    }

    public async Task AnimateDiscard(RectTransform fromZone, RectTransform toZone, CardController card, bool suppressSkillTrigger = false)
    {
        if (!IsCardValid(card) || toZone == null) return;

        if (card.トークン)
        {
            TokenExit.VanishLocal(card);
            return;
        }

        try
        {
            // 送られる前の場所を取得（自分側／相手側を区別する）
            CardZoneType fromLocation = CardController.ZoneTypeFromTransform(fromZone);

            if (await BaseCardAnimation(card, toZone.position, animationDuration))
            {
                SetCardParent(card, toZone);
                card.ResetBasePower();
                card.ResetMoveChanges();
                card.ResetMoveElements();

                await Task.Delay(200);

                EventBus.Publish(new CardSentToGraveyardEvent(card, fromLocation, suppressSkillTrigger));
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateDiscard でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// ゾーンへ移動する。墓地へ送られた時のトリガーは出さない。
    /// </summary>
    public async Task AnimateRelocate(RectTransform fromZone, RectTransform toZone, CardController card)
    {
        if (!IsCardValid(card) || toZone == null) return;
        if (card.トークン)
        {
            TokenExit.VanishLocal(card);
            return;
        }

        try
        {
            if (fromZone != null)
                card.transform.position = fromZone.position;
            if (await BaseCardAnimation(card, toZone.position, animationDuration))
            {
                SetCardParent(card, toZone);
                card.ResetBasePower();
                card.ResetMoveChanges();
                card.ResetMoveElements();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateRelocate でエラー: {ex.Message}");
        }
    }

    public async Task AnimateReturnToHand(CardController card, RectTransform fromZone, RectTransform toZone)
    {
        if (!IsCardValid(card) || toZone == null) return;

        if (card.トークン)
        {
            TokenExit.VanishLocal(card);
            return;
        }

        try
        {
            if (fromZone != null)
                card.transform.position = fromZone.position;

            if (await BaseCardAnimation(card, toZone.position, animationDuration))
                SetCardParent(card, toZone);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateReturnToHand でエラー: {ex.Message}");
        }
    }

    async Task<bool> BaseCardAnimation(CardController card, Vector3 targetPos, float duration)
    {
        if (!IsCardValid(card)) return false;

        var tween = card.transform.DOMove(targetPos, duration)
            .SetEase(animationEase)
            .OnUpdate(() =>
            {
                if (!IsCardValid(card))
                    DOTween.Kill(card.transform);
            });

        BattleManager.Instance.UpdateDeckUI();
        await tween.AsyncWaitForCompletion();
        return IsCardValid(card);
    }

    bool SetCardParent(CardController card, Transform parent)
    {
        if (!IsCardValid(card) || parent == null) return false;
        card.transform.SetParent(parent, false);
        card.transform.localPosition = Vector3.zero;
        return true;
    }
}
