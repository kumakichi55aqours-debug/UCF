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
        // 「対象条件」の場合、効果対象が「自分」なら表示しない。
        if (property.name == "対象条件")
        {
            SerializedProperty target = FindSibling(property, "効果対象");

            if (target != null &&
                target.propertyType == SerializedPropertyType.Enum &&
                target.enumNames[target.enumValueIndex] == nameof(TargetList.自分))
            {
                return 0f;
            }
        }

        SerializedProperty source = property.FindPropertyRelative("ソースカード条件");
        SerializedProperty filters = property.FindPropertyRelative("フィルタ");
        SerializedProperty processedNumbers = property.FindPropertyRelative("処理カード番号");
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float line = EditorGUIUtility.singleLineHeight;

        if (draw)
        {
            EditorGUI.LabelField(new Rect(x, y, width, line), label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
        }
        y += line + spacing;

        if (source != null && !HidesSourceCardCondition(property))
        {
            y = PlaceProperty(source, null, x, y, width, spacing, draw);

            // 処理したカードを指定している場合だけ、番号リストを表示する。
            if (processedNumbers != null && IsProcessedCardSource(source))
            {
                var numbersLabel = new GUIContent(
                    "処理カード番号",
                    "参照する処理カードの番号。複数指定した場合はいずれかの番号に含まれるカードが対象。空欄なら直前に処理したカードを参照。");

                y = PlaceProperty(processedNumbers, numbersLabel, x, y, width, spacing, draw);
            }
        }

        // ソースカード条件が「自身」の場合はフィルタを表示しない。
        bool showFilters = filters != null &&
            (source == null || !IsSelfSource(source));

        if (showFilters)
        {
            if (draw)
                ApplyDefaultsToNewFilters(filters);

            var filtersLabel = new GUIContent(
                "フィルタ",
                "追加した条件のいずれか1つに合致すれば対象。1つの条件の中では、カテゴリはすべて必要。");

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

    static bool IsProcessedCardSource(SerializedProperty source)
    {
        if (source == null || source.propertyType != SerializedPropertyType.Enum)
            return false;

        int index = source.enumValueIndex;
        string[] names = source.enumNames;

        if (index < 0 || index >= names.Length)
            return false;

        string name = names[index];

        return name == nameof(SourceCardType.処理したカード)
            || name == nameof(SourceCardType.処理したカード以外)
            || name == nameof(SourceCardType.処理したカードと同名以外);
    }

    static bool IsSelfSource(SerializedProperty source)
    {
        if (source == null ||
            source.propertyType != SerializedPropertyType.Enum)
            return false;

        int index = source.enumValueIndex;
        string[] names = source.enumNames;

        if (index < 0 || index >= names.Length)
            return false;

        return names[index] == nameof(SourceCardType.自身);
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
