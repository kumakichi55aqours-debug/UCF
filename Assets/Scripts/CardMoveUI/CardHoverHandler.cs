using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CardHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private CardController cc;
    private BattleCardView view;

    private void Awake()
    {
        cc = GetComponent<CardController>();
        view = GetComponent<BattleCardView>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (view != null && view.IsBack && !cc.isPlayerCard)
            return;

        if (CardDetailManager.Instance != null && cc.model != null)
        {
            CardDetailManager.Instance.ShowDetail(cc);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (CardDetailManager.Instance != null)
        {
            CardDetailManager.Instance.HideDetail();
        }
    }
}
