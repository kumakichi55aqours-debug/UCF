using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ISkillCondition), true)]
public class SkillConditionDrawer : PropertyDrawer
{
    static readonly List<Type> _types = new List<Type>
    {
        typeof(HandCountCondition),
        typeof(HpCondition),
        typeof(CardExistsCondition),
        typeof(ZoneEmptyCondition),
        typeof(DamageCondition),
        typeof(AttackerElementCondition),
        typeof(MoveElementCondition),
        typeof(PlayerResourceCondition),
        typeof(StatusAilmentCondition),
        typeof(PhaseCondition),
        typeof(NotSentToGraveThisTurnCondition),
    };
    static readonly string[] _typeNames = _types.Select(t => t.Name).ToArray();

    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
    {
        EditorGUI.BeginProperty(pos, label, prop);
        Layout(prop, pos.x, pos.y, pos.width, true);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
    {
        return Layout(prop, 0f, 0f, 0f, false);
    }

    static float Layout(SerializedProperty prop, float x, float y, float width, bool draw)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        if (draw)
        {
            Type current = prop.managedReferenceValue?.GetType();
            int idx = _types.IndexOf(current ?? _types[0]);
            int newIdx = EditorGUI.Popup(new Rect(x, y, width, line), "条件タイプ", idx, _typeNames);
            if (newIdx != idx)
            {
                prop.managedReferenceValue = Activator.CreateInstance(_types[newIdx]);
                prop.serializedObject.ApplyModifiedProperties();
            }
        }

        y += line + spacing;
        string path = prop.propertyPath;
        SerializedProperty threshold = prop.FindPropertyRelative("閾値タイプ");
        SerializedProperty it = prop.Copy();
        bool enter = true;
        while (it.NextVisible(enter) && it.propertyPath.StartsWith(path))
        {
            enter = false;
            if (SkipField(it, path, threshold))
                continue;

            SerializedProperty field = it.Copy();
            float height = EditorGUI.GetPropertyHeight(field, true);
            if (draw)
                EditorGUI.PropertyField(new Rect(x, y, width, height), field, true);
            y += height + spacing;
        }

        return y;
    }

    // ゾーン候補や、すでに1枚を渡して判定するリストでは、役割は使わない。
    // HP条件は、閾値タイプが固定値なら基準HP、比率なら基準HP比率だけを出す。
    static bool SkipField(SerializedProperty field, string conditionPath, SerializedProperty threshold)
    {
        if (field.name == "m_Script")
            return true;
        if (SkipUnusedHpField(field, threshold))
            return true;
        if (field.name != "見るカード")
            return false;
        return conditionPath.Contains("カード状態")
            || conditionPath.Contains("対象状態")
            || conditionPath.Contains("対象ごとの条件")
            || conditionPath.Contains("被ダメージ条件");
    }

    static bool SkipUnusedHpField(SerializedProperty field, SerializedProperty threshold)
    {
        if (threshold == null)
            return false;
        if (field.name != "基準HP" && field.name != "基準HP比率")
            return false;

        bool ratio = threshold.enumValueIndex == (int)HPThresholdType.最大HP比率以下
            || threshold.enumValueIndex == (int)HPThresholdType.最大HP比率以上;
        return field.name == "基準HP" ? ratio : !ratio;
    }
}
