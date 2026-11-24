
using System.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
using TMPro;

public class DamageEffect : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI damageText;

    private Sequence currentSequence;

    private Task PlayEffect(Sequence sequence)
    {
        currentSequence?.Kill();

        currentSequence = sequence;

        var tcs = new TaskCompletionSource<bool>();

        currentSequence.OnComplete(() =>
        {
            currentSequence = null;
            Destroy(gameObject);
            tcs.TrySetResult(true);
        });

        currentSequence.OnKill(() =>
        {
            currentSequence = null;
            tcs.TrySetResult(true);
        });

        return tcs.Task;
    }

    public Task PlayDamageValue(Vector3 startPosition, Vector3 targetPosition, int damage, Color effectColor)
    {
        transform.position = startPosition;
        transform.localScale = Vector3.one;

        damageText.text = damage.ToString();
        damageText.color = effectColor;
        damageText.alpha = 1f;

        Sequence seq = DOTween.Sequence();

        seq.AppendInterval(0.5f);
        seq.Append(transform.DOMove(targetPosition, 0.45f));
        seq.Join(transform.DOScale(0.2f, 0.25f));
        seq.Join(damageText.DOFade(0.1f, 0.35f));

        return PlayEffect(seq);
    }

    public Task PlayLabel(Vector3 startPosition, Vector3 targetPosition, string label, Color effectColor)
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

        return PlayEffect(seq);
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
            rectTransform.sizeDelta = new Vector2(400f, 100f);

        Sequence seq = DOTween.Sequence();

        seq.Append(transform.DOScale(Vector3.one * 1.25f, 0.3f).SetEase(Ease.OutBack));
        seq.Append(transform.DOScale(Vector3.one * 0.75f, 0.2f).SetEase(Ease.InBack));
        seq.AppendInterval(1.25f);
        seq.Append(damageText.DOFade(0f, 0.25f));

        PlayEffect(seq);
    }
}
