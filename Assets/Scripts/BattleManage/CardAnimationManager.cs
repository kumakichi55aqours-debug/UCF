using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// カードアニメーション管理クラス
/// </summary>
public class CardAnimationManager : MonoBehaviour
{
    public static CardAnimationManager Instance { get; private set; }

    [Header("アニメーション設定")]
    [SerializeField] private float animationDuration = 0.4f;
    [SerializeField] private float drawAnimationDuration = 0.25f;
    [SerializeField] private Ease animationEase = Ease.OutQuad;

    [Header("効果音")]
    [SerializeField] private AudioClip drawSe;
    [SerializeField] private float drawSeVolume = 0.8f;
    [SerializeField] private AudioClip moveDecisionSe;
    [SerializeField] private float moveDecisionSeVolume = 0.8f;
    [SerializeField] private AudioClip hitSe;
    [SerializeField] private float hitSeVolume = 0.8f;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    // ヘルパーメソッド: カードの有効性チェック
    private bool IsCardValid(CardController card) => card != null && card.transform != null && card.gameObject != null;

    // ヘルパーメソッド: カード移動アニメーション（nullチェック付き）
    private async Task<bool> BaseCardAnimation(CardController card, Vector3 targetPos, float duration)
    {
        if (!IsCardValid(card)) return false;

        var tween = card.transform.DOMove(targetPos, duration)
            .SetEase(animationEase)
            .OnUpdate(() => {
                if (!IsCardValid(card))
                {
                    DOTween.Kill(card.transform);
                }
            });

        BattleManager.Instance.UpdateDeckUI();
        await tween.AsyncWaitForCompletion();
        return IsCardValid(card);
    }

    // ヘルパーメソッド: SetParent処理
    private bool SetCardParent(CardController card, Transform parent)
    {
        if (!IsCardValid(card) || parent == null) return false;
        card.transform.SetParent(parent, false);
        card.transform.localPosition = Vector3.zero;
        return true;
    }

    // カードドローアニメーション
    public async Task AnimateDraw(RectTransform fromZone, RectTransform toZone, CardController card)
    {
        if (!IsCardValid(card) || toZone == null) return;
        PlaySe(drawSe, drawSeVolume);
        card.transform.position = fromZone?.position ?? card.transform.position;
        await BaseCardAnimation(card, toZone.position, drawAnimationDuration);
    }

    void PlaySe(AudioClip clip, float volume)
    {
        if (clip == null || AudioManager.Instance == null)
            return;
        AudioManager.Instance.PlaySE(clip, volume);
    }

    // カード召喚アニメーション
    public async Task AnimateSummon(RectTransform fromZone, RectTransform toZone, CardController card)
    {
        if (!IsCardValid(card) || toZone == null) return;
        
        try
        {
            if (await BaseCardAnimation(card, toZone.position, animationDuration))
            {
                SetCardParent(card, toZone);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateSummon でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// バトラー召喚カットイン。Resources/SummonCutIn をキャンバス中央に出して、終わりまで待つ。
    /// プレハブが無いときは何もしない。
    /// </summary>
    public async Task PlayBattlerSummonCutIn(CardController card)
    {
        if (!IsCardValid(card) || card.cardKind != CardKind.バトラー)
            return;

        SummonCutIn instance = null;
        try
        {
            var prefab = Resources.Load<SummonCutIn>("SummonCutIn");
            if (prefab == null)
                return;

            Canvas canvas = BattleManager.Instance != null ? BattleManager.Instance.mainCanvas : null;
            if (canvas == null)
                canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
                return;

            instance = Instantiate(prefab, canvas.transform);
            instance.transform.SetAsLastSibling();

            Sprite artwork = (card.model as CardDBBase)?.artwork;
            await instance.Play(artwork, card.cardName);
            instance = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"PlayBattlerSummonCutIn でエラー: {ex.Message}");
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

            await Task.Delay(Mathf.CeilToInt(Mathf.Max(0.05f, duration) * 1000f));
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

    // カード破棄アニメーション
    public async Task AnimateDiscard(RectTransform fromZone, RectTransform toZone, CardController card, bool suppressSkillTrigger = false)
    {
        if (!IsCardValid(card) || toZone == null) return;
        
        try
        {
            // 送られる前の場所を取得（自分側／相手側を区別する）
            CardZoneType fromLocation = CardController.ZoneTypeFromTransform(fromZone);
            
            if (await BaseCardAnimation(card, toZone.position, animationDuration))
            {
                SetCardParent(card, toZone);
                // basePowerをリセット
                card.ResetBasePower();
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
    /// サーチカード強調表示
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
                try { seq.Kill(false); } catch {}
            }
            if (IsCardValid(card))
            {
                card.transform.localScale = originalScale;
            }
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
    /// スキル発動前のカード拡大アニメーション
    /// 125%拡大→100%に戻る（各0.2秒）
    /// playMoveSe が真のときは、技使用の拡大開始時に決定音を再生する。
    /// 墓地のカードは、この拡大のあいだだけ一番手前に出す。
    /// </summary>
    public async Task AnimateSkillActivation(CardController card, bool playMoveSe = false)
    {
        if (!IsCardValid(card)) return;
        if (playMoveSe)
            PlaySe(moveDecisionSe, moveDecisionSeVolume);

        Transform graveParent = null;
        int savedSiblingIndex = -1;

        try
        {
            var cardTransform = card.transform;
            if (IsGraveyardParent(cardTransform.parent))
            {
                graveParent = cardTransform.parent;
                savedSiblingIndex = cardTransform.GetSiblingIndex();
                cardTransform.SetAsLastSibling();
            }

            var originalScale = cardTransform.localScale;

            var expandTween = cardTransform.DOScale(originalScale * 1.25f, 0.25f)
                .SetEase(Ease.OutQuad)
                .OnUpdate(() => { if (!IsCardValid(card)) DOTween.Kill(cardTransform); });
            await expandTween.AsyncWaitForCompletion();

            if (!IsCardValid(card)) return;

            var shrinkTween = cardTransform.DOScale(originalScale, 0.25f)
                .SetEase(Ease.InQuad)
                .OnUpdate(() => { if (!IsCardValid(card)) DOTween.Kill(cardTransform); });
            await shrinkTween.AsyncWaitForCompletion();
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
            RestoreGraveyardOrder(card, graveParent, savedSiblingIndex);
        }
    }

    bool IsGraveyardParent(Transform parent)
    {
        var battle = BattleManager.Instance;
        if (battle == null || parent == null)
            return false;

        return parent == battle.pGraveZone || parent == battle.eGraveZone;
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

    // カードを手札に戻すアニメーション
    public async Task AnimateReturnToHand(CardController card, RectTransform fromZone, RectTransform toZone)
    {
        if (!IsCardValid(card) || toZone == null) return;
        
        try
        {
            if (fromZone != null)
            {
                card.transform.position = fromZone.position;
            }
            
            if (await BaseCardAnimation(card, toZone.position, animationDuration))
            {
                SetCardParent(card, toZone);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"AnimateReturnToHand でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 矢印が着いたあとヒット音を鳴らし、対象カードがあれば2回点滅させる。
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
    /// 攻撃者からターゲットへ向かう矢印UIを表示して消す
    /// Resources/TargetArrowEffect プレハブ（任意のUI要素）を Canvas 直下に生成します
    /// </summary>
    public async Task ShowTargetArrow(Transform from, Transform to, float duration = 0.4f)
    {
        try
        {
            if (from == null || to == null)
                return;

            // Canvas とプレハブ取得
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
                return;

            var arrowPrefab = Resources.Load<RectTransform>("TargetArrowEffect");
            if (arrowPrefab == null)
                return;

            // 生成と初期設定
            RectTransform arrow = Instantiate(arrowPrefab, canvas.transform);
            arrow.gameObject.SetActive(true);
            arrow.localScale = Vector3.one;
            arrow.SetAsLastSibling();
            
            var image = arrow.GetComponent<UnityEngine.UI.Image>();
            if (image != null)
            {
                image.enabled = true;
            }

            Vector3 fromPos = from.position;
            Vector3 toPos = to.position;
            arrow.position = fromPos;

            // 初期配置と向き
            Vector3 dir = (toPos - fromPos).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            arrow.rotation = Quaternion.Euler(0f, 0f, angle);

            // フェード用 CanvasGroup（無ければ追加）
            var cg = arrow.GetComponent<CanvasGroup>();
            if (cg == null) cg = arrow.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            // 攻撃者から少し前進してからターゲットへ移動→フェードアウト→破棄
            Vector3 midPoint = Vector3.Lerp(fromPos, toPos, 0.2f);
            Sequence seq = DOTween.Sequence();
            seq.Append(arrow.DOMove(midPoint, duration * 0.3f).SetEase(Ease.OutQuad));
            seq.Append(arrow.DOMove(toPos, duration * 0.7f).SetEase(Ease.InQuad));
            seq.Join(cg.DOFade(0f, 0.18f).SetDelay(duration * 0.7f));
            await seq.AsyncWaitForCompletion();

            Destroy(arrow.gameObject);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ShowTargetArrow でエラー: {ex.Message}");
        }
    }
} 