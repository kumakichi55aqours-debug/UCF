using UnityEngine;

/// <summary>
/// 撃破時にカード上で再生する爆発Prefabの部品。
/// Animator か ParticleSystem の長さを返し、無ければ指定秒数を使う。
/// </summary>
public class DefeatExplosion : MonoBehaviour
{
    [SerializeField] private float duration = 0.8f;
    [SerializeField] private AudioClip バトラー撃破音;
    [SerializeField] private AudioClip フィールド撃破音;

    CardKind cardKind;

    public float Play(CardKind kind)
    {
        cardKind = kind;
        var animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.enabled = true;
            animator.Play(0, 0, 0f);
            animator.Update(0f);
        }

        foreach (var particle in GetComponentsInChildren<ParticleSystem>())
        {
            particle.Clear(true);
            particle.Play(true);
        }

        return ResolveDuration(animator);
    }

    /// <summary>
    /// 爆発クリップの Animation Event から呼ぶ。Animator と同じオブジェクトに付ける。
    /// </summary>
    public void PlayDefeatSe()
    {
        if (AudioManager.Instance == null)
            return;

        AudioClip clip = cardKind == CardKind.フィールド ? フィールド撃破音 : バトラー撃破音;
        AudioManager.Instance.PlaySE(clip);
    }

    float ResolveDuration(Animator animator)
    {
        float longest = 0f;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var clips = animator.runtimeAnimatorController.animationClips;
            if (clips != null)
            {
                foreach (var clip in clips)
                {
                    if (clip != null && clip.length > longest)
                        longest = clip.length;
                }
            }
        }

        foreach (var particle in GetComponentsInChildren<ParticleSystem>())
        {
            var main = particle.main;
            float particleDuration = main.duration + main.startLifetime.constantMax;
            if (particleDuration > longest)
                longest = particleDuration;
        }

        return longest > 0f ? longest : duration;
    }
}
