using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip titleBGM;
    [SerializeField] private AudioClip deckEditBGM;
    [SerializeField] private AudioClip battleBGM;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // シーンが変わってもオブジェクトを保持

            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        // 念のため解除
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // シーンが読み込まれたときに自動で呼ばれる
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayBGM(scene.name);
    }


    public void PlayBGM(string sceneName)
    {
        AudioClip clip = null;

        switch (sceneName)
        {
            case "TitleScene":
                clip = titleBGM;
                break;
            case "DeckEditScene":
                clip = deckEditBGM;
                break;
            case "BattleScene":
                clip = battleBGM;
                break;
        }

        if (clip != null && audioSource.clip != clip)
        {
            audioSource.clip = clip;
            audioSource.loop = true; // BGMをループ再生に設定
            audioSource.Play();
        }
    }

    public void PlaySE(AudioClip clip, float volume = 0.8f)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// 召喚ボイスを再生する（専用メソッド）
    /// </summary>
    public void PlaySummonVoice(AudioClip summonVoiceClip, float volume = 0.8f)
    {
        if (summonVoiceClip == null)
        {
            Debug.LogWarning("召喚ボイスクリップがnullです");
            return;
        }
        
        if (audioSource == null)
        {
            Debug.LogError("AudioSourceが設定されていません");
            return;
        }
        
        // 召喚ボイスを再生
        audioSource.PlayOneShot(summonVoiceClip, volume);
        Debug.Log($"召喚ボイスを再生: {summonVoiceClip.name}");
    }
}
