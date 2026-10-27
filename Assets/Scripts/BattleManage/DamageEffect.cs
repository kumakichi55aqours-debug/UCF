using UnityEngine;
using DG.Tweening;
using TMPro;

public class DamageEffect : MonoBehaviour
{
    // DamageEffect プレハブに含まれる TextMeshProUGUI（ダメージ数値表示用）
    [SerializeField] private TextMeshProUGUI damageText;

    /// <summary>
    /// ダメージエフェクトを再生する
    /// </summary>
    /// <param name="startPosition">開始位置（画面中央）</param>
    /// <param name="targetPosition">対象カードの位置</param>
    /// <param name="damage">ダメージ数値</param>
    /// <param name="effectColor">表示色（元パワーと同じなら黄、大きいなら緑、小さいなら赤）</param>
    public void PlayEffect(Vector3 startPosition, Vector3 targetPosition, int damage, Color effectColor)
    {
        // 初期設定
        transform.position = startPosition;
        transform.localScale = Vector3.one;
        damageText.text = damage.ToString();
        damageText.color = effectColor;
        damageText.alpha = 1f;  // 完全に不透明

        // Sequence でアニメーションを組み立てる
        Sequence seq = DOTween.Sequence();
        // 最初に1秒間表示（待機）
        seq.AppendInterval(0.5f);
        // 2秒間で対象カードの位置に移動、同時にスケールを小さくしてフェードアウト
        seq.Append(transform.DOMove(targetPosition, 0.75f));
        seq.Join(transform.DOScale(0.5f, 0.75f));
        seq.Join(damageText.DOFade(0f, 0.75f));
        // 完了後、エフェクトオブジェクトを削除
        seq.OnComplete(() => Destroy(gameObject));
    }

    /// <summary>
    /// ミスなど、数値以外のラベルをダメージと同じ軌道で出す。
    /// </summary>
    public void PlayLabel(Vector3 startPosition, Vector3 targetPosition, string label, Color effectColor)
    {
        transform.position = startPosition;
        transform.localScale = Vector3.one;
        damageText.text = label;
        damageText.color = effectColor;
        damageText.alpha = 1f;

        Sequence seq = DOTween.Sequence();
        seq.AppendInterval(0.5f);
        seq.Append(transform.DOMove(targetPosition, 0.5f));
        seq.Join(transform.DOScale(0.5f, 0.5f));
        seq.Join(damageText.DOFade(0f, 0.5f));
        seq.OnComplete(() => Destroy(gameObject));
    }

    /// <summary>
    /// ゲーム結果メッセージエフェクトを再生する（勝敗表示用）
    /// </summary>
    /// <param name="position">表示位置</param>
    /// <param name="message">表示メッセージ（"YOU WIN" または "YOU LOSE"）</param>
    /// <param name="messageColor">表示色</param>
    public void PlayGameResultEffect(Vector3 position, string message, Color messageColor)
    {
        // 初期設定
        transform.localPosition = position;
        transform.localScale = Vector3.one * 0.5f;
        
        // TextMeshProの設定を勝敗メッセージ用に調整
        damageText.text = message;
        damageText.color = messageColor;
        damageText.alpha = 1f;  // 完全に不透明
        damageText.enableWordWrapping = false; // 自動改行を無効化
        damageText.overflowMode = TextOverflowModes.Overflow; // オーバーフローを許可
        damageText.alignment = TextAlignmentOptions.Center; // 中央揃え
        
        // RectTransformのサイズを調整してテキストが収まるようにする
        var rectTransform = damageText.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.sizeDelta = new Vector2(400f, 100f); // 十分なサイズを確保
        }

        // Sequence でアニメーションを組み立てる
        Sequence seq = DOTween.Sequence();
        // パンチスケールエフェクト（少し大きくして戻る）
        seq.Append(transform.DOScale(Vector3.one * 1.25f, 0.3f).SetEase(Ease.OutBack));
        seq.Append(transform.DOScale(Vector3.one * 0.75f, 0.2f).SetEase(Ease.InBack));
        // 1.25秒間表示
        seq.AppendInterval(1.25f);
        // フェードアウト（0.25秒）
        seq.Append(damageText.DOFade(0f, 0.25f));
        // 完了後、エフェクトオブジェクトを削除
        seq.OnComplete(() => Destroy(gameObject));
    }
}
