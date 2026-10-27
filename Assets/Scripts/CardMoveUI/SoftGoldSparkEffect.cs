using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class SoftGoldSparkEffect : MonoBehaviour
{
    [Header("参照するカード")]
    [Tooltip("エフェクトを追従させる対象 (通常はカードの Artwork Image の Transform)")]
    public Transform targetTransform;

    private ParticleSystem goldSpark;

    private void Awake()
    {
        goldSpark = GetComponent<ParticleSystem>();

        // 初期設定（必要に応じてInspectorで上書き可）
        var main = goldSpark.main;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f, 0.6f));
        main.startSize = 0.1f;
        main.startLifetime = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = goldSpark.emission;
        emission.rateOverTime = 10f;

        var shape = goldSpark.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.8f;
    }

    private void LateUpdate()
    {
        // 毎フレーム、カード位置に追従
        if (targetTransform != null)
        {
            transform.position = targetTransform.position;
        }
    }

    /// <summary>
    /// エフェクトを再生します。
    /// </summary>
    public void PlayEffect()
    {
        if (goldSpark != null && !goldSpark.isPlaying)
        {
            goldSpark.Play();
        }
    }

    /// <summary>
    /// エフェクトを停止します。
    /// </summary>
    public void StopEffect()
    {
        if (goldSpark != null && goldSpark.isPlaying)
        {
            goldSpark.Stop();
        }
    }
}
