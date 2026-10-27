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

    static float TypeSelectorHeight =>
        EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;

    static void EnsureTypeCache()
    {
        if (_options != null) return;

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
        foreach (var folder in _folderNames)
        {
            var inFolder = _options.Where(o => o.Folder == folder).ToArray();
            _optionsByFolder[folder] = inFolder;
            _skillNamesByFolder[folder] = inFolder.Select(o => o.DisplayName).ToArray();
        }
    }

    static string GetFolderLabel(Type type)
    {
        string path = FindScriptPath(type);
        if (string.IsNullOrEmpty(path)) return RootFolderLabel;

        path = path.Replace('\\', '/');
        if (!path.StartsWith(EffectsRoot, StringComparison.OrdinalIgnoreCase))
            return RootFolderLabel;

        string relative = path.Substring(EffectsRoot.Length);
        int slash = relative.LastIndexOf('/');
        if (slash < 0) return RootFolderLabel;
        return relative.Substring(0, slash);
    }

    static string FindScriptPath(Type type)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == type)
                return path;
        }
        return null;
    }

    static string FolderOf(Type type)
    {
        if (type == null || _options == null) return RootFolderLabel;
        for (int i = 0; i < _options.Count; i++)
        {
            if (_options[i].Type == type) return _options[i].Folder;
        }
        return RootFolderLabel;
    }

    static string GetDisplayName(Type type)
    {
        var attr = (SkillDisplayNameAttribute)Attribute.GetCustomAttribute(type, typeof(SkillDisplayNameAttribute));
        return attr != null ? attr.Name : type.Name;
    }

    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
    {
        EnsureTypeCache();
        EditorGUI.BeginProperty(pos, label, prop);

        float x = pos.x, y = pos.y, w = pos.width, sp = EditorGUIUtility.standardVerticalSpacing;
        bool isNested = IsNestedSkill(prop);
        y = DrawGrantedSkillLabel(prop, x, y, w, sp);

        if (prop.managedReferenceValue == null)
        {
            DrawTypePopup(new Rect(x, y, w, EditorGUIUtility.singleLineHeight), prop, null);
            EditorGUI.EndProperty();
            return;
        }

        var currentType = prop.managedReferenceValue.GetType();
        y = DrawTypePopup(new Rect(x, y, w, EditorGUIUtility.singleLineHeight), prop, currentType);
        y += sp;

        if (!isNested)
        {
            foreach (var fieldName in CommonFields)
            {
                var fieldProp = prop.FindPropertyRelative(fieldName);
                if (fieldProp == null) continue;
                if (fieldName == "タイミング")
                {
                    float lh = EditorGUIUtility.singleLineHeight;
                    DrawTimingField(new Rect(x, y, w, lh), fieldProp);
                    y += lh + sp;
                    var timing = (ActiveTiming)fieldProp.enumValueIndex;
                    if (TryGetMoveGroup(timing, out _))
                    {
                        DrawMoveUseChecks(new Rect(x, y, w, lh), fieldProp);
                        y += lh + sp;
                    }
                    if (timing == ActiveTiming.登場時)
                    {
                        DrawSummonKindChecks(new Rect(x, y, w, lh), prop);
                        y += lh + sp;
                    }
                    if (IsOwnPhaseTiming(timing))
                    {
                        DrawOwnPhaseField(new Rect(x, y, w, lh), prop);
                        y += lh + sp;
                    }
                    continue;
                }
                float h = EditorGUI.GetPropertyHeight(fieldProp, true);
                EditorGUI.PropertyField(new Rect(x, y, w, h), fieldProp, true);
                y += h + sp;
            }
        }

        DrawTypeSpecificFields(prop, ref y, x, w, sp, isNested);
        DrawTimingExtras(prop, ref y, x, w, sp);

        var condProp = prop.FindPropertyRelative("条件");
        if (condProp != null)
        {
            float lh = EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(new Rect(x, y, w, lh), "■ 発動条件");
            y += lh + sp;
            float ch = EditorGUI.GetPropertyHeight(condProp, true);
            EditorGUI.PropertyField(new Rect(x, y, w, ch), condProp, true);
        }

        EditorGUI.EndProperty();
    }

    float DrawTypePopup(Rect rect, SerializedProperty prop, Type currentType)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float sp = EditorGUIUtility.standardVerticalSpacing;

        string currentFolder = FolderOf(currentType);
        int folderIdx = Array.IndexOf(_folderNames, currentFolder);
        if (folderIdx < 0) folderIdx = 0;

        EditorGUI.BeginChangeCheck();
        int newFolderIdx = EditorGUI.Popup(rect, "フォルダ", folderIdx, _folderNames);
        bool folderChanged = EditorGUI.EndChangeCheck();
        if (newFolderIdx < 0 || newFolderIdx >= _folderNames.Length)
        {
            newFolderIdx = folderIdx;
            folderChanged = false;
        }

        string folder = _folderNames[newFolderIdx];
        var options = _optionsByFolder[folder];
        var skillNames = _skillNamesByFolder[folder];
        int skillIdx = IndexOfType(options, currentType);
        // 未設定のとき表示上の先頭が (付与) なので、そのまま代入すると付与スキルが毎フレーム増える。
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

        Rect skillRect = new Rect(rect.x, rect.yMax + sp, rect.width, line);
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

        return skillRect.yMax;
    }

    static int IndexOfType(SkillTypeOption[] options, Type type)
    {
        if (type == null) return -1;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i].Type == type) return i;
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
            next.どこから = previous.どこから != null
                ? new List<CardZoneType>(previous.どこから)
                : new List<CardZoneType>();
            next.いる場所 = previous.いる場所 != null
                ? new List<CardZoneType>(previous.いる場所)
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

    void DrawTypeSpecificFields(SerializedProperty prop, ref float y, float x, float w, float sp, bool isNested)
    {
        var it = prop.Copy();
        var end = it.GetEndProperty();
        var path = prop.propertyPath;
        bool enter = true;
        bool drewHeader = false;
        bool drewSelectTarget = false;
        bool selectsTargets = SelectsTargets(prop);
        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            enter = false;
            if (it.propertyPath == path) continue;
            if (HiddenFields.Contains(it.name)) continue;
            if (CommonFields.Contains(it.name) || it.name == "条件" || it.name == "トリガー対象条件" || it.name == "どこから" || it.name == "いる場所" || it.name == "対象フェイズ" || it.name == "発動側" || it.name == "対象側")
            {
                continue;
            }
            if (isNested && CommonFields.Contains(it.name)) continue;

            if (!drewHeader)
            {
                float lh = EditorGUIUtility.singleLineHeight;
                EditorGUI.LabelField(new Rect(x, y, w, lh), "■ パラメータ");
                y += lh + sp;
                drewHeader = true;
            }

            var field = it.Copy();
            float h = EditorGUI.GetPropertyHeight(field, true);
            EditorGUI.PropertyField(new Rect(x, y, w, h), field, true);
            y += h + sp;

            if (field.name == "効果対象" && UsesSideAndSelection(field))
            {
                DrawTargetSide(prop, ref y, x, w, sp);
                if (selectsTargets && !drewSelectTarget)
                {
                    DrawSelectTarget(prop, ref y, x, w, sp);
                    drewSelectTarget = true;
                }
            }
        }
    }

    // 自分は発動カード、技対象は技の対象カードだけ。側も選択数も見ない。
    static bool UsesSideAndSelection(SerializedProperty effectTarget)
    {
        if (effectTarget == null || effectTarget.propertyType != SerializedPropertyType.Enum)
            return true;

        int index = effectTarget.enumValueIndex;
        string[] names = effectTarget.enumNames;
        if (index < 0 || index >= names.Length)
            return true;

        string name = names[index];
        return name != nameof(TargetList.自分) && name != nameof(TargetList.技対象);
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

    static void DrawTargetSide(SerializedProperty prop, ref float y, float x, float w, float sp)
    {
        var side = prop.FindPropertyRelative("対象側");
        if (side == null)
            return;

        float h = EditorGUI.GetPropertyHeight(side, true);
        EditorGUI.PropertyField(
            new Rect(x, y, w, h),
            side,
            new GUIContent("対象側", "プレイヤーはこのカードの側。相手はその反対。両方は両側。自分と技対象では使わない。"));
        y += h + sp;
    }

    static float TargetSideHeight(SerializedProperty prop, float sp)
    {
        var side = prop.FindPropertyRelative("対象側");
        if (side == null)
            return 0f;
        return sp + EditorGUI.GetPropertyHeight(side, true);
    }

    static void DrawSelectTarget(SerializedProperty prop, ref float y, float x, float w, float sp)
    {
        var selectProp = prop.FindPropertyRelative("対象選択");
        if (selectProp == null)
            return;

        DrawSelectTargetChild(selectProp, "選択数", ref y, x, w, sp);
        DrawSelectTargetChild(selectProp, "選択方式", ref y, x, w, sp);
    }

    static void DrawSelectTargetChild(SerializedProperty parent, string name, ref float y, float x, float w, float sp)
    {
        var child = parent.FindPropertyRelative(name);
        if (child == null)
            return;

        float h = EditorGUI.GetPropertyHeight(child, true);
        EditorGUI.PropertyField(new Rect(x, y, w, h), child, true);
        y += h + sp;
    }

    static float SelectTargetFieldsHeight(SerializedProperty prop, float sp)
    {
        var selectProp = prop.FindPropertyRelative("対象選択");
        if (selectProp == null)
            return 0f;

        float height = 0f;
        var count = selectProp.FindPropertyRelative("選択数");
        var mode = selectProp.FindPropertyRelative("選択方式");
        if (count != null)
            height += sp + EditorGUI.GetPropertyHeight(count, true);
        if (mode != null)
            height += sp + EditorGUI.GetPropertyHeight(mode, true);
        return height;
    }

    static void DrawTimingExtras(SerializedProperty prop, ref float y, float x, float w, float sp)
    {
        var timingProp = prop.FindPropertyRelative("タイミング");
        if (timingProp == null) return;

        ActiveTiming timing = (ActiveTiming)timingProp.enumValueIndex;
        string triggerLabel = GetTriggerConditionLabel(timing);
        if (!string.IsNullOrEmpty(triggerLabel))
        {
            DrawActivationOwnerField(ref y, x, w, sp, prop);
            var triggerCondProp = prop.FindPropertyRelative("トリガー対象条件");
            if (triggerCondProp != null)
            {
                float lh = EditorGUIUtility.singleLineHeight;
                EditorGUI.LabelField(new Rect(x, y, w, lh), triggerLabel);
                y += lh + sp;
                float tch = EditorGUI.GetPropertyHeight(triggerCondProp, true);
                EditorGUI.PropertyField(new Rect(x, y, w, tch), triggerCondProp, true);
                y += tch + sp;
            }
        }

        if (RequiresFromLocation(timing))
        {
            var fromLocationProp = prop.FindPropertyRelative("どこから");
            if (fromLocationProp != null)
            {
                float lh = EditorGUIUtility.singleLineHeight;
                EditorGUI.LabelField(new Rect(x, y, w, lh), "■ どこから");
                y += lh + sp;
                float flh = EditorGUI.GetPropertyHeight(fromLocationProp, true);
                EditorGUI.PropertyField(new Rect(x, y, w, flh), fromLocationProp, true);
                y += flh + sp;
            }
        }

        if (RequiresDefeaterZone(timing))
        {
            var zoneProp = prop.FindPropertyRelative("いる場所");
            if (zoneProp != null)
            {
                float lh = EditorGUIUtility.singleLineHeight;
                EditorGUI.LabelField(new Rect(x, y, w, lh), "■ いる場所（撃破したバトラーのゾーン）");
                y += lh + sp;
                float zh = EditorGUI.GetPropertyHeight(zoneProp, true);
                EditorGUI.PropertyField(new Rect(x, y, w, zh), zoneProp, true);
                y += zh + sp;
            }
        }
    }

    public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
    {
        EnsureTypeCache();
        float sp = EditorGUIUtility.standardVerticalSpacing;
        float total = TypeSelectorHeight;
        if (IsGrantedSkillField(prop))
            total += EditorGUIUtility.singleLineHeight + sp;
        if (prop.managedReferenceValue == null) return total;

        bool isNested = IsNestedSkill(prop);
        if (!isNested)
        {
            foreach (var fieldName in CommonFields)
            {
                var fieldProp = prop.FindPropertyRelative(fieldName);
                if (fieldProp == null) continue;
                if (fieldName == "タイミング")
                {
                    total += sp + EditorGUIUtility.singleLineHeight;
                    var timing = (ActiveTiming)fieldProp.enumValueIndex;
                    if (TryGetMoveGroup(timing, out _))
                        total += sp + EditorGUIUtility.singleLineHeight;
                    if (timing == ActiveTiming.登場時)
                        total += sp + EditorGUIUtility.singleLineHeight;
                    if (IsOwnPhaseTiming(timing))
                        total += sp + EditorGUIUtility.singleLineHeight;
                    continue;
                }
                total += sp + EditorGUI.GetPropertyHeight(fieldProp, true);
            }
        }

        var it = prop.Copy();
        var end = it.GetEndProperty();
        var path = prop.propertyPath;
        bool enter = true;
        bool drewHeader = false;
        bool drewSelectTarget = false;
        bool selectsTargets = SelectsTargets(prop);
        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
        {
            enter = false;
            if (it.propertyPath == path) continue;
            if (HiddenFields.Contains(it.name)) continue;
            if (CommonFields.Contains(it.name) || it.name == "条件" || it.name == "トリガー対象条件" || it.name == "どこから" || it.name == "いる場所" || it.name == "対象フェイズ" || it.name == "発動側" || it.name == "対象側")
            {
                continue;
            }
            if (!drewHeader)
            {
                total += sp + EditorGUIUtility.singleLineHeight;
                drewHeader = true;
            }
            var field = it.Copy();
            total += sp + EditorGUI.GetPropertyHeight(field, true);
            if (field.name == "効果対象" && UsesSideAndSelection(field))
            {
                total += TargetSideHeight(prop, sp);
                if (selectsTargets && !drewSelectTarget)
                {
                    total += SelectTargetFieldsHeight(prop, sp);
                    drewSelectTarget = true;
                }
            }
        }

        var timingProp = prop.FindPropertyRelative("タイミング");
        if (timingProp != null)
        {
            ActiveTiming timing = (ActiveTiming)timingProp.enumValueIndex;
            if (!string.IsNullOrEmpty(GetTriggerConditionLabel(timing)))
            {
                total += sp + EditorGUIUtility.singleLineHeight;
                var triggerCondProp = prop.FindPropertyRelative("トリガー対象条件");
                if (triggerCondProp != null)
                {
                    total += sp + EditorGUIUtility.singleLineHeight;
                    total += sp + EditorGUI.GetPropertyHeight(triggerCondProp, true);
                }
            }
            if (RequiresFromLocation(timing))
            {
                var fromLocationProp = prop.FindPropertyRelative("どこから");
                if (fromLocationProp != null)
                {
                    total += sp + EditorGUIUtility.singleLineHeight;
                    total += sp + EditorGUI.GetPropertyHeight(fromLocationProp, true);
                }
            }
            if (RequiresDefeaterZone(timing))
            {
                var zoneProp = prop.FindPropertyRelative("いる場所");
                if (zoneProp != null)
                {
                    total += sp + EditorGUIUtility.singleLineHeight;
                    total += sp + EditorGUI.GetPropertyHeight(zoneProp, true);
                }
            }
        }

        var condProp = prop.FindPropertyRelative("条件");
        if (condProp != null)
        {
            total += sp + EditorGUIUtility.singleLineHeight;
            total += sp + EditorGUI.GetPropertyHeight(condProp, true);
        }

        return Mathf.Max(total, EditorGUIUtility.singleLineHeight);
    }

    static bool IsNestedSkill(SerializedProperty prop)
    {
        string path = prop.propertyPath;
        return path.Contains("subSkills") ||
               path.Contains("Yesスキル") ||
               path.Contains("Noスキル") ||
               path.Contains("成立時スキル") ||
               path.Contains("不成立時スキル") ||
               path.Contains("付与スキル");
    }

    static bool IsGrantedSkillField(SerializedProperty prop)
    {
        return prop.propertyPath.EndsWith("付与スキル");
    }

    static float DrawGrantedSkillLabel(SerializedProperty prop, float x, float y, float w, float sp)
    {
        if (!IsGrantedSkillField(prop))
            return y;

        float lh = EditorGUIUtility.singleLineHeight;
        EditorGUI.LabelField(new Rect(x, y, w, lh), "■ 付与スキル");
        return y + lh + sp;
    }

    static void DrawActivationOwnerField(ref float y, float x, float w, float sp, SerializedProperty prop)
    {
        var sideProp = prop.FindPropertyRelative("発動側");
        if (sideProp == null)
            return;

        float lh = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(
            new Rect(x, y, w, lh),
            sideProp,
            new GUIContent("対象の側", "プレイヤーは、このスキルを持つカードの側。相手はその反対。両方は側を見ない。"));
        y += lh + sp;
    }

    static string GetTriggerConditionLabel(ActiveTiming timing)
    {
        return timing switch
        {
            ActiveTiming.他のカードが墓地に送られた時 => "■ トリガー対象条件（どのカードが墓地に送られた時か）",
            ActiveTiming.他のバトラーが撃破した時 => "■ トリガー対象条件（どのバトラーが撃破した時か）",
            ActiveTiming.他効果誘発 => "■ トリガー対象条件（どのカードの効果の発動に対してか）",
            _ => null
        };
    }

    static readonly string[] OwnPhaseOptionNames = { "ドロー", "メイン", "バトル", "エンド" };

    static bool IsOwnPhaseTiming(ActiveTiming timing)
    {
        return timing == ActiveTiming.自分のフェイズ開始時 || timing == ActiveTiming.自分のフェイズ終了時;
    }

    static void DrawOwnPhaseField(Rect row, SerializedProperty skillProp)
    {
        var phaseProp = skillProp.FindPropertyRelative("対象フェイズ");
        if (phaseProp == null) return;

        int current = phaseProp.enumValueIndex;
        if (current < 0 || current >= OwnPhaseOptionNames.Length)
            current = 0;

        int next = EditorGUI.Popup(row, "対象フェイズ", current, OwnPhaseOptionNames);
        if (next != current && next >= 0 && next < OwnPhaseOptionNames.Length)
            phaseProp.enumValueIndex = next;
    }

    static bool RequiresFromLocation(ActiveTiming timing)
    {
        return timing == ActiveTiming.墓地に送られた時 ||
               timing == ActiveTiming.他のカードが墓地に送られた時;
    }

    static bool RequiresDefeaterZone(ActiveTiming timing)
    {
        return timing == ActiveTiming.他のバトラーが撃破した時;
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
        if (_timingOptions != null) return;

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
        foreach (var candidate in MoveGroups)
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
            if (names[i] == name) return i;
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
        if (!TryGetMoveGroup(timing, out MoveTimingGroup group)) return;

        bool move1 = timing == group.Both || timing == group.Move1;
        bool move2 = timing == group.Both || timing == group.Move2;

        Rect content = EditorGUI.PrefixLabel(row, new GUIContent("対象の技"));
        float toggleW = Mathf.Min(72f, content.width * 0.5f);
        bool new1 = EditorGUI.ToggleLeft(new Rect(content.x, content.y, toggleW, row.height), "技1", move1);
        bool new2 = EditorGUI.ToggleLeft(new Rect(content.x + toggleW, content.y, toggleW, row.height), "技2", move2);
        if (new1 == move1 && new2 == move2) return;

        ActiveTiming next;
        if (new1 && new2) next = group.Both;
        else if (new1) next = group.Move1;
        else if (new2) next = group.Move2;
        else return;

        timingProp.enumValueIndex = EnumIndex(timingProp, next);
    }

    static void DrawSummonKindChecks(Rect row, SerializedProperty skillProp)
    {
        var normalExclude = skillProp.FindPropertyRelative("通常登場を除く");
        var specialExclude = skillProp.FindPropertyRelative("特殊登場を除く");
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
