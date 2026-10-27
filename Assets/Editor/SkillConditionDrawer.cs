using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// インターフェースおよびそのサブクラスをハンドルする
[CustomPropertyDrawer(typeof(ISkillCondition), true)]
public class SkillConditionDrawer : PropertyDrawer
{
    // ここで選択可能にしたい条件クラスを列挙
    static readonly List<Type> _types = new List<Type>
    {
        typeof(HandCountCondition),
        typeof(HpCondition),
        typeof(CardExistsCondition),
        typeof(TargetHPCondition),
        typeof(ZoneEmptyCondition),
        typeof(DamageCondition),
        typeof(AttackerElementCondition),
        typeof(PlayerResourceCondition),
        typeof(StatusAilmentCondition),
        // 追加の条件クラスがあればここに書く
    };
    static readonly string[] _typeNames = _types.Select(t => t.Name).ToArray();

    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
    {
        EditorGUI.BeginProperty(pos, label, prop);

        // 1行目：型選択ポップアップ
        Rect r = new Rect(pos.x, pos.y, pos.width, EditorGUIUtility.singleLineHeight);
        var current = prop.managedReferenceValue?.GetType();
        int idx = _types.IndexOf(current ?? _types[0]);
        int newIdx = EditorGUI.Popup(r, "条件タイプ", idx, _typeNames);
        if (newIdx != idx)
        {
            // 型が変わったらインスタンス生成してセット
            prop.managedReferenceValue = Activator.CreateInstance(_types[newIdx]);
            prop.serializedObject.ApplyModifiedProperties();
        }

        // 2行目以降：そのクラス自身のフィールドを描画
        float y = pos.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        var it = prop.Copy();
        bool enter = true;
        var path = it.propertyPath;
        while (it.NextVisible(enter) && it.propertyPath.StartsWith(path))
        {
            enter = false;
            // managed reference の内部管理用フィールドはスキップ
            if (it.name == "m_Script") continue;

            float h = EditorGUI.GetPropertyHeight(it, true);
            Rect fieldRect = new Rect(pos.x, y, pos.width, h);
            EditorGUI.PropertyField(fieldRect, it, true);
            y += h + EditorGUIUtility.standardVerticalSpacing;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
    {
        // ベース１行分＋実フィールド分
        float h = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        var it = prop.Copy();
        bool enter = true;
        var path = it.propertyPath;
        while (it.NextVisible(enter) && it.propertyPath.StartsWith(path))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            h += EditorGUI.GetPropertyHeight(it, true) + EditorGUIUtility.standardVerticalSpacing;
        }
        return h;
    }
}
