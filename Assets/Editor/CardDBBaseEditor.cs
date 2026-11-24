
using UnityEditor;
using UnityEngine;

// CardDBBase共通Inspector
public abstract class CardDBBaseEditor : Editor
{
    protected void DrawCommonFields()
    {
        DrawProperty("カード名");
        DrawRow("分類", "ID");
        DrawRow("レア度", "画像");
        DrawProperty("コスト");

        EditorGUILayout.Space(5);

        var categoryProp = serializedObject.FindProperty("カテゴリ");
        EditorGUILayout.PropertyField(categoryProp, true);
        DrawTextArea("テキスト");
        DrawProperty("スキル", true);
    }


    protected void DrawProperty(string name, bool includeChildren = false)
    {
        SerializedProperty prop = serializedObject.FindProperty(name);
        if (prop == null) return;

        if (name == "テキスト")
        {
            EditorGUILayout.PropertyField(prop, true);
        }
        else
        {
            EditorGUILayout.PropertyField(prop, includeChildren);
        }
    }

    protected void DrawRow(params string[] names)
    {
        Rect rect = EditorGUILayout.GetControlRect();

        float spacing = 5f;
        float width = (rect.width - spacing * (names.Length - 1)) / names.Length;
        float previousLabelWidth = EditorGUIUtility.labelWidth;

        for (int i = 0; i < names.Length; i++)
        {
            SerializedProperty prop = serializedObject.FindProperty(names[i]);
            if (prop == null) continue;

            Rect fieldRect = new Rect(
                rect.x + i * (width + spacing),
                rect.y,
                width,
                EditorGUIUtility.singleLineHeight
            );

            EditorGUIUtility.labelWidth = width * 0.35f;
            EditorGUI.PropertyField(fieldRect, prop, true);
        }

        EditorGUIUtility.labelWidth = previousLabelWidth;
    }

    protected void BeginInspector()
    {
        serializedObject.Update();
        EditorGUI.indentLevel = 0;
    }

    protected void EndInspector(int previousIndent)
    {
        EditorGUI.indentLevel = previousIndent;
        serializedObject.ApplyModifiedProperties();
    }

    protected void DrawTextArea(string name)
    {
        SerializedProperty prop = serializedObject.FindProperty(name);
        if (prop == null) return;

        GUIStyle style = new GUIStyle(EditorStyles.textArea)
        {
            fontSize = 11,
            wordWrap = false
        };

        prop.stringValue = EditorGUILayout.TextArea(
            prop.stringValue,
            style,
            GUILayout.MinHeight(60)
        );
    }
}

// バトラーカード
[CustomEditor(typeof(BattlerDB))]
public class CardDBDrawer : CardDBBaseEditor
{
    public override void OnInspectorGUI()
    {
        int previousIndent = EditorGUI.indentLevel;
        BeginInspector();

        // 基本情報
        DrawProperty("カード名");
        DrawRow("分類", "ID");
        DrawRow("レア度", "画像");

        EditorGUILayout.Space(5);

        // ステータス
        DrawRow("タイプ", "コスト", "hp");

        EditorGUILayout.Space(5);

        var categoryProp = serializedObject.FindProperty("カテゴリ");
        EditorGUILayout.PropertyField(categoryProp, true);
        DrawTextArea("テキスト");
        DrawProperty("スキル", true);

        EditorGUILayout.Space(5);

        // 属性情報
        DrawRow("得意属性", "属性軽減値");
        DrawRow("弱点属性", "属性増加値");

        EditorGUILayout.Space(10);

        // 技1
        EditorGUILayout.LabelField("技1", EditorStyles.boldLabel);
        DrawProperty("技名1");
        DrawRow("属性1", "EN1", "PW1");
        DrawMoveDetails(1);

        EditorGUILayout.Space(10);

        // 技2
        EditorGUILayout.LabelField("技2", EditorStyles.boldLabel);
        DrawProperty("技名2");
        DrawRow("属性2", "EN2", "PW2");
        DrawMoveDetails(2);

        EditorGUILayout.Space(10);

        // 召喚ボイス
        EditorGUILayout.LabelField("召喚ボイス", EditorStyles.boldLabel);
        DrawProperty("ボイス");

        EndInspector(previousIndent);
    }

    void DrawMoveDetails(int index)
    {
        string suffix = index.ToString();

        DrawProperty("対象" + suffix);
        DrawProperty("対象側" + suffix);
        DrawProperty("対象選択" + suffix, true);
        DrawProperty("効果" + suffix);
    }
}

// 作戦カード
[CustomEditor(typeof(TacticsDB))]
public class TacticsDBEditor : CardDBBaseEditor
{
    public override void OnInspectorGUI()
    {
        int previousIndent = EditorGUI.indentLevel;
        BeginInspector();

        DrawCommonFields();

        EditorGUILayout.Space(5);
        DrawProperty("タイプ");

        EndInspector(previousIndent);
    }
}

// フィールドカード
[CustomEditor(typeof(FieldDB))]
public class FieldDBEditor : CardDBBaseEditor
{
    public override void OnInspectorGUI()
    {
        int previousIndent = EditorGUI.indentLevel;
        BeginInspector();

        DrawCommonFields();

        EditorGUILayout.Space(5);
        DrawProperty("hp");

        EndInspector(previousIndent);
    }
}
