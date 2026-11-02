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
        typeof(CardCriteriaCondition),
        typeof(ZoneEmptyCondition),
        typeof(DamageCondition),
        typeof(AttackerElementCondition),
        typeof(MoveElementCondition),
        typeof(PlayerResourceCondition),
        typeof(StatusAilmentCondition),
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
        SerializedProperty it = prop.Copy();
        bool enter = true;
        while (it.NextVisible(enter) && it.propertyPath.StartsWith(path))
        {
            enter = false;
            if (SkipField(it, path))
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
    static bool SkipField(SerializedProperty field, string conditionPath)
    {
        if (field.name == "m_Script")
            return true;
        if (field.name != "見るカード")
            return false;
        return conditionPath.Contains("カード状態")
            || conditionPath.Contains("対象状態")
            || conditionPath.Contains("対象ごとの条件")
            || conditionPath.Contains("被ダメージ条件");
    }
}
