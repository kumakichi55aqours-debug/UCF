using System.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトラー召喚時のカットイン。
/// Resources/SummonCutIn を編集すると、画像の大きさ・名前の位置・色・表示秒数を変えられる。
/// 外側のぼかしは、大きい方の画像（ArtworkGlow）の余白へ色をにじませる。余白は二つの画像の大きさの差。
/// </summary>
public class SummonCutIn : MonoBehaviour
{
    [SerializeField] private Image[] artworkImages;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("動き（プレハブの位置を基準に左右へずらす）")]
    [SerializeField] private float startX = 30f;
    [SerializeField] private float shownStartX = -50f;
    [SerializeField] private float shownEndX = 180f;
    [SerializeField] private float exitX = 270f;
    [SerializeField] private float enterDuration = 0.25f;
    [SerializeField] private float holdDuration = 1f;
    [SerializeField] private float exitDuration = 0.33f;

    RectTransform rect;
    Sequence sequence;
    Vector2 restPosition;
    Material bleedMaterial;

    void Awake()
    {
        rect = transform as RectTransform;
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        restPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
    }

    public async Task Play(Sprite artwork, string cardName)
    {
        try
        {
            if (rect == null)
                rect = transform as RectTransform;

            FitOuterBlur();

            if (artworkImages != null)
            {
                foreach (var image in artworkImages)
                {
                    if (image == null)
                        continue;
                    image.sprite = artwork;
                    image.enabled = artwork != null;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                }
            }

            if (nameText != null)
            {
                nameText.text = string.IsNullOrEmpty(cardName) ? "" : cardName;
                nameText.raycastTarget = false;
            }

            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            if (rect == null)
                return;

            rect.anchoredPosition = restPosition + new Vector2(startX, 0f);

            sequence = DOTween.Sequence();
            sequence.Append(rect.DOAnchorPos(restPosition + new Vector2(shownStartX, 0f), enterDuration).SetEase(Ease.OutCubic));
            if (canvasGroup != null)
                sequence.Join(canvasGroup.DOFade(1f, enterDuration * 0.85f));
            sequence.Append(rect.DOAnchorPos(restPosition + new Vector2(shownEndX, 0f), holdDuration).SetEase(Ease.Linear));
            sequence.Append(rect.DOAnchorPos(restPosition + new Vector2(exitX, 0f), exitDuration).SetEase(Ease.InCubic));
            if (canvasGroup != null)
                sequence.Join(canvasGroup.DOFade(0f, exitDuration));

            await sequence.AsyncWaitForCompletion();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"SummonCutIn.Play でエラー: {ex.Message}");
        }
        finally
        {
            if (this != null)
                Destroy(gameObject);
        }
    }

    // 大きい画像の中央に、小さい画像と同じ大きさで絵を置き、はみ出した分だけを外側のぼかしにする。
    void FitOuterBlur()
    {
        if (artworkImages == null)
            return;

        Image front = null;
        Image bleed = null;
        float frontArea = float.MaxValue;
        float bleedArea = -1f;
        foreach (var image in artworkImages)
        {
            if (image == null)
                continue;
            RectTransform imageRect = image.transform as RectTransform;
            if (imageRect == null)
                continue;
            float area = imageRect.rect.width * imageRect.rect.height;
            if (area <= frontArea)
            {
                frontArea = area;
                front = image;
            }
            if (area >= bleedArea)
            {
                bleedArea = area;
                bleed = image;
            }
        }

        if (front == null || bleed == null || front == bleed || bleed.material == null)
            return;

        RectTransform frontRect = front.transform as RectTransform;
        RectTransform bleedRect = bleed.transform as RectTransform;
        float scale = Mathf.Min(
            frontRect.rect.width / Mathf.Max(1f, bleedRect.rect.width),
            frontRect.rect.height / Mathf.Max(1f, bleedRect.rect.height));
        float margin = Mathf.Clamp((1f - scale) * 0.5f, 0f, 0.45f);
        if (margin <= 0.001f)
            return;

        if (bleedMaterial != null)
            Destroy(bleedMaterial);
        bleedMaterial = Instantiate(bleed.material);
        bleedMaterial.SetFloat("_Margin", margin);

        float reach = 1f / Mathf.Max(0.08f, 1f - margin * 2f);
        float fadeEnd = bleedMaterial.GetFloat("_EdgeEnd");
        float limit = reach * 0.96f;
        if (fadeEnd > limit)
            bleedMaterial.SetFloat("_EdgeEnd", limit);
        bleed.material = bleedMaterial;
    }

    void OnDestroy()
    {
        if (sequence != null && sequence.IsActive())
            sequence.Kill();
        if (bleedMaterial != null)
            Destroy(bleedMaterial);
    }
}
