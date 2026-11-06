using System.Threading.Tasks;
using UnityEngine;

public static class TaskExtensions
{
    /// <summary>
    /// 完了を待たずに走らせる。例外は握りつぶさず、その場でログに出す。
    /// </summary>
    public static async void Forget(this Task task, string context)
    {
        try
        {
            await task;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{context}でエラー: {ex}");
        }
    }
}
