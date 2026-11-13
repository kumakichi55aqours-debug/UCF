using UnityEngine;
using DG.Tweening;
using TMPro;

public class DamageEffect : MonoBehaviour
{
    // DamageEffect プレハブに含まれる TextMeshProUGUI（ダメージ数値表示用）
    [SerializeField] private TextMeshProUGUI damageText;

    private Sequence currentSequence;

    private void PlayEffect(Sequence sequence)
    {
        // 前回のアニメーションが残っていた場合は停止
        currentSequence?.Kill();

        currentSequence = sequence;

        // アニメーション終了時にオブジェクトを削除
        currentSequence.OnComplete(() =>
        {
            currentSequence = null;
            Destroy(gameObject);
        });
    }

    public void PlayDamageValue(Vector3 startPosition, Vector3 targetPosition, int damage, Color effectColor)
    {
        // 初期設定
        transform.position = startPosition;
        transform.localScale = Vector3.one;

        damageText.text = damage.ToString();
        damageText.color = effectColor;
        damageText.alpha = 1f;

        Sequence seq = DOTween.Sequence();

        seq.AppendInterval(0.3f);
        seq.Append(transform.DOMove(targetPosition, 0.4f));
        seq.Join(transform.DOScale(0.5f, 0.5f));
        seq.Join(damageText.DOFade(0f, 0.5f));

        PlayEffect(seq);
    }

    public void PlayLabel(Vector3 startPosition, Vector3 targetPosition, string label, Color effectColor)
    {
        transform.position = startPosition;
        transform.localScale = Vector3.one;

        damageText.text = label;
        damageText.color = effectColor;
        damageText.alpha = 1f;

        Sequence seq = DOTween.Sequence();

        seq.AppendInterval(0.3f);
        seq.Append(transform.DOMove(targetPosition, 0.4f));
        seq.Join(transform.DOScale(0.5f, 0.5f));
        seq.Join(damageText.DOFade(0f, 0.5f));

        PlayEffect(seq);
    }

    public void PlayGameResultEffect(Vector3 position, string message, Color messageColor)
    {
        transform.localPosition = position;
        transform.localScale = Vector3.one * 0.5f;

        damageText.text = message;
        damageText.color = messageColor;
        damageText.alpha = 1f;
        damageText.enableWordWrapping = false;
        damageText.overflowMode = TextOverflowModes.Overflow;
        damageText.alignment = TextAlignmentOptions.Center;

        var rectTransform = damageText.GetComponent<RectTransform>();

        if (rectTransform != null)
        {
            rectTransform.sizeDelta = new Vector2(400f, 100f);
        }

        Sequence seq = DOTween.Sequence();

        seq.Append(transform.DOScale(Vector3.one * 1.25f, 0.3f).SetEase(Ease.OutBack));
        seq.Append(transform.DOScale(Vector3.one * 0.75f, 0.2f).SetEase(Ease.InBack));
        seq.AppendInterval(1.25f);
        seq.Append(damageText.DOFade(0f, 0.25f));

        PlayEffect(seq);
    }
}
