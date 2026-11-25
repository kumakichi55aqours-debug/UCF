using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class HelpMainSubject
{
    [Tooltip("いちばん上の段に並ぶカテゴリ名")]
    public string subject = "メインサブジェクト";

    public List<HelpEntry> subSubjects = new List<HelpEntry>();
}

[Serializable]
public class HelpEntry
{
    [Tooltip("メインの下の段に並ぶ項目名")]
    public string subject = "サブサブジェクト";

    [TextArea(5, 16)]
    [Tooltip("下段に表示する本文。TextMeshPro の制御文字が使えます。例: <color=#FFCC66>黄色</color> <b>太字</b> <size=40>大きい</size>")]
    public string text = "";

    [Tooltip("画像などを置くときだけ割り当てる。未設定なら本文テキストだけを表示する。Prefab を開いて中身を編集できる。")]
    public GameObject pagePrefab;
}

[CreateAssetMenu(fileName = "HelpCatalog", menuName = "UCF/Help Catalog")]
public class HelpCatalog : ScriptableObject
{
    public List<HelpMainSubject> subjects = new List<HelpMainSubject>();

    [SerializeField, HideInInspector]
    List<HelpEntry> entries = new List<HelpEntry>();

    void OnEnable()
    {
        MigrateLegacyEntries();
    }

    void OnValidate()
    {
        MigrateLegacyEntries();
    }

    void MigrateLegacyEntries()
    {
        if (subjects == null)
            subjects = new List<HelpMainSubject>();
        if (subjects.Count > 0 || entries == null || entries.Count == 0)
            return;

        subjects.Add(new HelpMainSubject
        {
            subject = "ヘルプ",
            subSubjects = new List<HelpEntry>(entries)
        });
        entries.Clear();
    }
}
