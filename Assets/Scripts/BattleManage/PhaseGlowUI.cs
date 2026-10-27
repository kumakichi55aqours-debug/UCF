using UnityEngine;
using UnityEngine.UI;

public class PhaseGlowUI : MonoBehaviour
{
    [Header("各フェーズ画像")]
    [SerializeField] private Image drawImage;
    [SerializeField] private Image mainImage;
    [SerializeField] private Image battleImage;
    [SerializeField] private Image endImage;

    [Header("自分ターンの光るマテリアル")]
    [SerializeField] private Material glowMaterial;

    [Header("相手ターンの光るマテリアル")]
    [SerializeField] private Material opponentGlowMaterial;

    [Header("通常のマテリアル")]
    [SerializeField] private Material defaultMaterial;

    public void HighlightPhase(TurnPhase phase)
    {
        ResetAllPhaseImages();

        Image activeImage = GetImageByPhase(phase);
        if (activeImage != null)
        {
            activeImage.material = ResolveGlowMaterial();
            activeImage.color = new Color(1f, 1f, 0.8f, 1f);
        }
    }

    Material ResolveGlowMaterial()
    {
        if (!IsOwnTurn() && opponentGlowMaterial != null)
            return opponentGlowMaterial;
        return glowMaterial;
    }

    static bool IsOwnTurn()
    {
        if (BattleManager.Instance == null || OnlineSyncManager.Instance == null)
            return true;
        return BattleManager.Instance.IsMyTurn;
    }

    private void ResetAllPhaseImages()
    {
        Image[] images = { drawImage, mainImage, battleImage, endImage };
        foreach (var img in images)
        {
            img.material = defaultMaterial;
            img.color = Color.white;
        }
    }

    private Image GetImageByPhase(TurnPhase phase)
    {
        switch (phase)
        {
            case TurnPhase.ドローフェイズ: return drawImage;
            case TurnPhase.メインフェイズ: return mainImage;
            case TurnPhase.バトルフェイズ: return battleImage;
            case TurnPhase.エンドフェイズ: return endImage;
            default: return null;
        }
    }
}
