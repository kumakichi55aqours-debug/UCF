using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 墓地ゾーンと異次元ゾーンのクリックで、確認一覧を開く。
/// カードが重なっているときは、そのカード側が先に受け取る。
/// </summary>
public class ZoneClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            return;

        Open(transform);
    }

    public static void Open(Transform zone)
    {
        if (zone == null || ZoneViewUI.Instance == null)
            return;
        if (TargetSelectionManager.Instance != null && TargetSelectionManager.Instance.IsSkillTargetSelectionMode)
            return;
        if (SkillChainManager.Instance != null && SkillChainManager.Instance.IsResolvingSkills)
            return;

        var battle = BattleManager.Instance;
        if (battle == null)
            return;
        if (zone != battle.pGraveZone && zone != battle.eGraveZone
            && zone != battle.pDimensionZone && zone != battle.eDimensionZone)
            return;

        ZoneViewUI.Instance.Show(zone);
    }
}
