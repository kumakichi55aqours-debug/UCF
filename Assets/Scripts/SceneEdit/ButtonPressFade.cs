using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// オンラインマッチング中と同じく、ボタンを Disabled 色（薄い色）にする。
/// </summary>
public static class ButtonPressFade
{
    public static void Apply(Button button)
    {
        if (button != null)
            button.interactable = false;
    }

    public static IEnumerator Wait(Button button)
    {
        Apply(button);
        float duration = button != null ? button.colors.fadeDuration : 0.1f;
        if (duration > 0f)
            yield return new WaitForSecondsRealtime(duration);
        else
            yield return null;
    }
}
