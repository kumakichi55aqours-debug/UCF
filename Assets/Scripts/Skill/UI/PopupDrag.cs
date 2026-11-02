using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 表示中のパネルを、ボタン以外の場所をドラッグして動かす。
/// 表示のたびに、最初に置いてあった位置へ戻す。
/// </summary>
public class PopupDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    RectTransform rect;
    Canvas canvas;
    Vector2 home;
    bool captured;
    bool dragging;

    public static void Use(GameObject panel)
    {
        if (panel == null)
            return;

        var drag = panel.GetComponent<PopupDrag>();
        if (drag == null)
            drag = panel.AddComponent<PopupDrag>();
        drag.ResetToHome();
    }

    /// <summary>
    /// コンポーネントだけ付ける。表示されたときに元の位置へ戻る。
    /// </summary>
    public static void Attach(GameObject panel)
    {
        if (panel == null || panel.GetComponent<PopupDrag>() != null)
            return;
        panel.AddComponent<PopupDrag>();
    }

    void Awake()
    {
        rect = transform as RectTransform;
        canvas = GetComponentInParent<Canvas>();
        Capture();
    }

    void OnEnable()
    {
        ResetToHome();
    }

    void Capture()
    {
        if (captured || rect == null)
            return;
        home = rect.anchoredPosition;
        captured = true;
    }

    public void ResetToHome()
    {
        if (rect == null)
            rect = transform as RectTransform;
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();
        Capture();
        dragging = false;
        if (rect != null)
            rect.anchoredPosition = home;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = eventData != null && !PressedControl(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || rect == null || eventData == null)
            return;

        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        rect.anchoredPosition += eventData.delta / scale;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
    }

    static bool PressedControl(PointerEventData eventData)
    {
        var press = eventData.pointerPress != null
            ? eventData.pointerPress
            : eventData.pointerCurrentRaycast.gameObject;
        return press != null && press.GetComponentInParent<Selectable>() != null;
    }
}
