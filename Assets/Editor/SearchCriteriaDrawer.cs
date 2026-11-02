using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SearchCriteria))]
public class SearchCriteriaDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Layout(property, label, position.x, position.y, position.width, true);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return Layout(property, label, 0f, 0f, 0f, false);
    }

    static float Layout(SerializedProperty property, GUIContent label, float x, float y, float width, bool draw)
    {
        SerializedProperty source = property.FindPropertyRelative("ソースカード条件");
        SerializedProperty filters = property.FindPropertyRelative("フィルタ");
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float line = EditorGUIUtility.singleLineHeight;

        if (draw)
        {
            EditorGUI.LabelField(new Rect(x, y, width, line), label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
        }
        y += line + spacing;

        if (source != null && !HidesSourceCardCondition(property))
            y = PlaceProperty(source, null, x, y, width, spacing, draw);

        if (filters != null)
        {
            if (draw)
                ApplyDefaultsToNewFilters(filters);

            var filtersLabel = new GUIContent("フィルタ", "追加した条件のいずれか1つに合致すれば対象。1つの条件の中では、カテゴリはすべて必要。");
            y = PlaceProperty(filters, filtersLabel, x, y, width, spacing, draw);

            if (draw)
                ApplyDefaultsToNewFilters(filters);
        }

        if (draw)
            EditorGUI.indentLevel--;
        return y;
    }

    static float PlaceProperty(
        SerializedProperty property,
        GUIContent label,
        float x,
        float y,
        float width,
        float spacing,
        bool draw)
    {
        float height = EditorGUI.GetPropertyHeight(property, true);
        if (draw)
        {
            var rect = new Rect(x, y, width, height);
            if (label == null)
                EditorGUI.PropertyField(rect, property, true);
            else
                EditorGUI.PropertyField(rect, property, label, true);
        }
        return y + height + spacing;
    }

    static bool HidesSourceCardCondition(SerializedProperty criteria)
    {
        // 枚数ダメージの「対象条件」は数えるカード用。効果の対象は「適用条件」。
        if (criteria.name == "対象条件")
        {
            if (FindSibling(criteria, "適用条件") != null)
                return false;
        }
        else if (criteria.name != "適用条件")
        {
            return false;
        }

        SerializedProperty target = FindSibling(criteria, "効果対象");
        if (target == null || target.propertyType != SerializedPropertyType.Enum)
            return false;

        int index = target.enumValueIndex;
        string[] names = target.enumNames;
        if (index < 0 || index >= names.Length)
            return false;

        return names[index] == nameof(TargetList.自分);
    }

    static SerializedProperty FindSibling(SerializedProperty property, string siblingName)
    {
        string path = property.propertyPath;
        int dot = path.LastIndexOf('.');
        if (dot < 0)
            return null;

        SerializedProperty parent = property.serializedObject.FindProperty(path.Substring(0, dot));
        if (parent == null)
            return null;

        return parent.FindPropertyRelative(siblingName);
    }

    static void ApplyDefaultsToNewFilters(SerializedProperty filters)
    {
        for (int i = 0; i < filters.arraySize; i++)
        {
            SerializedProperty element = filters.GetArrayElementAtIndex(i);
            if (!IsZeroInitializedFilter(element))
                continue;

            SerializedProperty cost = element.FindPropertyRelative("コスト");
            if (cost != null)
                cost.intValue = -1;

            SerializedProperty compare = element.FindPropertyRelative("コスト比較");
            if (compare != null)
                compare.enumValueIndex = (int)MatchType.同じ;

            SerializedProperty initialized = element.FindPropertyRelative("コスト比較設定済み");
            if (initialized != null)
                initialized.boolValue = true;
        }
    }

    static bool IsZeroInitializedFilter(SerializedProperty element)
    {
        SerializedProperty initialized = element.FindPropertyRelative("コスト比較設定済み");
        if (initialized == null || initialized.boolValue)
            return false;

        SerializedProperty cost = element.FindPropertyRelative("コスト");
        if (cost == null || cost.intValue != 0)
            return false;

        SerializedProperty kind = element.FindPropertyRelative("カードの種類");
        if (kind != null && kind.enumValueIndex != (int)CardKind.None)
            return false;

        SerializedProperty id = element.FindPropertyRelative("カードID");
        if (id != null && id.intValue != 0)
            return false;

        SerializedProperty word = element.FindPropertyRelative("サーチワード");
        if (word != null && !string.IsNullOrEmpty(word.stringValue))
            return false;

        SerializedProperty types = element.FindPropertyRelative("カードのタイプ");
        if (types != null && types.arraySize > 0)
            return false;

        SerializedProperty categories = element.FindPropertyRelative("カードのカテゴリ");
        if (categories != null && categories.arraySize > 0)
            return false;

        return true;
    }
}
