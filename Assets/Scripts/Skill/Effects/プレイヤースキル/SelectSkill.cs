using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public enum SelectSide
{
    エネミー,
    プレイヤー
}

[Serializable]
public class SelectChoice
{
    [Tooltip("ボタンに出す文章")]
    public string 文章;

    [SerializeReference] public List<SkillDB> スキル = new List<SkillDB>();
}

/// <summary>
/// 文章ボタンを縦に出して選ばせ、選ばれた選択肢の効果だけを実行する。
/// プレイヤーは発動したカードの側、エネミーはその相手。
/// </summary>
[Serializable]
[SkillDisplayName("選択(セレクト)")]
public class SelectSkill : SkillDB
{
    [Tooltip("空なら「どちらを選択しますか？」")]
    public string タイトル = "どちらを選択しますか？";

    [Tooltip("プレイヤーは発動したカードの側。エネミーはその相手")]
    public SelectSide 選択者 = SelectSide.エネミー;

    [Tooltip("選ぶ側がCPUのとき、先頭の選択肢を選ぶ。オフにするとランダム")]
    public bool オフラインは先頭を選ぶ = true;

    [Tooltip("選ばれた番号（1始まり）を入れるキー。空なら記録しない")]
    public string 結果出力キー;

    public List<SelectChoice> 選択肢 = new List<SelectChoice>
    {
        new SelectChoice(),
        new SelectChoice()
    };

    public override async Task ActivateEffect(SkillContext context)
    {
        if (context == null || context.発動カード == null)
            return;

        var choices = LiveChoices();
        if (choices.Count == 0)
            return;

        var labels = new string[choices.Count];
        var selectable = new bool[choices.Count];
        bool anySelectable = false;
        for (int i = 0; i < choices.Count; i++)
        {
            labels[i] = ResolveText(choices[i].文章, $"選択肢{i + 1}", context);
            selectable[i] = ChoiceIsSelectable(choices[i], context);
            if (selectable[i])
                anySelectable = true;
        }

        if (!anySelectable)
            return;

        string title = ResolveText(タイトル, "どちらを選択しますか？", context);
        int index = await Choose(context.発動カード, title, labels, selectable);
        if (index < 0 || index >= choices.Count || !selectable[index])
            return;

        if (!string.IsNullOrEmpty(結果出力キー))
            context.IntVariables[結果出力キー] = index + 1;

        await ActivateSubSkills(choices[index].スキル, context);
    }

    List<SelectChoice> LiveChoices()
    {
        var choices = new List<SelectChoice>();
        if (選択肢 == null)
            return choices;

        foreach (var choice in 選択肢)
        {
            if (choice != null)
                choices.Add(choice);
        }

        return choices;
    }

    async Task<int> Choose(CardController source, string title, string[] labels, bool[] selectable)
    {
        bool ownerChooses = 選択者 == SelectSide.プレイヤー;
        bool localHumanChooses = ownerChooses ? source.isMyCard : !source.isMyCard;

        if (!BattleManager.Instance.IsOnlineMode)
        {
            if (!localHumanChooses)
                return PickAutomatic(labels.Length, selectable);

            return await ShowToLocalHuman(title, labels, selectable, notifyOpponent: false);
        }

        if (!source.isMyCard)
            return -1;

        if (ownerChooses)
            return await ShowToLocalHuman(title, labels, selectable, notifyOpponent: true);

        var sync = OnlineSyncManager.Instance;
        if (sync == null)
        {
            Debug.LogWarning("[SelectSkill] OnlineSyncManagerが存在しないため、相手の選択を待ちます。");
            await new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            return -1;
        }

        return await sync.WhileOpponentDecides(
            () => sync.RequestOpponentSelectDecision(source, title, labels, selectable),
            notifyOpponent: true);
    }

    static async Task<int> ShowToLocalHuman(string title, string[] labels, bool[] selectable, bool notifyOpponent)
    {
        var ui = OpponentSelectUI.Ensure();
        if (ui == null || labels == null || labels.Length == 0)
            return FirstSelectable(labels != null ? labels.Length : 0, selectable);

        var sync = OnlineSyncManager.Instance;
        if (sync == null)
            return await ui.WaitForChoice(title, labels, selectable);

        return await sync.WhileOpponentDecides(
            () => ui.WaitForChoice(title, labels, selectable),
            notifyOpponent);
    }

    /// <summary>
    /// 選択肢の先にあるスキルの発動条件を見る。条件がないスキルは選べる。
    /// 先のスキルが複合なら、その中の条件も見る。
    /// </summary>
    static bool ChoiceIsSelectable(SelectChoice choice, SkillContext context)
    {
        if (choice == null || choice.スキル == null || choice.スキル.Count == 0)
            return true;

        for (int i = 0; i < choice.スキル.Count; i++)
        {
            if (!SkillConditionsMet(choice.スキル[i], context))
                return false;
        }

        return true;
    }

    static bool SkillConditionsMet(SkillDB skill, SkillContext context)
    {
        if (skill == null)
            return true;
        if (!skill.CanActivate(context))
            return false;

        if (skill is CompositeSkill composite && composite.subSkills != null)
        {
            for (int i = 0; i < composite.subSkills.Count; i++)
            {
                if (!SkillConditionsMet(composite.subSkills[i], context))
                    return false;
            }
        }

        return true;
    }

    int PickAutomatic(int count, bool[] selectable)
    {
        if (オフラインは先頭を選ぶ)
            return FirstSelectable(count, selectable);

        int open = 0;
        for (int i = 0; i < count; i++)
        {
            if (IsSelectable(i, selectable))
                open++;
        }

        if (open == 0)
            return -1;

        int pick = UnityEngine.Random.Range(0, open);
        for (int i = 0; i < count; i++)
        {
            if (!IsSelectable(i, selectable))
                continue;
            if (pick == 0)
                return i;
            pick--;
        }

        return -1;
    }

    static int FirstSelectable(int count, bool[] selectable)
    {
        for (int i = 0; i < count; i++)
        {
            if (IsSelectable(i, selectable))
                return i;
        }

        return -1;
    }

    static bool IsSelectable(int index, bool[] selectable)
    {
        return selectable == null || index >= selectable.Length || selectable[index];
    }

    static string ResolveText(string template, string fallback, SkillContext context)
    {
        string resolved = SkillContext.ApplyTemplate(template, context);
        return string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
    }

    protected override void CopyPolymorphicFieldsTo(SkillDB clone)
    {
        if (clone is SelectSkill select)
            select.選択肢 = CloneChoices(選択肢);
    }

    static List<SelectChoice> CloneChoices(List<SelectChoice> source)
    {
        var copy = new List<SelectChoice>();
        if (source == null)
            return copy;

        foreach (var choice in source)
        {
            if (choice == null)
                continue;

            copy.Add(new SelectChoice
            {
                文章 = choice.文章,
                スキル = CloneSkillList(choice.スキル)
            });
        }

        return copy;
    }
}
