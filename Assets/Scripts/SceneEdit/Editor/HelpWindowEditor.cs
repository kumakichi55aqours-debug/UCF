using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HelpWindow))]
public class HelpWindowEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        HelpCatalog catalog = ((HelpWindow)target).Catalog;
        if (catalog == null)
        {
            EditorGUILayout.HelpBox("HelpCatalog が未設定です。共通の項目リストを割り当ててください。", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("共通ヘルプ項目", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "ここで追加・編集した内容は、全Sceneのヘルプで共通です。\n" +
            "メインサブジェクトが上の段、その中のサブサブジェクトが下の段に並びます。\n" +
            "文字色などの制御文字: <color=#FFCC66>黄色</color>  <b>太字</b>  <size=40>大きい</size>\n" +
            "画像などを置く項目は Page Prefab を割り当て、その Prefab を開いて配置します。",
            MessageType.Info);

        SerializedObject catalogObject = new SerializedObject(catalog);
        catalogObject.Update();
        EditorGUILayout.PropertyField(catalogObject.FindProperty("subjects"), new GUIContent("メインサブジェクト"), true);
        catalogObject.ApplyModifiedProperties();
    }
}
