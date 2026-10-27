using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SearchCriteria))]
public class SearchCriteriaDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var source = property.FindPropertyRelative("ソースカード条件");
        var filters = property.FindPropertyRelative("フィルタ");

        float y = position.y;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float line = EditorGUIUtility.singleLineHeight;

        EditorGUI.LabelField(new Rect(position.x, y, position.width, line), label, EditorStyles.boldLabel);
        y += line + spacing;

        EditorGUI.indentLevel++;
        // 効果対象が「自分」のときは対象が発動カード自身なので、Self 以外を選ぶ余地がない。
        if (source != null && !HidesSourceCardCondition(property))
        {
            float sourceHeight = EditorGUI.GetPropertyHeight(source, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, sourceHeight), source, true);
            y += sourceHeight + spacing;
        }

        if (filters != null)
        {
            // 追加前から残っている未設定行を、描画より先に直す。
            ApplyDefaultsToNewFilters(filters);

            float filtersHeight = EditorGUI.GetPropertyHeight(filters, true);
            var filtersLabel = new GUIContent("フィルタ", "追加した条件のいずれか1つに合致すれば対象。1つの条件の中では、カテゴリはすべて必要。");
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, filtersHeight), filters, filtersLabel, true);

            // + は PropertyField の中で要素を足す。保存前にコスト 0 を -1 にする。
            ApplyDefaultsToNewFilters(filters);
        }
        EditorGUI.indentLevel--;

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var source = property.FindPropertyRelative("ソースカード条件");
        var filters = property.FindPropertyRelative("フィルタ");
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float height = EditorGUIUtility.singleLineHeight + spacing;
        if (source != null && !HidesSourceCardCondition(property))
            height += EditorGUI.GetPropertyHeight(source, true) + spacing;
        if (filters != null)
            height += EditorGUI.GetPropertyHeight(filters, true) + spacing;
        return height;
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

        var target = FindSibling(criteria, "効果対象");
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

        var parent = property.serializedObject.FindProperty(path.Substring(0, dot));
        if (parent == null)
            return null;

        return parent.FindPropertyRelative(siblingName);
    }

    static void ApplyDefaultsToNewFilters(SerializedProperty filters)
    {
        for (int i = 0; i < filters.arraySize; i++)
        {
            var element = filters.GetArrayElementAtIndex(i);
            if (!IsZeroInitializedFilter(element))
                continue;

            var cost = element.FindPropertyRelative("コスト");
            if (cost != null)
                cost.intValue = -1;

            var compare = element.FindPropertyRelative("コスト比較");
            if (compare != null)
                compare.enumValueIndex = (int)MatchType.同じ;

            var initialized = element.FindPropertyRelative("コスト比較設定済み");
            if (initialized != null)
                initialized.boolValue = true;
        }
    }

    static bool IsZeroInitializedFilter(SerializedProperty element)
    {
        var initialized = element.FindPropertyRelative("コスト比較設定済み");
        if (initialized == null || initialized.boolValue)
            return false;

        var cost = element.FindPropertyRelative("コスト");
        if (cost == null || cost.intValue != 0)
            return false;

        var kind = element.FindPropertyRelative("カードの種類");
        if (kind != null && kind.enumValueIndex != (int)CardKind.None)
            return false;

        var id = element.FindPropertyRelative("カードID");
        if (id != null && id.intValue != 0)
            return false;

        var word = element.FindPropertyRelative("サーチワード");
        if (word != null && !string.IsNullOrEmpty(word.stringValue))
            return false;

        var types = element.FindPropertyRelative("カードのタイプ");
        if (types != null && types.arraySize > 0)
            return false;

        var categories = element.FindPropertyRelative("カードのカテゴリ");
        if (categories != null && categories.arraySize > 0)
            return false;

        return true;
    }
}
