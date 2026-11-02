using UnityEngine;
using DG.Tweening;

/// <summary>
/// カードの移動、スキル発動、カットイン、着弾などの見た目をまとめる。
/// </summary>
public partial class CardAnimationManager : MonoBehaviour
{
    public static CardAnimationManager Instance { get; private set; }

    [Header("アニメーション設定")]
    [SerializeField] private float animationDuration = 0.4f;
    [SerializeField] private float drawAnimationDuration = 0.25f;
    [SerializeField] private Ease animationEase = Ease.OutQuad;

    [Header("スキル発動ハイライト")]
    [SerializeField] private string skillHighlightResource = "SkillActivationHighlight";

    [Header("効果音")]
    [SerializeField] private AudioClip drawSe;
    [SerializeField] private float drawSeVolume = 0.8f;
    [SerializeField] private AudioClip moveDecisionSe;
    [SerializeField] private float moveDecisionSeVolume = 0.8f;
    [SerializeField] private AudioClip skillActiveSe;
    [SerializeField] private float skillActiveSeVolume = 0.8f;
    [SerializeField] private AudioClip hitSe;
    [SerializeField] private float hitSeVolume = 0.8f;
    [SerializeField] private AudioClip grantSe;
    [SerializeField] private float grantSeVolume = 0.8f;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    bool IsCardValid(CardController card) => card != null && card.transform != null && card.gameObject != null;

    void PlaySe(AudioClip clip, float volume)
    {
        if (clip == null || AudioManager.Instance == null)
            return;
        AudioManager.Instance.PlaySE(clip, volume);
    }

    /// <summary>
    /// 付与スキルが効果を乗せたときの効果音。
    /// </summary>
    public void PlayGrantSe()
    {
        PlaySe(grantSe, grantSeVolume);
    }
}
