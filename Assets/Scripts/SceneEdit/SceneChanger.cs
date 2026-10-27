using UnityEngine.SceneManagement;

public static class SceneChanger
{
    /// <summary>
    /// 指定シーンをロードします。
    /// </summary>
    public static void Load(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
