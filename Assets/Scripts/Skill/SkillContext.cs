using UnityEngine;
using System.Collections.Generic;

public class SkillContext
{
    public CardController 発動カード { get; private set; } // 発動元（攻撃側）
    public CardController 対象カード { get; private set; }   // 攻撃対象
    public CardController 条件対象カード { get; private set; } // スキル内の対象カード
    public Transform TargetZone { get; private set; }

    public bool RestrictTargetsToContextCard { get; set; }

    // false のとき、TargetCard が条件を満たしていても対象選択UIを開く。
    // 起動効果の宣言時は、まだプレイヤーが対象を選んでいない。
    public bool ReuseTargetCard { get; set; } = true;

    // 付与の再実行や永続の再計算では、相手も同じ効果を自分で走らせる。結果のRPCは出さない。
    public bool 相手も同じ効果を実行する { get; set; }

    // 永続効果の再計算からの実行。付けるたびに増える効果は、ここでは付け直さない。
    public bool 再計算 { get; set; }

    // サブスキル間で共有するランタイム変数格納用
    public Dictionary<string, int> IntVariables { get; private set; } = new Dictionary<string, int>();

    public void SetArg(SkillArg arg, int value)
    {
        if (!SkillArgKey.TryGetKey(arg, out string key))
            return;
        IntVariables[key] = value;
    }

    public bool TryGetArg(SkillArg arg, out int value)
    {
        value = 0;
        if (!SkillArgKey.TryGetKey(arg, out string key))
            return false;
        return IntVariables.TryGetValue(key, out value);
    }

    /// <summary>
    /// 技で倒したバトラーの、カードに書かれたHPを渡す。バトラーでなければ 0。
    /// </summary>
    public void SetDefeatedOriginalHp(CardController defeated)
    {
        int hp = 0;
        if (defeated != null && defeated.model is BattlerModel battler)
            hp = Mathf.Max(0, battler.maxHp);
        SetArg(SkillArg.倒したバトラーの元々のHP, hp);
    }

    // 直前のサブスキルが処理したカード。
    public List<CardController> 処理したカード { get; } = new List<CardController>();

    // スキル間で参照する番号付きの処理結果。
    // 例：0 = Aの効果で処理したカード、1 = Bの効果で処理したカード
    private readonly Dictionary<int, List<CardController>> 処理カード履歴
        = new Dictionary<int, List<CardController>>();

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

    /// <summary>
    /// 指定番号に処理したカードを保存する。
    /// 同じ番号を指定した場合は、その番号の結果を上書きする。
    /// </summary>
    public void SetProcessedCards(int number, IEnumerable<CardController> cards)
    {
        var result = new List<CardController>();

        if (cards != null)
        {
            foreach (var card in cards)
            {
                if (card != null)
                    result.Add(card);
            }
        }

        処理カード履歴[number] = result;

        // 従来どおり、最後に処理したカードも更新する。
        処理したカード.Clear();
        処理したカード.AddRange(result);
    }

    /// <summary>
    /// 指定番号の処理カードを取得する。
    /// </summary>
    public IReadOnlyList<CardController> GetProcessedCards(int number)
    {
        if (処理カード履歴.TryGetValue(number, out var cards))
            return cards;

        return null;
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

    /// <summary>
    /// 発動条件を、スキルを持つカードから見る。
    /// 攻撃者、攻撃対象、ダメージ、技の変数は元のコンテキストのまま残す。
    /// 相手の技使用時は発動カードが空で渡る。空のままだと自分のゾーンが相手側になる。
    /// </summary>
    public SkillContext AsOwnedBy(CardController owner)
    {
        if (owner == null || 発動カード == owner)
            return this;

        var copy = new SkillContext(owner, 対象カード, TargetZone);
        copy.RestrictTargetsToContextCard = RestrictTargetsToContextCard;
        copy.ReuseTargetCard = ReuseTargetCard;
        copy.相手も同じ効果を実行する = 相手も同じ効果を実行する;
        copy.IntVariables = IntVariables;
        copy.CalculatedDamage = CalculatedDamage;
        copy.FinalDamage = FinalDamage;
        copy.AttackerElement = AttackerElement;
        copy.MoveIndex = MoveIndex;
        copy.Attacker = Attacker;
        copy.移した攻撃対象 = 移した攻撃対象;
        return copy;
    }

    // スキル内の対象カードを取得
    public void SetConditionTarget(CardController card)
    {
        条件対象カード = card;
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

    /// <summary>
    /// 強化作戦の付与先を処理カード番号0に登録する。
    /// </summary>
    public void RegisterEnhancementHosts(CardController source)
    {
        if (source == null ||
            source.cardKind != CardKind.作戦 ||
            source.type != CardTypeList.強化)
            return;

        var hosts = BattleManager.Instance?.GetEnhancementHosts(source);

        if (hosts != null)
            SetProcessedCards(1, hosts);
    }
}
