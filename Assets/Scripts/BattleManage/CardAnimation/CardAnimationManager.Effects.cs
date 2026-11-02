using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public partial class CardAnimationManager
{
    /// <summary>
    /// バトラー召喚カットイン。Resources/CutIn をキャンバス中央に出して、終わりまで待つ。
    /// プレハブが無いときは何もしない。
    /// </summary>
    public async Task PlayBattlerSummonCutIn(CardController card)
    {
        if (!IsCardValid(card) || card.cardKind != CardKind.バトラー)
            return;

        Sprite artwork = (card.model as CardDBBase)?.artwork;
        await PlayCutIn("CutIn", artwork, card.cardName);
    }

    /// <summary>
    /// 技のカットイン。Resources/CutIn をキャンバス中央に出して、終わりまで待つ。
    /// 表示は「{カード名}の\n{技名}！」。開始と同時にバトラーボイスを再生する。
    /// </summary>
    public async Task PlayMoveCutIn(CardController card, int moveIndex)
    {
        if (!IsCardValid(card) || card.cardKind != CardKind.バトラー)
            return;

        var battler = card.model as BattlerModel;
        if (battler == null)
            return;

        PlaySe(battler.summonVoice, 1f);

        string moveName = moveIndex == 0 ? battler.moveName1 : battler.moveName2;
        string text = $"{card.cardName}の\n{moveName}！";
        await PlayCutIn("CutIn", battler.artwork, text);
    }

    async Task PlayCutIn(string resourceName, Sprite artwork, string text)
    {
        SummonCutIn instance = null;
        try
        {
            var prefab = Resources.Load<SummonCutIn>(resourceName);
            if (prefab == null)
                return;

            Canvas canvas = BattleManager.Instance != null ? BattleManager.Instance.mainCanvas : null;
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
                return;

            instance = Instantiate(prefab, canvas.transform);
            instance.transform.SetAsLastSibling();
            await instance.Play(artwork, text);
            instance = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"PlayCutIn({resourceName}) でエラー: {ex.Message}");
            if (instance != null)
                Destroy(instance.gameObject);
        }
    }

    /// <summary>
    /// HPが0のバトラー・フィールドの上で撃破爆発を再生し、終わってから破棄する。
    /// Resources/DefeatExplosion をカードの子として出す。Prefabが無いときは何もしない。
    /// </summary>
    public async Task PlayDefeatExplosion(CardController card)
    {
        if (!IsCardValid(card) || card.curHp > 0)
            return;
        if (card.cardKind != CardKind.バトラー && card.cardKind != CardKind.フィールド)
            return;

        GameObject instance = null;
        try
        {
            var prefab = Resources.Load<GameObject>("DefeatExplosion");
            if (prefab == null)
                return;

            instance = Instantiate(prefab, card.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.transform.SetAsLastSibling();

            float duration = 0.8f;
            var effect = instance.GetComponentInChildren<DefeatExplosion>();
            if (effect != null)
                duration = effect.Play(card.cardKind);

            await Task.Delay(Mathf.CeilToInt(Mathf.Max(0.05f, duration) * 950f));
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"PlayDefeatExplosion でエラー: {ex.Message}");
        }
        finally
        {
            if (instance != null)
                Destroy(instance);
        }
    }

    /// <summary>
    /// サーチカード強調表示。
    /// 例: 1.1倍に拡大→1.0倍に戻るを繰り返す。
    /// </summary>
    public async Task AnimateSearchedCard(CardController card, float totalDurationSeconds = 1.2f, float scaleFactor = 1.1f, float onePulseDuration = 0.25f)
    {
        if (!IsCardValid(card)) return;

        Vector3 originalScale = card.transform.localScale;
        Sequence seq = null;
        try
        {
            seq = DOTween.Sequence();
            seq.Append(card.transform.DOScale(originalScale * scaleFactor, onePulseDuration).SetEase(Ease.OutQuad));
            seq.Append(card.transform.DOScale(originalScale, onePulseDuration).SetEase(Ease.InQuad));
            seq.SetLoops(-1, LoopType.Restart);
            seq.Play();

            await Task.Delay(Mathf.CeilToInt(totalDurationSeconds * 1000f));
        }
        finally
        {
            if (seq != null && seq.IsActive())
            {
                try { seq.Kill(false); } catch { }
            }
            if (IsCardValid(card))
                card.transform.localScale = originalScale;
        }
    }

    /// <summary>
    /// 選ばれたカードを同時に拡縮する。syncOnline のときは相手の画面にも出す。
    /// 技は両側で同じ処理が走るので、同期は効果の対象選択だけが使う。
    /// </summary>
    public async Task EmphasizeSelectedCards(IEnumerable<CardController> cards, bool syncOnline = false)
    {
        var list = new List<CardController>();
        if (cards != null)
        {
            foreach (var card in cards)
            {
                if (!IsCardValid(card) || list.Contains(card))
                    continue;
                list.Add(card);
            }
        }
        if (list.Count == 0)
            return;

        var sync = OnlineSyncManager.Instance;
        bool online = syncOnline && sync != null && BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode;
        if (online)
            sync.SyncSelectedTargetEmphasis(list);

        var tasks = new List<Task>(list.Count);
        foreach (var card in list)
            tasks.Add(AnimateSearchedCard(card, 0.6f, 1.1f, 0.15f));
        await Task.WhenAll(tasks);

        if (online)
            await sync.WaitForOpponentAnimation();
    }

    /// <summary>
    /// 矢印が着いたあとヒット音を鳴らし、対象カードがあれば点滅させる。
    /// </summary>
    public async Task PlayMoveImpact(Transform from, Transform to, CardController targetCard, int blinkCount = 2)
    {
        if (from != null && to != null)
            await ShowTargetArrow(from, to, 0.4f);

        PlaySe(hitSe, hitSeVolume);

        if (targetCard != null)
            await BlinkCard(targetCard, blinkCount);
    }

    /// <summary>
    /// カードの表示を指定回数だけ薄くして戻す。
    /// </summary>
    public async Task BlinkCard(CardController card, int times = 2)
    {
        if (!IsCardValid(card) || times <= 0)
            return;

        var group = card.GetComponent<CanvasGroup>();
        bool added = group == null;
        if (added)
            group = card.gameObject.AddComponent<CanvasGroup>();

        float original = group.alpha;
        try
        {
            for (int i = 0; i < times; i++)
            {
                if (!IsCardValid(card))
                    return;

                await group.DOFade(0.15f, 0.08f).AsyncWaitForCompletion();
                if (!IsCardValid(card))
                    return;

                await group.DOFade(original, 0.08f).AsyncWaitForCompletion();
            }
        }
        catch (MissingReferenceException)
        {
        }
        finally
        {
            if (group != null)
            {
                DOTween.Kill(group);
                group.alpha = original;
                if (added)
                    Destroy(group);
            }
        }
    }

    /// <summary>
    /// 攻撃者からターゲットへ向かう矢印UIを表示して消す。
    /// Resources/TargetArrowEffect を Canvas 直下に生成する。
    /// </summary>
    public async Task ShowTargetArrow(Transform from, Transform to, float duration = 0.4f)
    {
        try
        {
            if (from == null || to == null)
                return;

            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
                return;

            var arrowPrefab = Resources.Load<RectTransform>("TargetArrowEffect");
            if (arrowPrefab == null)
                return;

            RectTransform arrow = Instantiate(arrowPrefab, canvas.transform);
            arrow.gameObject.SetActive(true);
            arrow.localScale = Vector3.one;
            arrow.SetAsLastSibling();

            var image = arrow.GetComponent<Image>();
            if (image != null)
                image.enabled = true;

            Vector3 fromPos = from.position;
            Vector3 toPos = to.position;
            arrow.position = fromPos;

            Vector3 dir = (toPos - fromPos).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            var cg = arrow.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = arrow.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            Sequence seq = DOTween.Sequence();
            seq.Append(arrow.DORotate(new Vector3(0f, 0f, angle), 0.25f, RotateMode.Fast).SetEase(Ease.OutQuad));
            seq.AppendInterval(0.15f);
            seq.Append(arrow.DOMove(toPos, duration).SetEase(Ease.InQuad));
            seq.Join(cg.DOFade(0f, 0.18f).SetDelay(duration));
            await seq.AsyncWaitForCompletion();

            Destroy(arrow.gameObject);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ShowTargetArrow でエラー: {ex.Message}");
        }
    }
}
