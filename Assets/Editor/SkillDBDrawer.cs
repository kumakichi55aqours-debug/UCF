using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SkillDB), true)]
public class SkillDBDrawer : PropertyDrawer
{
    static readonly string[] CommonFields =
    {
        "スキル名",
        "タイミング",
        "場所リスト",
        "上限回数",
        "強制発動"
    };

    // 共通欄か、タイミングに応じて後から出す欄。種別ごとのループでは飛ばす。
    static readonly HashSet<string> MovedFields = new HashSet<string>
    {
        "スキル名",
        "タイミング",
        "場所リスト",
        "上限回数",
        "強制発動",
        "条件",
        "トリガー対象条件",
        "対象元ゾーン",
        "対象ゾーン",
        "対象フェイズ",
        "発動側",
        "対象側"
    };

    static readonly HashSet<string> HiddenFields = new HashSet<string>
    {
        "使用回数",
        "m_Script"
    };

    const string EffectsRoot = "Assets/Scripts/Skill/Effects/";
    const string RootFolderLabel = "その他";

    class SkillTypeOption
    {
        public Type Type;
        public string DisplayName;
        public string Folder;
    }

    static List<SkillTypeOption> _options;
    static string[] _folderNames;
    static Dictionary<string, SkillTypeOption[]> _optionsByFolder;
    static Dictionary<string, string[]> _skillNamesByFolder;
    static ActiveTiming[] _timingOptions;
    static string[] _timingOptionNames;

    static float Line => EditorGUIUtility.singleLineHeight;
    static float Gap => EditorGUIUtility.standardVerticalSpacing;
    static float TypeSelectorHeight => Line * 2f + Gap;

    struct Cursor
    {
        public float X;
        public float Y;
        public float Width;
        public bool Draw;

        public void Span(float height, Action<Rect> paint)
        {
            Y += Gap;
            if (Draw && paint != null)
                paint(new Rect(X, Y, Width, height));
            Y += height;
        }

        public void LineSpan(Action<Rect> paint) => Span(Line, paint);

        public void Text(string text) => LineSpan(rect => EditorGUI.LabelField(rect, text));

        public void Property(SerializedProperty prop, GUIContent label = null)
        {
            if (prop == null)
                return;

            float height = EditorGUI.GetPropertyHeight(prop, true);
            Span(height, rect =>
            {
                if (label == null)
                    EditorGUI.PropertyField(rect, prop, true);
                else
                    EditorGUI.PropertyField(rect, prop, label, true);
            });
        }
    }

    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
    {
        EnsureTypeCache();
        EditorGUI.BeginProperty(pos, label, prop);
        Layout(prop, pos.x, pos.y, pos.width, true);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
    {
        EnsureTypeCache();
        return Mathf.Max(Layout(prop, 0f, 0f, 0f, false), Line);
    }

    static float Layout(SerializedProperty prop, float x, float y, float width, bool draw)
    {
        string role = SkillRoleLabel(prop);
        if (role != null)
        {
            if (draw)
                EditorGUI.LabelField(new Rect(x, y, width, Line), role);
            y += Line + Gap;
        }

        bool expanded = IsExpanded(prop);
        if (draw)
        {
            bool next = EditorGUI.Foldout(new Rect(x, y, width, Line), expanded, FoldTitle(prop), true);
            if (next != expanded)
                SetExpanded(prop, next);
            expanded = next;
        }
        y += Line + Gap;
        if (!expanded)
            return y;

        if (draw)
            DrawTypePopup(new Rect(x, y, width, Line), prop, prop.managedReferenceValue?.GetType());
        y += TypeSelectorHeight;

        if (prop.managedReferenceValue == null)
            return y;

        var cursor = new Cursor { X = x, Y = y, Width = width, Draw = draw };
        if (!IsNestedSkill(prop))
            LayoutCommonFields(prop, ref cursor);
        LayoutSpecificFields(prop, ref cursor);
        LayoutTimingExtras(prop, ref cursor);
        LayoutConditions(prop, ref cursor);
        return cursor.Y;
    }

    static void LayoutCommonFields(SerializedProperty prop, ref Cursor cursor)
    {
        foreach (string fieldName in CommonFields)
        {
            SerializedProperty fieldProp = prop.FindPropertyRelative(fieldName);
            if (fieldProp == null)
                continue;

            if (fieldName != "タイミング")
            {
                cursor.Property(fieldProp);
                continue;
            }

            cursor.LineSpan(rect => DrawTimingField(rect, fieldProp));
            ActiveTiming timing = (ActiveTiming)fieldProp.enumValueIndex;
            if (TryGetMoveGroup(timing, out _))
                cursor.LineSpan(rect => DrawMoveUseChecks(rect, fieldProp));
            if (UsesAppearKind(timing))
                cursor.LineSpan(rect => DrawSummonKindChecks(rect, prop));
            if (IsOwnPhaseTiming(timing))
                cursor.LineSpan(rect => DrawOwnPhaseField(rect, prop));
        }
    }

    static void LayoutSpecificFields(SerializedProperty prop, ref Cursor cursor)
    {
        SerializedProperty it = prop.Copy();
        SerializedProperty end = it.GetEndProperty();
        string path = prop.propertyPath;
        bool enter = true;
        bool drewHeader = false;
        bool drewSelectTarget = false;
        bool selectsTargets = SelectsTargets(prop);
        bool hasEffectTarget = prop.FindPropertyRelative("効果対象") != null;

        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            enter = false;
            if (it.propertyPath == path || HiddenFields.Contains(it.name) || MovedFields.Contains(it.name))
                continue;

            if (!drewHeader)
            {
                cursor.Text("■ パラメータ");
                drewHeader = true;
            }

            SerializedProperty field = it.Copy();
            cursor.Property(field);
            if (field.name != "効果対象" || !UsesSideAndSelection(field))
                continue;

            cursor.Property(
                prop.FindPropertyRelative("対象側"),
                new GUIContent("対象側", "プレイヤーはこのカードの側。相手はその反対。両方は両側。自分と技対象では使わない。"));
            if (!selectsTargets || drewSelectTarget || !ShowsSelectionCount(field))
                continue;

            LayoutSelectTarget(prop, ref cursor);
            drewSelectTarget = true;
        }

        if (hasEffectTarget)
            return;

        SerializedProperty side = prop.FindPropertyRelative("対象側");
        if (side == null)
            return;

        if (!drewHeader)
            cursor.Text("■ パラメータ");
        cursor.Property(
            side,
            new GUIContent("対象側", "プレイヤーはこのカードの側。相手はその反対。両方は両側。"));
    }

    static void LayoutSelectTarget(SerializedProperty prop, ref Cursor cursor)
    {
        SerializedProperty selectProp = prop.FindPropertyRelative("対象選択");
        if (selectProp == null)
            return;

        cursor.Property(selectProp.FindPropertyRelative("選択数"));
        cursor.Property(selectProp.FindPropertyRelative("選択方式"));
    }

    static void LayoutTimingExtras(SerializedProperty prop, ref Cursor cursor)
    {
        SerializedProperty timingProp = prop.FindPropertyRelative("タイミング");
        if (timingProp == null)
            return;

        ActiveTiming timing = (ActiveTiming)timingProp.enumValueIndex;
        SerializedProperty side = prop.FindPropertyRelative("発動側");
        if (RequiresActivationOwner(timing) && side != null)
        {
            cursor.LineSpan(rect => EditorGUI.PropertyField(
                rect,
                side,
                new GUIContent("対象の側", "プレイヤーは、このスキルを持つカードの側。相手はその反対。両方は側を見ない。")));
        }

        string triggerLabel = GetTriggerConditionLabel(timing);
        if (!string.IsNullOrEmpty(triggerLabel))
        {
            SerializedProperty triggerCondProp = prop.FindPropertyRelative("トリガー対象条件");
            if (triggerCondProp != null)
            {
                cursor.Text(triggerLabel);
                cursor.Property(triggerCondProp);
            }
        }

        if (RequiresFromLocation(timing))
            LayoutLabeledProperty(prop, "対象元ゾーン", "■ どこから（空ならどこからでも）", ref cursor);
        if (RequiresDefeaterZone(timing))
            LayoutLabeledProperty(prop, "対象ゾーン", "■ いる場所（撃破したバトラーのゾーン）", ref cursor);
    }

    static void LayoutLabeledProperty(SerializedProperty prop, string fieldName, string caption, ref Cursor cursor)
    {
        SerializedProperty field = prop.FindPropertyRelative(fieldName);
        if (field == null)
            return;

        cursor.Text(caption);
        cursor.Property(field);
    }

    static void LayoutConditions(SerializedProperty prop, ref Cursor cursor)
    {
        LayoutLabeledProperty(prop, "条件", "■ 発動条件", ref cursor);
    }

    static void EnsureTypeCache()
    {
        if (_options != null)
            return;

        _options = TypeCache.GetTypesDerivedFrom<SkillDB>()
            .Where(t => !t.IsAbstract && !t.IsGenericType)
            .Select(t => new SkillTypeOption
            {
                Type = t,
                DisplayName = GetDisplayName(t),
                Folder = GetFolderLabel(t)
            })
            .OrderBy(o => o.DisplayName)
            .ToList();

        _folderNames = _options
            .Select(o => o.Folder)
            .Distinct()
            .OrderBy(name => name == RootFolderLabel ? 0 : 1)
            .ThenBy(name => name)
            .ToArray();

        _optionsByFolder = new Dictionary<string, SkillTypeOption[]>();
        _skillNamesByFolder = new Dictionary<string, string[]>();
        foreach (string folder in _folderNames)
        {
            SkillTypeOption[] inFolder = _options.Where(o => o.Folder == folder).ToArray();
            _optionsByFolder[folder] = inFolder;
            _skillNamesByFolder[folder] = inFolder.Select(o => o.DisplayName).ToArray();
        }
    }

    static string GetFolderLabel(Type type)
    {
        string path = FindScriptPath(type);
        if (string.IsNullOrEmpty(path))
            return RootFolderLabel;

        path = path.Replace('\\', '/');
        if (!path.StartsWith(EffectsRoot, StringComparison.OrdinalIgnoreCase))
            return RootFolderLabel;

        string relative = path.Substring(EffectsRoot.Length);
        int slash = relative.LastIndexOf('/');
        if (slash < 0)
            return RootFolderLabel;
        return relative.Substring(0, slash);
    }

    static string FindScriptPath(Type type)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == type)
                return path;
        }
        return null;
    }

    static string FolderOf(Type type)
    {
        if (type == null || _options == null)
            return RootFolderLabel;

        for (int i = 0; i < _options.Count; i++)
        {
            if (_options[i].Type == type)
                return _options[i].Folder;
        }
        return RootFolderLabel;
    }

    static bool IsExpanded(SerializedProperty prop)
    {
        return SessionState.GetBool(FoldKey(prop), true);
    }

    static void SetExpanded(SerializedProperty prop, bool expanded)
    {
        SessionState.SetBool(FoldKey(prop), expanded);
    }

    static string FoldKey(SerializedProperty prop)
    {
        int id = 0;
        if (prop.serializedObject != null && prop.serializedObject.targetObject != null)
            id = prop.serializedObject.targetObject.GetInstanceID();
        return "UCF.SkillFold." + id + "." + prop.propertyPath;
    }

    static string FoldTitle(SerializedProperty prop)
    {
        if (prop.managedReferenceValue == null)
            return "（未選択）";

        string typeName = GetDisplayName(prop.managedReferenceValue.GetType());
        SerializedProperty nameProp = prop.FindPropertyRelative("スキル名");
        string skillName = nameProp != null ? nameProp.stringValue : null;
        string head = string.IsNullOrEmpty(skillName) ? typeName : skillName;

        SerializedProperty timingProp = prop.FindPropertyRelative("タイミング");
        string timing = null;
        if (timingProp != null
            && timingProp.enumValueIndex >= 0
            && timingProp.enumValueIndex < timingProp.enumDisplayNames.Length)
            timing = timingProp.enumDisplayNames[timingProp.enumValueIndex];

        if (string.IsNullOrEmpty(skillName))
            return string.IsNullOrEmpty(timing) ? typeName : typeName + "  /  " + timing;
        if (string.IsNullOrEmpty(timing))
            return head + "  /  " + typeName;
        return head + "  /  " + typeName + "  /  " + timing;
    }

    static string GetDisplayName(Type type)
    {
        var attr = (SkillDisplayNameAttribute)Attribute.GetCustomAttribute(type, typeof(SkillDisplayNameAttribute));
        return attr != null ? attr.Name : type.Name;
    }

    static void DrawTypePopup(Rect rect, SerializedProperty prop, Type currentType)
    {
        string currentFolder = FolderOf(currentType);
        int folderIdx = Array.IndexOf(_folderNames, currentFolder);
        if (folderIdx < 0)
            folderIdx = 0;

        EditorGUI.BeginChangeCheck();
        int newFolderIdx = EditorGUI.Popup(rect, "フォルダ", folderIdx, _folderNames);
        bool folderChanged = EditorGUI.EndChangeCheck();
        if (newFolderIdx < 0 || newFolderIdx >= _folderNames.Length)
        {
            newFolderIdx = folderIdx;
            folderChanged = false;
        }

        string folder = _folderNames[newFolderIdx];
        SkillTypeOption[] options = _optionsByFolder[folder];
        string[] skillNames = _skillNamesByFolder[folder];
        int skillIdx = IndexOfType(options, currentType);
        bool showUnselected = skillIdx < 0 && !folderChanged;

        string[] popupNames;
        int popupIndex;
        if (showUnselected)
        {
            popupNames = new string[skillNames.Length + 1];
            popupNames[0] = "（未選択）";
            Array.Copy(skillNames, 0, popupNames, 1, skillNames.Length);
            popupIndex = 0;
        }
        else
        {
            popupNames = skillNames;
            popupIndex = skillIdx >= 0 ? skillIdx : 0;
        }

        Rect skillRect = new Rect(rect.x, rect.yMax + Gap, rect.width, Line);
        EditorGUI.BeginChangeCheck();
        int newPopupIndex = popupNames.Length > 0
            ? EditorGUI.Popup(skillRect, "スキルタイプ", popupIndex, popupNames)
            : -1;
        bool skillChanged = EditorGUI.EndChangeCheck();

        Type nextType = null;
        if (folderChanged)
        {
            if (options.Length > 0)
                nextType = options[0].Type;
        }
        else if (skillChanged && showUnselected)
        {
            int pick = newPopupIndex - 1;
            if (pick >= 0 && pick < options.Length)
                nextType = options[pick].Type;
        }
        else if (skillChanged && newPopupIndex >= 0 && newPopupIndex < options.Length && newPopupIndex != skillIdx)
        {
            nextType = options[newPopupIndex].Type;
        }

        if (nextType != null && nextType != currentType)
            AssignSkillType(prop, nextType);
    }

    static int IndexOfType(SkillTypeOption[] options, Type type)
    {
        if (type == null)
            return -1;

        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Type == type)
                return i;
        }
        return -1;
    }

    static void AssignSkillType(SerializedProperty prop, Type type)
    {
        var next = (SkillDB)Activator.CreateInstance(type);
        if (prop.managedReferenceValue is SkillDB previous)
        {
            next.スキル名 = previous.スキル名;
            next.タイミング = previous.タイミング;
            next.場所リスト = previous.場所リスト != null
                ? new List<ActiveLocation>(previous.場所リスト)
                : new List<ActiveLocation>();
            next.上限回数 = previous.上限回数 > 0 ? previous.上限回数 : 1;
            next.強制発動 = previous.強制発動;
            next.条件 = previous.条件 != null
                ? new List<ISkillCondition>(previous.条件)
                : new List<ISkillCondition>();
            next.トリガー対象条件 = previous.トリガー対象条件;
            next.対象元ゾーン = previous.対象元ゾーン != null
                ? new List<CardZoneType>(previous.対象元ゾーン)
                : new List<CardZoneType>();
            next.対象ゾーン = previous.対象ゾーン != null
                ? new List<CardZoneType>(previous.対象ゾーン)
                : new List<CardZoneType>();
            next.対象フェイズ = previous.対象フェイズ;
            next.発動側 = previous.発動側;
            next.通常登場を除く = previous.通常登場を除く;
            next.特殊登場を除く = previous.特殊登場を除く;
            if (previous.対象選択 != null && next.対象選択 != null)
            {
                next.対象選択.選択数 = previous.対象選択.選択数;
                next.対象選択.選択方式 = previous.対象選択.選択方式;
            }
        }
        prop.managedReferenceValue = next;
        prop.serializedObject.ApplyModifiedProperties();
    }

    static bool UsesSideAndSelection(SerializedProperty effectTarget)
    {
        string name = TargetListName(effectTarget);
        if (name == null)
            return true;

        return name != nameof(TargetList.自分) && name != nameof(TargetList.技対象);
    }

    // 人数を選ばない対象。対象側は出す。自分と技対象は対象側ごと出さない。
    static bool ShowsSelectionCount(SerializedProperty effectTarget)
    {
        string name = TargetListName(effectTarget);
        if (name == null)
            return true;

        return name != nameof(TargetList.自分)
            && name != nameof(TargetList.技対象)
            && name != nameof(TargetList.バトラー全員)
            && name != nameof(TargetList.フィールド1枚)
            && name != nameof(TargetList.プレイヤー);
    }

    static string TargetListName(SerializedProperty effectTarget)
    {
        if (effectTarget == null || effectTarget.propertyType != SerializedPropertyType.Enum)
            return null;

        int index = effectTarget.enumValueIndex;
        string[] names = effectTarget.enumNames;
        if (index < 0 || index >= names.Length)
            return null;

        return names[index];
    }

    static bool SelectsTargets(SerializedProperty prop)
    {
        if (prop.managedReferenceValue == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        return prop.managedReferenceValue.GetType()
            .GetFields(flags)
            .Any(field => field.FieldType == typeof(TargetList));
    }

    static bool IsNestedSkill(SerializedProperty prop)
    {
        string path = prop.propertyPath;
        return path.Contains("subSkills") ||
               path.Contains("Yesスキル") ||
               path.Contains("Noスキル") ||
               path.Contains("成立時スキル") ||
               path.Contains("不成立時スキル") ||
               path.Contains("付与スキル") ||
               path.Contains("実行スキル");
    }

    static string SkillRoleLabel(SerializedProperty prop)
    {
        if (prop.propertyPath.EndsWith("付与スキル"))
            return "■ 付与スキル";
        if (prop.propertyPath.EndsWith("実行スキル"))
            return "■ 切れた時に実行";
        return null;
    }

    static string GetTriggerConditionLabel(ActiveTiming timing)
    {
        return timing switch
        {
            ActiveTiming.他のカードが墓地に送られた時 => "■ トリガー対象条件（どのカードが墓地に送られた時か）",
            ActiveTiming.技撃破時 => "■ トリガー対象条件（どのカードを倒した時か）",
            ActiveTiming.技1撃破時 => "■ トリガー対象条件（どのカードを倒した時か）",
            ActiveTiming.技2撃破時 => "■ トリガー対象条件（どのカードを倒した時か）",
            ActiveTiming.他バトラー撃破時 => "■ トリガー対象条件（どのバトラーが撃破した時か）",
            ActiveTiming.他効果誘発 => "■ トリガー対象条件（どのカードの効果の発動に対してか）",
            ActiveTiming.他のカードが登場した時 => "■ トリガー対象条件（どのカードが登場した時か）",
            _ => null
        };
    }

    static readonly string[] OwnPhaseOptionNames = { "ドロー", "メイン", "バトル", "エンド" };

    static bool IsOwnPhaseTiming(ActiveTiming timing)
    {
        return timing == ActiveTiming.自分のフェイズ開始時 || timing == ActiveTiming.自分のフェイズ終了時;
    }

    static bool UsesAppearKind(ActiveTiming timing)
    {
        return timing == ActiveTiming.登場時 || timing == ActiveTiming.他のカードが登場した時;
    }

    static void DrawOwnPhaseField(Rect row, SerializedProperty skillProp)
    {
        SerializedProperty phaseProp = skillProp.FindPropertyRelative("対象フェイズ");
        if (phaseProp == null)
            return;

        int current = phaseProp.enumValueIndex;
        if (current < 0 || current >= OwnPhaseOptionNames.Length)
            current = 0;

        int next = EditorGUI.Popup(row, "対象フェイズ", current, OwnPhaseOptionNames);
        if (next != current && next >= 0 && next < OwnPhaseOptionNames.Length)
            phaseProp.enumValueIndex = next;
    }

    static bool RequiresActivationOwner(ActiveTiming timing)
    {
        return !string.IsNullOrEmpty(GetTriggerConditionLabel(timing))
            || timing == ActiveTiming.他のカードが手札に戻った時;
    }

    static bool RequiresFromLocation(ActiveTiming timing)
    {
        return timing == ActiveTiming.墓地に送られた時 ||
               timing == ActiveTiming.他のカードが墓地に送られた時 ||
               timing == ActiveTiming.他のカードが手札に戻った時 ||
               timing == ActiveTiming.他のカードが登場した時;
    }

    static bool RequiresDefeaterZone(ActiveTiming timing)
    {
        return timing == ActiveTiming.他バトラー技使用時 ||
               timing == ActiveTiming.他バトラー技使用後 ||
               timing == ActiveTiming.他バトラー撃破時;
    }

    struct MoveTimingGroup
    {
        public ActiveTiming Both;
        public ActiveTiming Move1;
        public ActiveTiming Move2;
    }

    static readonly MoveTimingGroup[] MoveGroups =
    {
        new MoveTimingGroup { Both = ActiveTiming.技使用時, Move1 = ActiveTiming.技1使用時, Move2 = ActiveTiming.技2使用時 },
        new MoveTimingGroup { Both = ActiveTiming.技使用後, Move1 = ActiveTiming.技1使用後, Move2 = ActiveTiming.技2使用後 },
        new MoveTimingGroup { Both = ActiveTiming.技撃破時, Move1 = ActiveTiming.技1撃破時, Move2 = ActiveTiming.技2撃破時 },
    };

    static void EnsureTimingOptions()
    {
        if (_timingOptions != null)
            return;

        _timingOptions = ((ActiveTiming[])Enum.GetValues(typeof(ActiveTiming)))
            .Where(t => !IsSpecificMoveTiming(t))
            .ToArray();
        _timingOptionNames = _timingOptions.Select(t => t.ToString()).ToArray();
    }

    static bool IsSpecificMoveTiming(ActiveTiming timing)
    {
        return TryGetMoveGroup(timing, out MoveTimingGroup group) && timing != group.Both;
    }

    static bool TryGetMoveGroup(ActiveTiming timing, out MoveTimingGroup group)
    {
        foreach (MoveTimingGroup candidate in MoveGroups)
        {
            if (timing == candidate.Both || timing == candidate.Move1 || timing == candidate.Move2)
            {
                group = candidate;
                return true;
            }
        }

        group = default;
        return false;
    }

    static int TimingDisplayIndex(ActiveTiming timing)
    {
        if (TryGetMoveGroup(timing, out MoveTimingGroup group))
            timing = group.Both;
        int index = Array.IndexOf(_timingOptions, timing);
        return index >= 0 ? index : 0;
    }

    static int EnumIndex(SerializedProperty prop, ActiveTiming timing)
    {
        string name = timing.ToString();
        string[] names = prop.enumNames;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == name)
                return i;
        }
        return prop.enumValueIndex;
    }

    static void DrawTimingField(Rect row, SerializedProperty timingProp)
    {
        EnsureTimingOptions();
        ActiveTiming timing = (ActiveTiming)timingProp.enumValueIndex;
        int current = TimingDisplayIndex(timing);
        int next = EditorGUI.Popup(row, "タイミング", current, _timingOptionNames);
        if (next != current && next >= 0 && next < _timingOptions.Length)
            timingProp.enumValueIndex = EnumIndex(timingProp, _timingOptions[next]);
    }

    static void DrawMoveUseChecks(Rect row, SerializedProperty timingProp)
    {
        ActiveTiming timing = (ActiveTiming)timingProp.enumValueIndex;
        if (!TryGetMoveGroup(timing, out MoveTimingGroup group))
            return;

        bool move1 = timing == group.Both || timing == group.Move1;
        bool move2 = timing == group.Both || timing == group.Move2;

        Rect content = EditorGUI.PrefixLabel(row, new GUIContent("対象の技"));
        float toggleW = Mathf.Min(72f, content.width * 0.5f);
        bool new1 = EditorGUI.ToggleLeft(new Rect(content.x, content.y, toggleW, row.height), "技1", move1);
        bool new2 = EditorGUI.ToggleLeft(new Rect(content.x + toggleW, content.y, toggleW, row.height), "技2", move2);
        if (new1 == move1 && new2 == move2)
            return;

        ActiveTiming next;
        if (new1 && new2)
            next = group.Both;
        else if (new1)
            next = group.Move1;
        else if (new2)
            next = group.Move2;
        else
            return;

        timingProp.enumValueIndex = EnumIndex(timingProp, next);
    }

    static void DrawSummonKindChecks(Rect row, SerializedProperty skillProp)
    {
        SerializedProperty normalExclude = skillProp.FindPropertyRelative("通常登場を除く");
        SerializedProperty specialExclude = skillProp.FindPropertyRelative("特殊登場を除く");
        if (normalExclude == null || specialExclude == null)
            return;

        bool normal = !normalExclude.boolValue;
        bool special = !specialExclude.boolValue;

        Rect content = EditorGUI.PrefixLabel(row, new GUIContent("発動する登場"));
        float toggleW = content.width * 0.5f;
        bool newNormal = EditorGUI.ToggleLeft(
            new Rect(content.x, content.y, toggleW, row.height),
            new GUIContent("通常", "登場ボタンから場に出したときに発動する"),
            normal);
        bool newSpecial = EditorGUI.ToggleLeft(
            new Rect(content.x + toggleW, content.y, toggleW, row.height),
            new GUIContent("特殊", "スキルで場に出したときに発動する"),
            special);
        if (newNormal == normal && newSpecial == special)
            return;
        if (!newNormal && !newSpecial)
            return;

        normalExclude.boolValue = !newNormal;
        specialExclude.boolValue = !newSpecial;
    }
}
