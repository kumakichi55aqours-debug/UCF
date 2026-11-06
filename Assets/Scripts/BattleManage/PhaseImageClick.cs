using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// フェイズ表示の画像クリックで、そのフェイズまで進める。
/// 進めるかどうかの判定は PhaseManager 側で行う。
/// </summary>
public class PhaseImageClick : MonoBehaviour, IPointerClickHandler
{
    public TurnPhase Phase { get; set; }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            return;

        PhaseManager.Instance?.RequestPhase(Phase);
    }
}
