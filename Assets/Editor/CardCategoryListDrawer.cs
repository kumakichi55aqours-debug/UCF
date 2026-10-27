using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(CardCategoryList))]
public class CardCategoryListDrawer : PropertyDrawer
{
    struct Entry
    {
        public string Name;
        public string Group;
        public string Reading;
        public int EnumIndex;
    }

    static readonly string[] GroupOrder = { "種類", "昭和シリーズ", "平成シリーズ", "その他" };

    static List<Entry> _entries;

    static void EnsureEntries()
    {
        if (_entries != null) return;

        var declared = new List<Entry>();
        var groupOrder = new List<string>(GroupOrder);
        FieldInfo[] fields = typeof(CardCategoryList).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (FieldInfo field in fields)
        {
            var attr = field.GetCustomAttribute<CardCategoryAttribute>();
            string group = attr != null && !string.IsNullOrEmpty(attr.Group) ? attr.Group : "未分類";
            string reading = attr != null && !string.IsNullOrEmpty(attr.Reading) ? attr.Reading : field.Name;
            if (!groupOrder.Contains(group)) groupOrder.Add(group);
            declared.Add(new Entry
            {
                Name = field.Name,
                Group = group,
                Reading = NormalizeReading(reading),
                EnumIndex = -1
            });
        }

        declared.Sort((a, b) =>
        {
            int group = groupOrder.IndexOf(a.Group).CompareTo(groupOrder.IndexOf(b.Group));
            if (group != 0) return group;
            int reading = string.CompareOrdinal(a.Reading, b.Reading);
            if (reading != 0) return reading;
            return string.CompareOrdinal(a.Name, b.Name);
        });
        _entries = declared;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EnsureEntries();
        EditorGUI.BeginProperty(position, label, property);

        int currentIndex = property.hasMultipleDifferentValues ? -1 : property.enumValueIndex;
        string button = "未設定";
        if (!property.hasMultipleDifferentValues &&
            currentIndex >= 0 && currentIndex < property.enumNames.Length)
        {
            button = property.enumNames[currentIndex];
        }

        Rect field = EditorGUI.PrefixLabel(position, label);
        bool mixed = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        if (EditorGUI.DropdownButton(field, new GUIContent(button), FocusType.Keyboard))
        {
            string[] names = property.enumNames;
            var indexed = new List<Entry>(_entries.Count);
            foreach (Entry entry in _entries)
            {
                int enumIndex = Array.IndexOf(names, entry.Name);
                if (enumIndex < 0) continue;
                indexed.Add(new Entry
                {
                    Name = entry.Name,
                    Group = entry.Group,
                    Reading = entry.Reading,
                    EnumIndex = enumIndex
                });
            }

            var serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;
            var menu = new GenericMenu();
            foreach (Entry entry in indexed)
            {
                int picked = entry.EnumIndex;
                menu.AddItem(new GUIContent(entry.Group + "/" + entry.Name), picked == currentIndex, () =>
                {
                    serializedObject.Update();
                    SerializedProperty target = serializedObject.FindProperty(propertyPath);
                    if (target == null) return;
                    target.enumValueIndex = picked;
                    serializedObject.ApplyModifiedProperties();
                });
            }
            menu.DropDown(field);
        }
        EditorGUI.showMixedValue = mixed;

        EditorGUI.EndProperty();
    }

    static string NormalizeReading(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var chars = new List<char>(text.Length);
        foreach (char raw in text)
        {
            char c = raw;
            if (c >= 'Ａ' && c <= 'Ｚ') c = (char)(c - 'Ａ' + 'A');
            else if (c >= 'ａ' && c <= 'ｚ') c = (char)(c - 'ａ' + 'a');
            else if (c >= 'ァ' && c <= 'ヶ') c = (char)(c - 0x60);

            switch (c)
            {
                case 'ぁ': c = 'あ'; break;
                case 'ぃ': c = 'い'; break;
                case 'ぅ': c = 'う'; break;
                case 'ぇ': c = 'え'; break;
                case 'ぉ': c = 'お'; break;
                case 'ゃ': c = 'や'; break;
                case 'ゅ': c = 'ゆ'; break;
                case 'ょ': c = 'よ'; break;
                case 'っ': c = 'つ'; break;
                case 'ゎ': c = 'わ'; break;
            }

            if (c == 'ー' || c == 'ｰ')
            {
                if (chars.Count > 0 && TryGetVowel(chars[chars.Count - 1], out char vowel))
                {
                    chars.Add(vowel);
                    continue;
                }
            }

            chars.Add(c);
        }

        return new string(chars.ToArray());
    }

    static bool TryGetVowel(char kana, out char vowel)
    {
        switch (kana)
        {
            case 'あ': case 'か': case 'が': case 'さ': case 'ざ': case 'た': case 'だ':
            case 'な': case 'は': case 'ば': case 'ぱ': case 'ま': case 'や': case 'ら': case 'わ':
                vowel = 'あ'; return true;
            case 'い': case 'き': case 'ぎ': case 'し': case 'じ': case 'ち': case 'ぢ':
            case 'に': case 'ひ': case 'び': case 'ぴ': case 'み': case 'り':
                vowel = 'い'; return true;
            case 'う': case 'く': case 'ぐ': case 'す': case 'ず': case 'つ': case 'づ':
            case 'ぬ': case 'ふ': case 'ぶ': case 'ぷ': case 'む': case 'ゆ': case 'る': case 'ゔ':
                vowel = 'う'; return true;
            case 'え': case 'け': case 'げ': case 'せ': case 'ぜ': case 'て': case 'で':
            case 'ね': case 'へ': case 'べ': case 'ぺ': case 'め': case 'れ':
                vowel = 'え'; return true;
            case 'お': case 'こ': case 'ご': case 'そ': case 'ぞ': case 'と': case 'ど':
            case 'の': case 'ほ': case 'ぼ': case 'ぽ': case 'も': case 'よ': case 'ろ': case 'を':
                vowel = 'お'; return true;
            default:
                vowel = '\0'; return false;
        }
    }
}
