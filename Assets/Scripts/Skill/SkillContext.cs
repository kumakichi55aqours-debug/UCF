using UnityEngine;
using System.Collections.Generic;

public class SkillContext
{
    public CardController 発動カード { get; private set; } // 発動元（攻撃側）
    public CardController 対象カード { get; private set; }   // 攻撃対象
    public Transform TargetZone { get; private set; }

    public bool RestrictTargetsToContextCard { get; set; }

    // false のとき、TargetCard が条件を満たしていても対象選択UIを開く。
    // 起動効果の宣言時は、まだプレイヤーが対象を選んでいない。
    public bool ReuseTargetCard { get; set; } = true;

    // サブスキル間で共有するランタイム変数格納用
    public Dictionary<string, int> IntVariables { get; } = new Dictionary<string, int>();

    // 直前のサブスキルが処理したカード。手札へ加えたカードを、続く登場や墓地送りの対象にする。
    public List<CardController> 処理したカード { get; } = new List<CardController>();

    public void SetProcessedCards(IEnumerable<CardController> cards)
    {
        処理したカード.Clear();
        if (cards == null)
            return;

        foreach (var card in cards)
        {
            if (card != null)
                処理したカード.Add(card);
        }
    }

    // 選択スキルが渡した候補。手札へや墓地へは、これがあるときは自分では選び直さない。
    // 0件でも「渡された」ことは残す。見つからなかったときに、次のスキルが別の選択を始めないため。
    public List<SelectionItem> 選択結果 { get; } = new List<SelectionItem>();
    bool 選択結果あり;

    public void SetSelection(IEnumerable<SelectionItem> items)
    {
        選択結果.Clear();
        選択結果あり = true;
        if (items == null)
            return;

        foreach (var item in items)
        {
            if (item != null)
                選択結果.Add(item);
        }
    }

    public bool TryConsumeSelection(out List<SelectionItem> items)
    {
        if (!選択結果あり)
        {
            items = null;
            return false;
        }

        items = new List<SelectionItem>(選択結果);
        選択結果.Clear();
        選択結果あり = false;
        return true;
    }

    // ダメージ関連情報（ダメージを受ける時のスキル用）
    public int? CalculatedDamage { get; set; } // 計算されたダメージ（nullの場合はダメージなし）
    public int? FinalDamage { get; set; } // スキルによって変更された最終ダメージ
    public int? CurrentDamage => FinalDamage ?? CalculatedDamage;
    public ElementList AttackerElement { get; set; } // 攻撃属性
    public int MoveIndex { get; set; } // 技番号（0 or 1）
    public CardController Attacker { get; set; } // 攻撃者（ダメージを受ける時用）
    // 相手側のバトラー1体への技を、受けたバトラーから別の自分バトラーへ移したとき。
    public CardController 移した攻撃対象 { get; set; }
    
    // スキル処理完了を待つためのTaskCompletionSource（ダメージを受ける時用）
    public System.Threading.Tasks.TaskCompletionSource<bool> ProcessingCompleteTcs { get; set; }

    public SkillContext(CardController sourceCard, CardController targetCard, Transform targetZone)
    {
        発動カード = sourceCard;
        対象カード = targetCard;
        TargetZone = targetZone;
    }

    /// <summary>
    /// 技のコンテキストを作る。攻撃対象がいないときは、発動カードで埋めない。
    /// </summary>
    public static SkillContext WithAttackTarget(CardController sourceCard, CardController attackTarget)
    {
        bool hasTarget = attackTarget != null;
        Transform zone = hasTarget
            ? attackTarget.transform
            : sourceCard != null ? sourceCard.transform : null;
        return new SkillContext(sourceCard, hasTarget ? attackTarget : null, zone);
    }

    // ダメージ情報を設定（ダメージを受ける時専用）
    public void SetDamageInfo(int calculatedDamage, ElementList attackerElement, int moveIndex, CardController attacker)
    {
        CalculatedDamage = calculatedDamage;
        FinalDamage = calculatedDamage; // 初期値は計算されたダメージ
        AttackerElement = attackerElement;
        MoveIndex = moveIndex;
        Attacker = attacker;
        ProcessingCompleteTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
    }

    // SourceCardを設定（ContextのSourceCardがnullの場合に使用）
    public void SetSourceCard(CardController sourceCard)
    {
        if (発動カード == null)
            発動カード = sourceCard;
    }

    static readonly Stack<CardController> resolvingSources = new Stack<CardController>();

    public static void PushResolvingSource(CardController source)
    {
        resolvingSources.Push(source);
    }

    public static void PopResolvingSource()
    {
        if (resolvingSources.Count > 0)
            resolvingSources.Pop();
    }

    /// <summary>
    /// オフラインで、今解決中の効果がCPUカードのものか。
    /// 対象選択UIは出さず、候補からランダムに選ぶ。
    /// </summary>
    public static bool IsOfflineCpuResolving
    {
        get
        {
            if (resolvingSources.Count == 0)
                return false;
            if (BattleManager.Instance == null || BattleManager.Instance.IsOnlineMode)
                return false;
            var source = resolvingSources.Peek();
            return source != null && !source.isMyCard;
        }
    }

    /// <summary>
    /// {SourceCard} {Attacker} {Move} を、このコンテキストの名前と技番号に置き換える。
    /// </summary>
    public static string ApplyTemplate(string template, SkillContext context)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        string sourceName = context.発動カード != null ? context.発動カード.cardName : "相手のカード";
        string attackerName = context.Attacker != null ? context.Attacker.cardName : "相手のバトラー";
        string moveNumber = context.MoveIndex >= 0 ? (context.MoveIndex + 1).ToString() : "";

        return template
            .Replace("{SourceCard}", sourceName)
            .Replace("{Attacker}", attackerName)
            .Replace("{Move}", moveNumber);
    }
}
