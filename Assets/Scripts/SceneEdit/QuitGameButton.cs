using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class QuitGameButton : MonoBehaviour
{
	private Button btn;
	private bool isQuitting;

	void Awake()
	{
		btn = GetComponent<Button>();
		btn.onClick.RemoveAllListeners();
		btn.onClick.AddListener(OnClick);
	}

	private void OnClick()
	{
		if (isQuitting) return;
		isQuitting = true;
		StartCoroutine(QuitAfterFade());
	}

	private IEnumerator QuitAfterFade()
	{
		yield return ButtonPressFade.Wait(btn);
		QuitGame();
	}

	public static void QuitGame()
	{
		#if UNITY_EDITOR
		// エディタでは再生を停止
		UnityEditor.EditorApplication.isPlaying = false;
		#else
		// ビルド後はアプリケーションを終了
		Application.Quit();
		#endif
	}
}


