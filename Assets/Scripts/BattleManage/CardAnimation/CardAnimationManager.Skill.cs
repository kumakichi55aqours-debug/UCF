using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public partial class CardAnimationManager
{
    /// <summary>
    /// スキル発動前のカード拡大アニメーション。
    /// 125%拡大→100%に戻る（各0.25秒）。
    /// 同時に、カードの子の先頭へハイライトを出して縁から見えるようにする。
    /// playMoveSe が真のときは、技使用の拡大開始時に決定音を再生する。
    /// 墓地のカードは、この拡大のあいだだけ一番手前に出す。
    /// </summary>
    public async Task AnimateSkillActivation(CardController card, bool playMoveSe = false)
    {
        if (!IsCardValid(card)) return;
        if (playMoveSe)
            PlaySe(moveDecisionSe, moveDecisionSeVolume);
        else
            PlaySe(skillActiveSe, skillActiveSeVolume);

        // 相手手札は裏面。発動演出の前に表へする。呼び出し側が先に表へしているときは何もしない。
        bool revealedHere = RevealEnemyHandFace(card);
        Transform graveParent = null;
        int savedSiblingIndex = -1;
        RectTransform highlight = null;

        try
        {
            var cardTransform = card.transform;
            if (IsGraveyardParent(cardTransform.parent))
            {
                graveParent = cardTransform.parent;
                savedSiblingIndex = cardTransform.GetSiblingIndex();
                cardTransform.SetAsLastSibling();
            }

            highlight = AttachSkillHighlight(cardTransform);
            var originalScale = cardTransform.localScale;
            var seq = DOTween.Sequence();
            seq.Append(cardTransform.DOScale(originalScale * 1.25f, 0.25f).SetEase(Ease.OutQuad));
            seq.Append(cardTransform.DOScale(originalScale, 0.25f).SetEase(Ease.InQuad));
            seq.OnUpdate(() =>
            {
                if (!IsCardValid(card))
                    seq.Kill();
            });
            PlaySkillHighlight(highlight, seq);

            await seq.AsyncWaitForCompletion();
        }
        catch (MissingReferenceException)
        {
            Debug.LogWarning("AnimateSkillActivation: アニメーション中にカードが破棄されたため中断しました");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateSkillActivation でエラー: {ex.Message}");
        }
        finally
        {
            ClearSkillHighlight(highlight);
            RestoreGraveyardOrder(card, graveParent, savedSiblingIndex);
            // この演出の中だけで表にしたカードは、裏へ戻す。
            // 効果の選択まで表のまま見せる場合は、呼び出し側が先に表にしておく。
            RestoreEnemyHandBack(card, revealedHere);
        }
    }

    /// <summary>
    /// 相手手札の裏カードを表にする。表にしたとき真。
    /// 発動アニメのあと、効果の選択が終わるまで表のままにするときに使う。
    /// </summary>
    public static bool RevealEnemyHandFace(CardController card)
    {
        if (!IsEnemyHandCard(card))
            return false;

        var view = card.GetComponent<BattleCardView>();
        if (view == null || !view.IsBack)
            return false;

        card.UpdateDisplay();
        return true;
    }

    /// <summary>
    /// RevealEnemyHandFace で表にしたカードが、まだ相手手札にあるとき裏へ戻す。
    /// </summary>
    public static void RestoreEnemyHandBack(CardController card, bool revealed)
    {
        if (!revealed || !IsEnemyHandCard(card))
            return;

        card.GetComponent<BattleCardView>()?.ShowCardBack();
    }

    static bool IsEnemyHandCard(CardController card)
    {
        var battle = BattleManager.Instance;
        return card != null
            && card.transform != null
            && battle != null
            && card.transform.parent == battle.eHandZone;
    }

    /// <summary>
    /// Resources のハイライトをカードの一番後ろに付ける。
    /// プレハブが無いときは何も出さない。
    /// </summary>
    RectTransform AttachSkillHighlight(Transform cardTransform)
    {
        if (string.IsNullOrEmpty(skillHighlightResource))
            return null;

        var prefab = Resources.Load<RectTransform>(skillHighlightResource);
        if (prefab == null)
        {
            Debug.LogWarning($"スキル発動ハイライトが見つかりません: Resources/{skillHighlightResource}");
            return null;
        }

        var highlight = Instantiate(prefab, cardTransform, false);
        highlight.SetAsFirstSibling();
        highlight.anchoredPosition = Vector2.zero;
        highlight.localRotation = Quaternion.identity;
        highlight.localScale = new Vector3(0.85f, 0.85f, 1f);

        var group = highlight.GetComponent<CanvasGroup>();
        if (group == null)
            group = highlight.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        foreach (var graphic in highlight.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        return highlight;
    }

    /// <summary>
    /// 拡大に合わせてフェードインし、戻るあいだに外へ広がって消える。
    /// </summary>
    static void PlaySkillHighlight(RectTransform highlight, Sequence seq)
    {
        if (highlight == null)
            return;

        var group = highlight.GetComponent<CanvasGroup>();
        seq.Insert(0f, highlight.DOScale(new Vector3(1.15f, 1.15f, 1f), 0.25f).SetEase(Ease.OutQuad));
        if (group != null)
            seq.Insert(0f, group.DOFade(1f, 0.18f));
        seq.Insert(0.25f, highlight.DOScale(new Vector3(1.28f, 1.28f, 1f), 0.25f).SetEase(Ease.OutQuad));
        if (group != null)
            seq.Insert(0.25f, group.DOFade(0f, 0.25f));
    }

    static void ClearSkillHighlight(RectTransform highlight)
    {
        if (highlight == null)
            return;

        highlight.DOKill();
        var group = highlight.GetComponent<CanvasGroup>();
        if (group != null)
            group.DOKill();
        Destroy(highlight.gameObject);
    }

    bool IsGraveyardParent(Transform parent)
    {
        var battle = BattleManager.Instance;
        if (battle == null || parent == null)
            return false;

        return parent == battle.pGraveZone || parent == battle.eGraveZone
            || parent == battle.pDimensionZone || parent == battle.eDimensionZone;
    }

    void RestoreGraveyardOrder(CardController card, Transform graveParent, int savedSiblingIndex)
    {
        if (graveParent == null || savedSiblingIndex < 0 || !IsCardValid(card))
            return;
        if (card.transform.parent != graveParent)
            return;

        int last = graveParent.childCount - 1;
        if (last < 0)
            return;

        card.transform.SetSiblingIndex(Mathf.Min(savedSiblingIndex, last));
    }
}
