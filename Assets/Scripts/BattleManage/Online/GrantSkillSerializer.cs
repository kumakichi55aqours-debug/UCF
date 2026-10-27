using System;
using UnityEngine;

/// <summary>
/// 付与スキル同期用のシリアライズ。SkillDB の具象型名 + JsonUtility で往復する。
/// </summary>
public static class GrantSkillSerializer
{
    public static string Serialize(SkillDB skill)
    {
        if (skill == null) return "{}";
        try
        {
            return JsonUtility.ToJson(skill);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GrantSkillSerializer] シリアライズに失敗: {ex.Message}");
            return "{}";
        }
    }

    public static SkillDB Deserialize(string className, string json)
    {
        try
        {
            var type = FindSkillType(className);
            if (type == null)
            {
                Debug.LogError($"[GrantSkillSerializer] 未知のスキル型: {className}");
                return null;
            }

            var skill = (SkillDB)Activator.CreateInstance(type);
            if (!string.IsNullOrEmpty(json) && json != "{}")
            {
                JsonUtility.FromJsonOverwrite(json, skill);
            }
            return skill;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GrantSkillSerializer] デシリアライズに失敗: {ex.Message}");
            return null;
        }
    }

    private static Type FindSkillType(string className)
    {
        if (string.IsNullOrEmpty(className)) return null;

        foreach (var type in typeof(SkillDB).Assembly.GetTypes())
        {
            if (!typeof(SkillDB).IsAssignableFrom(type) || type.IsAbstract) continue;
            if (type.Name == className) return type;

            var attr = (SkillDisplayNameAttribute)Attribute.GetCustomAttribute(type, typeof(SkillDisplayNameAttribute));
            if (attr != null && attr.Name == className) return type;
        }

        return null;
    }
}
