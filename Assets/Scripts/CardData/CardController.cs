using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class CardController : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // 表示更新用
    public BattleCardView battleCardview;  
    public DeckCardView deckCardView;

    //デッキ編集用
    public CardDBBase cardData;
    public DeckEditCustomZone zoneType;

    // 各カード種類が違うため object 型で保持
    public object model;

    // 基本情報
    public int cardID { get; private set; }
    public CardKind cardKind;
    // CardDBList/トークン から読んだバトラー。墓地・手札・デッキ・異次元へは行かず消える。
    public bool トークン { get; private set; }
    public CardTypeList type;
    public string cardName;
    public CardRarity rarity;
    public int cost;
    public List<CardCategoryList> category;

    // バトラー&フィールド共通
    public int curHp;

    // バトラー用
    public ElementList strongElement;
    public ElementList weakElement;
    public int basePower1;
    public int basePower2;
    public int energy1;
    public int energy2;
    public TargetList target1;
    public PlayerType 対象側1;
    public TargetList target2;
    public PlayerType 対象側2;
    public ElementList moveElement1;
    public int downElementPower;
    public ElementList moveElement2;
    public int upElementPower;

    public int bonusPower1 = 0;
    public int bonusPower2 = 0;
    public int bonusEnergy1 = 0;
    public int bonusEnergy2 = 0;
    public int bonusCost = 0;

    public int modifiedPower1;
    public int modifiedPower2;
    public int modifiedEnergy1;
    public int modifiedEnergy2;

    public bool hasUsedMove = false;

    // 最後に墓地へ入ったときの BattleManager.curTurn。まだ入っていなければ int.MinValue
    public int 墓地に送られたターン = int.MinValue;

    // 墓地スキル一覧の見た目用。クリックしたときは墓地にある本体を開く
    CardController graveSkillSource;

    // 特殊ステータス数値
    public int damageBonus = 0;
    // 技の命中率（百分率）。0 と 100 以上は必中。暗闇中は 50
    public int hitRate = 0;

    // スキル
    [SerializeReference]
    public List<SkillDB> skills = new List<SkillDB>();  // カード固有のスキルリスト
    public List<ActivePassiveEffect> passiveEffects = new List<ActivePassiveEffect>();
    public List<StatusAilment> statusAilments = new List<StatusAilment>();

    // 属性無効化効果
    public List<ElementList> invalidElements = new List<ElementList>();

    // ターン制限効果
    public List<TurnRestrictionEffect> turnRestrictions = new List<TurnRestrictionEffect>();

    // 属性耐性無視
    public bool ignoreResistance1 = false;  // 耐性無視が有効かどうか
    public ElementList ignoreResistanceTargetElement1 = ElementList.None;  // None=全属性、それ以外=指定属性のみ
    public bool ignoreResistance2 = false;
    public ElementList ignoreResistanceTargetElement2 = ElementList.None;

    // HP保護
    public bool hpProtection1 = false;  // HP保護が有効かどうか
    public ElementList hpProtectionTargetElement1 = ElementList.None;  // None=全属性、それ以外=指定属性のみ
    public bool hpProtection2 = false;
    public ElementList hpProtectionTargetElement2 = ElementList.None;

    // プレイヤーカードかどうかは、親のゾーンで判断
    public bool isPlayerCard
    {
        get
        {
            var parent = transform.parent;
            return parent == BattleManager.Instance.pBattlerZone
                || parent == BattleManager.Instance.pTacticsZone
                || parent == BattleManager.Instance.pFieldZone
                || parent == BattleManager.Instance.pHandZone
                || parent == BattleManager.Instance.pGraveZone
                || parent == BattleManager.Instance.pDeckZone
                || parent == BattleManager.Instance.pDimensionZone;
        }
    }
    
    // オンライン対戦時の表示判定用：自分のカードかどうか
    public bool isMyCard
    {
        get
        {
            if (!BattleManager.Instance.IsOnlineMode)
                return isPlayerCard;
            else
            {
                // オンライン時は、自分の手札から生成されたカードかどうかで判定
                var parent = transform.parent;
                bool isInMyZone = parent == BattleManager.Instance.pBattlerZone
                    || parent == BattleManager.Instance.pTacticsZone
                    || parent == BattleManager.Instance.pFieldZone
                    || parent == BattleManager.Instance.pHandZone
                    || parent == BattleManager.Instance.pGraveZone
                    || parent == BattleManager.Instance.pDeckZone
                    || parent == BattleManager.Instance.pDimensionZone;

                // 自分のゾーンにあるカードは自分のカード
                return isInMyZone;
            }
        }
    }

    Transform leftDimensionZone;
    Transform parentBeforeChange;

    void OnBeforeTransformParentChanged()
    {
        parentBeforeChange = transform.parent;
        leftDimensionZone = null;
        var battle = BattleManager.Instance;
        if (battle == null)
            return;

        var parent = transform.parent;
        if (parent == battle.pDimensionZone || parent == battle.eDimensionZone)
            leftDimensionZone = parent;
    }

    void OnDestroy()
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return;

        battle.OnCardLeftZone(this, transform.parent);

        var parent = transform.parent;
        if (parent == battle.pDimensionZone || parent == battle.eDimensionZone)
            battle.RequestDimensionPresenceRefresh();
    }

    // 生成直後は親がキャンバスのことがあり、その時点では持ち主が決まらない。
    // 手札や場などのゾーンに入ってから、同名スキルの使用回数を付け直す。
    void OnTransformParentChanged()
    {
        Transform leftZone = parentBeforeChange;
        parentBeforeChange = null;

        var battle = BattleManager.Instance;
        battle?.OnCardLeftZone(this, leftZone);
        if (leftDimensionZone != null)
        {
            battle?.RefreshDimensionZonePresence(leftDimensionZone);
            leftDimensionZone = null;
        }

        var parent = transform.parent;
        bool enteredDimension = battle != null && (parent == battle.pDimensionZone || parent == battle.eDimensionZone);
        if (enteredDimension)
            battle.RefreshDimensionZonePresence(parent);

        if (battle != null && (parent == battle.pGraveZone || parent == battle.eGraveZone)
            && parent != leftZone)
            墓地に送られたターン = battle.curTurn;

        if (cardKind == CardKind.None || !IsPlayZone()) return;
        SkillUseCountStore.ApplyTo(this);

        if (SceneManager.GetActiveScene().name != "BattleScene")
            return;

        bool enteredHand = battle != null && (parent == battle.pHandZone || parent == battle.eHandZone);
        if (enteredHand || enteredDimension)
            PersistentEffectManager.Instance?.RecalculateImmediately();
    }

    bool IsPlayZone()
    {
        var battle = BattleManager.Instance;
        if (battle == null) return false;

        var parent = transform.parent;
        if (parent == null) return false;

        return parent == battle.pBattlerZone || parent == battle.eBattlerZone
            || parent == battle.pTacticsZone || parent == battle.eTacticsZone
            || parent == battle.pFieldZone || parent == battle.eFieldZone
            || parent == battle.pHandZone || parent == battle.eHandZone
            || parent == battle.pGraveZone || parent == battle.eGraveZone
            || parent == battle.pDeckZone || parent == battle.eDeckZone
            || parent == battle.pDimensionZone || parent == battle.eDimensionZone;
    }

    private void Awake()
    {
        battleCardview = GetComponent<BattleCardView>();
        deckCardView = GetComponent<DeckCardView>();
    }

    // カード種類とカードIDに応じたモデルを作成して初期化する
    public void Init(CardKind cardKind, int cardID)
    {
        this.cardID = cardID;
        this.cardKind = cardKind;
        switch (cardKind)
        {
            case CardKind.バトラー:
                var battler = ScriptableObject.CreateInstance<BattlerModel>();
                battler.Init(cardID);
                model = battler;
                トークン = battler.トークン;
                type = battler.type;
                cardName = battler.cardName;
                rarity = battler.rarity;
                curHp = battler.maxHp;
                cost = battler.cost;
                category = battler.category;
                strongElement = battler.得意属性;
                downElementPower = battler.属性軽減値;
                weakElement = battler.弱点属性;
                upElementPower = battler.属性増加値;
                basePower1 = battler.power1;
                basePower2 = battler.power2;
                energy1 = battler.energy1;
                energy2 = battler.energy2;
                RecalculateModifiedPowers();
                target1 = battler.target1;
                対象側1 = battler.対象側1;
                target2 = battler.target2;
                対象側2 = battler.対象側2;
                moveElement1 = battler.moveElement1;
                moveElement2 = battler.moveElement2;
                skills = SkillDB.CloneList(battler.skills);
                break;
            case CardKind.作戦:
                var tactics = ScriptableObject.CreateInstance<TacticsModel>();
                tactics.Init(cardID);
                model = tactics;
                type = tactics.type;
                cardName = tactics.cardName;
                rarity = tactics.rarity;
                cost = tactics.cost;
                category = tactics.category;
                skills = SkillDB.CloneList(tactics.skills);
                break;
            case CardKind.フィールド:
                var field = ScriptableObject.CreateInstance<FieldModel>();
                field.Init(cardID);
                model = field;
                cardName = field.cardName;
                rarity = field.rarity;
                curHp = field.maxHp;
                cost = field.cost;
                category = field.category;
                skills = SkillDB.CloneList(field.skills);
                break;
        }

        if (IsPlayZone())
            SkillUseCountStore.ApplyTo(this);

        if (battleCardview != null)
            battleCardview.Show(model);
        else if (deckCardView != null)
            deckCardView.Show(model);
    }

    /// <summary>
    /// デッキ外のトークンにする。
    /// overrideHp が真のとき、現在HPと最大HPを hp にする。偽のときは生成時のHPのまま。
    /// 技・タイプ・カテゴリは、生成時に読んだカードデータのまま。
    /// </summary>
    public void BecomeToken(bool overrideHp, int hp)
    {
        トークン = true;
        if (!overrideHp)
            return;

        curHp = Mathf.Max(0, hp);
        if (model is BattlerModel battler)
            battler.maxHp = curHp;
        UpdateDisplay();
    }

    public void UpdateDisplay()
    {
        if (SceneManager.GetActiveScene().name == "BattleScene" && battleCardview != null)
        {
            battleCardview.Show(model);
            battleCardview.UpdateCanUseMovePanel();
        }
        else if (SceneManager.GetActiveScene().name == "DeckEditScene")
            deckCardView.Show(model);
    }

    public void SetTargetHighlightVisible(bool isVisible)
    {
        if (battleCardview != null)
        {
            battleCardview.SetTargetPanelVisible(isVisible);
        }
    }

    public void SetTargetChosen(bool chosen)
    {
        battleCardview?.SetTargetChosen(chosen);
    }

    public void BindGraveSkillChoice(CardController source)
    {
        graveSkillSource = source;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (DeckEditCardDrag.IsDragging)
            return;

        if (battleCardview != null && battleCardview.IsBack && !isPlayerCard)
            return;

        if (CardDetailManager.Instance != null && model != null)
            CardDetailManager.Instance.ShowDetail(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (CardDetailManager.Instance != null)
            CardDetailManager.Instance.HideDetail();
    }

    // クリック処理
    public async void OnPointerClick(PointerEventData eventData)
    {
        if (SceneManager.GetActiveScene().name == "BattleScene")
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            if (graveSkillSource != null)
            {
                if (CardContextMenu.Instance != null)
                    CardContextMenu.Instance.ShowGraveSkillOptions(graveSkillSource, transform.position);
                return;
            }

            // ① スキルターゲット選択中なら SkillManager に渡す
            if (TargetSelectionManager.Instance.IsSkillTargetSelectionMode)
            {
                TargetSelectionManager.Instance.ConfirmSkillTargetSelection(this);
                return;
            }

            // ② 通常の右クリックメニュー表示なら
            Vector3 worldPos = transform.position;
            Transform parent = transform.parent;
            
            // 墓地と異次元。起動できるときはメニュー、確認だけなら一覧を開く。
            if (parent == BattleManager.Instance.pGraveZone || parent == BattleManager.Instance.eGraveZone
                || parent == BattleManager.Instance.pDimensionZone || parent == BattleManager.Instance.eDimensionZone)
            {
                bool handled = CardContextMenu.Instance != null
                    && CardContextMenu.Instance.HandlePileClick(this, worldPos);
                if (!handled)
                    ZoneClick.Open(parent);
                return;
            }
            
            // オンライン対戦時の正しいターン制御
            bool canOperate = OnlineSyncManager.Instance.CanPlayerOperateCards();
            if (canOperate && BattleManager.Instance.IsTargetSelectionMode == false)
            {
                // 手札
                if (parent == BattleManager.Instance.pHandZone)
                {
                    CardContextMenu.Instance.ShowHandOptions(this, worldPos);
                }
                // バトラーゾーン、タクティクスゾーン、フィールドゾーン
                else if (parent == BattleManager.Instance.pBattlerZone
                      || parent == BattleManager.Instance.pTacticsZone
                      || parent == BattleManager.Instance.pFieldZone)
                {
                    CardContextMenu.Instance.ShowBattleOptions(this, worldPos);
                }
            }

            // ③ ターゲット選択クリック。バトラー1体は自分側も選べる。
            if (BattleManager.Instance.IsTargetSelectionMode &&
                (parent == BattleManager.Instance.eBattlerZone || parent == BattleManager.Instance.pBattlerZone))
            {
                await BattleManager.Instance.SelectTargetAsync(this);
            }
        }
        else if (SceneManager.GetActiveScene().name == "DeckEditScene")
        {
            if (DeckEditCardDrag.IsDragging || DeckEditManager.Instance == null)
                return;

            if (zoneType == DeckEditCustomZone.カードリスト)
            {
                if (eventData.button == PointerEventData.InputButton.Right)
                    DeckEditManager.Instance.AddCardToDeck(cardData);
            }
            else if (zoneType == DeckEditCustomZone.デッキリスト)
            {
                if (eventData.button == PointerEventData.InputButton.Middle)
                    DeckEditManager.Instance.AddCardToDeck(cardData, transform.GetSiblingIndex() + 1);
                else if (eventData.button == PointerEventData.InputButton.Right)
                    DeckEditManager.Instance.RemoveCardFromDeck(this);
            }
        }
    }

    public void RecalculateModifiedPowers()
    {
        // 全体パワーボーナスはターンで変わるため、ここには含めない。ダメージ計算時にGetMoveBaseDamageが加算する
        modifiedPower1 = basePower1 + bonusPower1;
        modifiedPower2 = basePower2 + bonusPower2;
        modifiedEnergy1 = energy1 + bonusEnergy1;
        modifiedEnergy2 = energy2 + bonusEnergy2;

        // エネルギーが0未満にならないようにする
        if (modifiedPower1 < 0) modifiedPower1 = 0;
        if (modifiedPower2 < 0) modifiedPower2 = 0;
        if (modifiedEnergy1 < 0) modifiedEnergy1 = 0;
        if (modifiedEnergy2 < 0) modifiedEnergy2 = 0;

        if (IsZeroPowerMove(0)) modifiedPower1 = 0;
        if (IsZeroPowerMove(1)) modifiedPower2 = 0;
    }

    /// <summary>
    /// カードに書かれたパワーが0の技。パワー補正を受けず常に0で、表示は「-」。
    /// </summary>
    public bool IsZeroPowerMove(int moveIndex)
    {
        return cardKind == CardKind.バトラー
            && model is BattlerModel battler
            && (moveIndex == 0 ? battler.power1 : battler.power2) <= 0;
    }

    public void ResetBonuses()
    {
        bonusPower1 = 0;
        bonusPower2 = 0;
        bonusEnergy1 = 0;
        bonusEnergy2 = 0;
        bonusCost = 0;
        
        ignoreResistance1 = false;
        ignoreResistanceTargetElement1 = ElementList.None;
        ignoreResistance2 = false;
        ignoreResistanceTargetElement2 = ElementList.None;
    }

    /// <summary>
    /// basePowerをBattlerModelの元の値にリセット
    /// </summary>
    public void ResetBasePower()
    {
        if (cardKind == CardKind.バトラー && model is BattlerModel battler)
        {
            basePower1 = battler.power1;
            basePower2 = battler.power2;
        }
    }

    /// <summary>
    /// 技属性をカードデータの元の値に戻す
    /// </summary>
    public void ResetMoveElements()
    {
        if (cardKind != CardKind.バトラー || !(model is BattlerModel battler))
            return;

        BattlerDB cardDB = BattlerAssets.Load(cardID, out _);
        if (cardDB == null)
            return;

        battler.moveElement1 = cardDB.moveElement1;
        battler.moveElement2 = cardDB.moveElement2;
        moveElement1 = cardDB.moveElement1;
        moveElement2 = cardDB.moveElement2;
        UpdateDisplay();
    }

    public bool IsInZone(ActiveLocation zone)
    {
        switch (zone)
        {
            case ActiveLocation.Bゾーン:
                return transform.parent == BattleManager.Instance.pBattlerZone;
            case ActiveLocation.Tゾーン:
                return transform.parent == BattleManager.Instance.pTacticsZone;
            case ActiveLocation.Fゾーン:
                return transform.parent == BattleManager.Instance.pFieldZone;
            case ActiveLocation.手札:
                return transform.parent == BattleManager.Instance.pHandZone;
            case ActiveLocation.墓地:
                return transform.parent == BattleManager.Instance.pGraveZone;
            case ActiveLocation.異次元:
                return transform.parent == BattleManager.Instance.pDimensionZone;
            default:
                return false;
        }
    }

    public async Task UseZoneActivatedSkill()
    {
        var location = GetCurrentActiveLocation();
        if (location != ActiveLocation.墓地 && location != ActiveLocation.異次元 && (IsActionBlocked || IsSlowed)) return;
        if (NegateZoneEffectSkill.Blocks(this)) return;
        if (skills == null) return;
        var context = new SkillContext(this, this, transform)
        {
            ReuseTargetCard = false
        };

        var usable = skills
            .Where(skill => skill != null && skill.CanUseAt(ActiveTiming.起動効果, location, context))
            .ToList();
        if (usable.Count == 0) return;

        var chosen = usable.Count == 1 ? usable[0] : await ChooseActivatedSkill(usable);
        if (chosen == null) return;

        await SkillManager.ResolveDeclaredIgnition(this, chosen, context);
    }

    static async Task<SkillDB> ChooseActivatedSkill(List<SkillDB> usable)
    {
        var ui = OpponentSelectUI.Ensure();
        if (ui == null) return usable[0];

        var labels = new string[usable.Count];
        for (int i = 0; i < usable.Count; i++)
        {
            string name = usable[i].スキル名;
            if (string.IsNullOrWhiteSpace(name))
                name = usable[i].GetDisplayTypeName();
            labels[i] = string.IsNullOrWhiteSpace(name) ? $"効果{i + 1}" : name;
        }

        const string title = "どの効果を使用しますか？";
        var sync = OnlineSyncManager.Instance;
        bool online = BattleManager.Instance != null && BattleManager.Instance.IsOnlineMode;
        int index = sync == null
            ? await ui.WaitForChoice(title, labels, null)
            : await sync.WhileOpponentDecides(() => ui.WaitForChoice(title, labels, null), notifyOpponent: online);

        return index >= 0 && index < usable.Count ? usable[index] : null;
    }

    public void AddPassiveEffect(ActivePassiveEffect effect)
    {
        passiveEffects.Add(effect);
        UpdatePassiveEffectUI();
    }

    public void RemovePassiveEffect(string skillName)
    {
        var effectToRemove = passiveEffects.FirstOrDefault(e => e.PassiveEffect != null && 
            e.PassiveEffect.GetType().Name.Contains(skillName));
        
        if (effectToRemove != null)
        {
            passiveEffects.Remove(effectToRemove);
            UpdatePassiveEffectUI();
            Debug.Log($"[CardController] パッシブ効果を削除: カード={cardName}, スキル={skillName}");
        }
        else
        {
            Debug.LogWarning($"[CardController] 削除対象のパッシブ効果が見つかりません: カード={cardName}, スキル={skillName}");
        }
    }

    /// <summary>
    /// 出し元の付与を外す。付与が書いていた状態異常も、ほかに同じ付与が無ければ外す。
    /// </summary>
    public bool RemovePassivesFromSource(CardController source)
    {
        if (source == null || passiveEffects == null)
            return false;

        var removedStatuses = new List<StatusAilmentType>();
        int before = passiveEffects.Count;
        passiveEffects.RemoveAll(effect =>
        {
            if (effect == null || effect.SourceCard != source)
                return false;
            if (effect.PassiveEffect is ApplyStatusAilmentSkill statusSkill)
                removedStatuses.Add(statusSkill.状態異常);
            return true;
        });
        if (passiveEffects.Count == before)
            return false;

        RemoveStatusesNoLongerGranted(removedStatuses);
        ResetBonuses();
        RecalculateModifiedPowers();
        UpdatePassiveEffectUI();
        return true;
    }

    /// <summary>
    /// 付与で付いた状態異常は期限なしで付くので、付与が無くなったらここで外す。
    /// </summary>
    void RemoveStatusesNoLongerGranted(IEnumerable<StatusAilmentType> types)
    {
        foreach (var type in types.Distinct())
        {
            bool stillGranted = passiveEffects.Any(e =>
                e?.PassiveEffect is ApplyStatusAilmentSkill other && other.状態異常 == type);
            if (!stillGranted)
                RemoveStatus(type);
        }
    }

    public void RemoveAllPassiveEffects()
    {
        BattleManager.Instance?.RememberEnhancementsLeftBy(this);
        ClearPassiveEffectsOnly();
    }

    public void ClearPassiveEffectsOnly()
    {
        passiveEffects.Clear();
        ResetBonuses();
        RecalculateModifiedPowers();
        UpdatePassiveEffectUI();
    }

    public List<GrantExpireSkill> TickPassiveEffects()
    {
        var expiredStatuses = new List<StatusAilmentType>();
        var expiredGrants = new List<GrantExpireSkill>();
        foreach (var effect in passiveEffects)
        {
            if (effect == null) continue;
            effect.Tick();
            if (effect.RemainingTurns != 0)
                continue;

            BattleManager.Instance?.NoteExpiredEnhancement(effect.SourceCard);

            if (effect.PassiveEffect is ApplyStatusAilmentSkill statusSkill)
                expiredStatuses.Add(statusSkill.状態異常);
            if (effect.RunsOnExpire && effect.PassiveEffect is GrantExpireSkill expire && expire.CanResolveOnExpire)
                expiredGrants.Add(expire);
        }

        int beforeCount = passiveEffects.Count;
        passiveEffects.RemoveAll(e => e != null && e.RemainingTurns == 0);
        if (passiveEffects.Count < beforeCount)
        {
            RemoveStatusesNoLongerGranted(expiredStatuses);
            ResetBonuses();
            RecalculateModifiedPowers();
        }
        UpdatePassiveEffectUI();
        return expiredGrants;
    }


    /// <summary>
    /// ターン制限効果を追加
    /// </summary>
    public void AddTurnRestriction(TurnRestrictionEffect restriction)
    {
        turnRestrictions.Add(restriction);
        UpdatePassiveEffectUI();
    }

    /// <summary>
    /// ターン制限効果を更新（ターン経過時に呼ぶ）
    /// </summary>
    public void TickTurnRestrictions()
    {
        int beforeCount = turnRestrictions.Count;
        foreach (var restriction in turnRestrictions)
        {
            restriction.Tick();
        }
        turnRestrictions.RemoveAll(r => r.IsExpired());
        if (turnRestrictions.Count < beforeCount)
        {
            UpdatePassiveEffectUI();
        }
    }

    /// <summary>
    /// 付いているターン制限で、指定の技が使えないかチェック
    /// </summary>
    public bool IsMoveRestricted(int moveIndex, ElementList moveElement)
    {
        foreach (var restriction in turnRestrictions)
        {
            if (restriction.Restricts(moveIndex, moveElement))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 全てのターン制限効果をクリア
    /// </summary>
    public void RemoveAllTurnRestrictions()
    {
        turnRestrictions.Clear();
        UpdatePassiveEffectUI();
    }
    
    /// <summary>
    /// 現在のActiveLocationを取得
    /// </summary>
    public ActiveLocation GetCurrentActiveLocation()
    {
        return FromTransform(transform.parent);
    }

    /// <summary>
    /// ゾーンTransformからActiveLocationを取得する。
    /// 移動前のゾーンなど、カードの現在の親と違う場所を見るときに使う。
    /// </summary>
    public static ActiveLocation FromTransform(Transform zone)
    {
        if (zone == null) return ActiveLocation.None;

        var battle = BattleManager.Instance;
        if (battle == null) return ActiveLocation.None;

        if (zone == battle.pBattlerZone || zone == battle.eBattlerZone)
            return ActiveLocation.Bゾーン;
        if (zone == battle.pTacticsZone || zone == battle.eTacticsZone)
            return ActiveLocation.Tゾーン;
        if (zone == battle.pFieldZone || zone == battle.eFieldZone)
            return ActiveLocation.Fゾーン;
        if (zone == battle.pHandZone || zone == battle.eHandZone)
            return ActiveLocation.手札;
        if (zone == battle.pGraveZone || zone == battle.eGraveZone)
            return ActiveLocation.墓地;
        if (zone == battle.pDimensionZone || zone == battle.eDimensionZone)
            return ActiveLocation.異次元;
        if (zone == battle.pDeckZone || zone == battle.eDeckZone)
            return ActiveLocation.デッキ;

        return ActiveLocation.None;
    }

    /// <summary>
    /// ゾーンTransformから、自分側と相手側を区別したCardZoneTypeを取得する。
    /// 「プレイヤー」はローカルプレイヤー側。
    /// </summary>
    public static CardZoneType ZoneTypeFromTransform(Transform zone)
    {
        if (zone == null) return CardZoneType.None;

        var battle = BattleManager.Instance;
        if (battle == null) return CardZoneType.None;

        if (zone == battle.pHandZone) return CardZoneType.プレイヤー手札;
        if (zone == battle.pBattlerZone) return CardZoneType.プレイヤーBゾーン;
        if (zone == battle.pTacticsZone) return CardZoneType.プレイヤーTゾーン;
        if (zone == battle.pFieldZone) return CardZoneType.プレイヤーFゾーン;
        if (zone == battle.pGraveZone) return CardZoneType.プレイヤー墓地;
        if (zone == battle.pDimensionZone) return CardZoneType.プレイヤー異次元;
        if (zone == battle.pDeckZone) return CardZoneType.プレイヤーデッキ;
        if (zone == battle.eHandZone) return CardZoneType.相手手札;
        if (zone == battle.eBattlerZone) return CardZoneType.相手Bゾーン;
        if (zone == battle.eTacticsZone) return CardZoneType.相手Tゾーン;
        if (zone == battle.eFieldZone) return CardZoneType.相手Fゾーン;
        if (zone == battle.eGraveZone) return CardZoneType.相手墓地;
        if (zone == battle.eDimensionZone) return CardZoneType.相手異次元;
        if (zone == battle.eDeckZone) return CardZoneType.相手デッキ;

        return CardZoneType.None;
    }

    /// <summary>
    /// 召喚元を、ローカルプレイヤー基準のゾーンにする。
    /// SummonSource の墓地とデッキは名前と逆で、墓地指定はデッキ、デッキ指定は墓地を指す。
    /// 場はゾーンが複数あるので、呼び出し側が移動前の親から取る。
    /// </summary>
    public static CardZoneType ZoneFromSummonSource(SummonSource source, bool isPlayerSide)
    {
        switch (source)
        {
            case SummonSource.手札:
                return isPlayerSide ? CardZoneType.プレイヤー手札 : CardZoneType.相手手札;
            case SummonSource.墓地:
                return isPlayerSide ? CardZoneType.プレイヤーデッキ : CardZoneType.相手デッキ;
            case SummonSource.デッキ:
                return isPlayerSide ? CardZoneType.プレイヤー墓地 : CardZoneType.相手墓地;
            default:
                return CardZoneType.None;
        }
    }

    /// <summary>
    /// ローカルプレイヤー基準のゾーンを、スキル所持カードから見たCardZoneTypeに変換する。
    /// isMyCardがfalseのときはプレイヤーと相手を入れ替える。
    /// </summary>
    public static CardZoneType ToOwnerPerspective(CardZoneType absoluteZone, bool isMyCard)
    {
        if (isMyCard || absoluteZone == CardZoneType.None)
            return absoluteZone;

        return absoluteZone switch
        {
            CardZoneType.プレイヤー手札 => CardZoneType.相手手札,
            CardZoneType.プレイヤーBゾーン => CardZoneType.相手Bゾーン,
            CardZoneType.プレイヤーTゾーン => CardZoneType.相手Tゾーン,
            CardZoneType.プレイヤーFゾーン => CardZoneType.相手Fゾーン,
            CardZoneType.プレイヤー墓地 => CardZoneType.相手墓地,
            CardZoneType.プレイヤーデッキ => CardZoneType.相手デッキ,
            CardZoneType.プレイヤー異次元 => CardZoneType.相手異次元,
            CardZoneType.相手手札 => CardZoneType.プレイヤー手札,
            CardZoneType.相手Bゾーン => CardZoneType.プレイヤーBゾーン,
            CardZoneType.相手Tゾーン => CardZoneType.プレイヤーTゾーン,
            CardZoneType.相手Fゾーン => CardZoneType.プレイヤーFゾーン,
            CardZoneType.相手墓地 => CardZoneType.プレイヤー墓地,
            CardZoneType.相手デッキ => CardZoneType.プレイヤーデッキ,
            CardZoneType.相手異次元 => CardZoneType.プレイヤー異次元,
            _ => absoluteZone
        };
    }

    /// <summary>
    /// スキルの「どこから」が、実際に送られた元ゾーンと一致するか。
    /// 未指定、または None だけのときはどこからでも一致する。複数あるときはどれか一つと一致すればよい。
    /// </summary>
    public static bool MatchesFromZone(List<CardZoneType> specified, CardZoneType absoluteFrom, bool ownerIsMyCard)
    {
        if (specified == null || specified.Count == 0)
            return true;

        bool hasConcreteZone = false;
        var actual = ToOwnerPerspective(absoluteFrom, ownerIsMyCard);
        foreach (var zone in specified)
        {
            if (zone == CardZoneType.None)
                continue;
            hasConcreteZone = true;
            if (zone == actual)
                return true;
        }

        return !hasConcreteZone;
    }

    /// <summary>
    /// 属性無効化効果を追加
    /// </summary>
    public void AddInvalidPassiveEffect(List<ElementList> elements)
    {
        foreach (var element in elements)
        {
            if (!invalidElements.Contains(element))
                invalidElements.Add(element);
        }
    }

    /// <summary>
    /// 指定された属性のダメージを無効化するかチェック
    /// </summary>
    public bool ShouldInvalidateDamage(ElementList attackerElement)
    {
        return invalidElements.Contains(attackerElement);
    }

    /// <summary>
    /// 属性無効化効果をクリア
    /// </summary>
    public void ClearInvalidElements()
    {
        invalidElements.Clear();
    }

    private Coroutine passiveDisplayCoroutine;

    private void UpdatePassiveEffectUI()
    {
        Transform panel = transform.Find("PassivePanel");
        if (panel != null)
        {
            // passiveEffects が空の場合パネルを非表示にする
            if (passiveEffects == null || passiveEffects.Count == 0)
            {
                panel.gameObject.SetActive(false);
                // コルーチンを停止
                if (passiveDisplayCoroutine != null)
                {
                    StopCoroutine(passiveDisplayCoroutine);
                    passiveDisplayCoroutine = null;
                }
                return;
            }

            // パッシブ効果がある場合はパネルを表示
            panel.gameObject.SetActive(true);

            // 複数のパッシブ効果がある場合はコルーチンで交互表示
            if (passiveEffects.Count > 1)
            {
                // 既存のコルーチンを停止
                if (passiveDisplayCoroutine != null)
                {
                    StopCoroutine(passiveDisplayCoroutine);
                }
                // 新しいコルーチンを開始
                passiveDisplayCoroutine = StartCoroutine(UpdatePassiveDisplayCoroutine());
            }
            else
            {
                // 1つのパッシブ効果のみの場合は従来通り
                var textComponent = panel.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (textComponent != null)
                {
                    var effect = passiveEffects[0];
                    textComponent.text = effect.RemainingTurns == -1 ? "∞" : effect.RemainingTurns.ToString();
                }
                // コルーチンを停止
                if (passiveDisplayCoroutine != null)
                {
                    StopCoroutine(passiveDisplayCoroutine);
                    passiveDisplayCoroutine = null;
                }
            }
        }
    }

    private IEnumerator UpdatePassiveDisplayCoroutine()
    {
        Transform panel = transform.Find("PassivePanel");
        var textComponent = panel?.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        
        if (textComponent == null) yield break;

        int currentIndex = 0;
        
        while (passiveEffects != null && passiveEffects.Count > 1)
        {
            // 現在の効果を表示
            var currentEffect = passiveEffects[currentIndex];
            textComponent.text = currentEffect.RemainingTurns == -1 ? "∞" : currentEffect.RemainingTurns.ToString();
            
            // 1秒待機
            yield return new WaitForSeconds(1f);
            
            // 次の効果に移動
            currentIndex = (currentIndex + 1) % passiveEffects.Count;
        }
        
        passiveDisplayCoroutine = null;
    }

    /// <summary>
    /// 属性によるダメージ修正を計算する
    /// </summary>
    /// <param name="attackerElement">攻撃技の属性</param>
    /// <param name="targetBattler">対象バトラーのモデル</param>
    /// <param name="moveIndex">使用している技のインデックス（0=技1, 1=技2）</param>
    /// <returns>ダメージ修正値（正の値でダメージ増加、負の値でダメージ減少）</returns>
    public int CalculateElementDamageModification(ElementList attackerElement, BattlerModel targetBattler, int moveIndex)
    {
        if (targetBattler == null) return 0;

        // 得意属性の場合、属性軽減値分ダメージを下げる
        // ただし、属性耐性無視が有効な場合は軽減を無視
        if (targetBattler.得意属性 != ElementList.None && attackerElement == targetBattler.得意属性)
        {
            // 使用している技に対する耐性無視設定を取得
            bool isIgnoreEnabled = moveIndex == 0 ? ignoreResistance1 : ignoreResistance2;
            ElementList targetElement = moveIndex == 0 ? ignoreResistanceTargetElement1 : ignoreResistanceTargetElement2;
            
            // 耐性無視が有効な場合
            if (isIgnoreEnabled)
            {
                var battler = model as BattlerModel;
                if (battler != null)
                {
                    // 実際に使用している技の属性を取得
                    ElementList actualMoveElement = moveIndex == 0 ? battler.moveElement1 : battler.moveElement2;
                    
                    // 対象属性がNone（全属性）または実際の技の属性と一致する場合、耐性無視
                    if (targetElement == ElementList.None || targetElement == actualMoveElement)
                    {
                        Debug.Log($"{cardName} は属性耐性を無視して攻撃！(Move{moveIndex + 1}, 技属性:{actualMoveElement}, 対象属性:{targetElement})");
                        return 0;
                    }
                }
            }
            
            return -targetBattler.属性軽減値;
        }

        // 弱点属性の場合、属性増加値分ダメージを上げる
        if (targetBattler.弱点属性 != ElementList.None && attackerElement == targetBattler.弱点属性)
        {
            return targetBattler.属性増加値;
        }

        return 0;
    }

    /// <summary>
    /// バトラーカードに書かれている、補正前の技パワー。
    /// </summary>
    public int GetOriginalMovePower(int moveIndex)
    {
        if (model is BattlerModel battler)
            return Mathf.Max(0, moveIndex == 0 ? battler.power1 : battler.power2);
        return 0;
    }

    /// <summary>
    /// ダメージエフェクトの色を、元パワーと最終ダメージの差で決める。
    /// パワー修正に加え、カテゴリアップなど最終ダメージだけを変える効果も含む。
    /// </summary>
    public Color GetDamageEffectColor(int moveIndex, int finalDamage)
    {
        int originalDamage = GetOriginalMovePower(moveIndex);
        if (finalDamage > originalDamage)
        {
            // 元パワーより大きい（カテゴリアップ、弱点属性など）。ステータス上昇と同じ緑
            return new Color(0.45f, 0.9f, 0.45f);
        }
        if (finalDamage < originalDamage)
        {
            // 元パワーより小さい（得意属性、ダメージ軽減など）- 赤色
            return new Color(0.902f, 0.082f, 0.243f); // #e6153e
        }

        return Color.yellow;
    }

    public bool IsActionBlocked => HasStatus(StatusAilmentType.気絶);

    /// <summary>
    /// 鈍化。起動効果と、技に付いた追加効果を止める。
    /// </summary>
    public bool IsSlowed => HasStatus(StatusAilmentType.鈍化);

    public bool HasStatus(StatusAilmentType type)
    {
        return statusAilments != null && statusAilments.Exists(ailment => ailment != null && ailment.Type == type);
    }

    public StatusAilment GetStatus(StatusAilmentType type)
    {
        if (statusAilments == null) return null;
        return statusAilments.Find(ailment => ailment != null && ailment.Type == type);
    }

    public const int DarknessHitRate = 50;

    /// <summary>
    /// 暗闇なら命中率を 50 にする。それ以外は必中（0）に戻す。
    /// </summary>
    public void RefreshHitRate()
    {
        hitRate = HasStatus(StatusAilmentType.暗闇) ? DarknessHitRate : 0;
    }

    /// <summary>
    /// 技が命中するか。hitRate が 0 または 100 以上なら必中。
    /// </summary>
    public bool RollMoveHit()
    {
        if (hitRate <= 0 || hitRate >= 100)
            return true;
        return UnityEngine.Random.Range(0, 100) < hitRate;
    }

    /// <summary>
    /// 召喚に払うコスト。印刷コストに、場の永続効果で付いた補正を足す。0未満にはしない。
    /// </summary>
    public int GetModifiedCost()
    {
        int adjustment = 0;
        if (BattleManager.Instance != null)
            adjustment = BattleManager.Instance.GetPlayCostAdjustment(isPlayerCard, cardKind);
        return Mathf.Max(0, cost + bonusCost + adjustment);
    }

    // 「凍結」のエネルギー増加
    public int GetMoveEnergyCost(int moveIndex)
    {
        int requiredEnergy = moveIndex == 0 ? modifiedEnergy1 : modifiedEnergy2;
        if (HasStatus(StatusAilmentType.凍結))
            requiredEnergy += 1;
        return Mathf.Max(0, requiredEnergy);
    }

    // 「火傷」のダメージ低下。相手の技使用時に登録したダメージ無効化も、ここでその技のダメージへかける。
    public int ModifyOutgoingMoveDamage(int damage)
    {
        if (BattleManager.Instance != null)
            damage = BattleManager.Instance.ApplyMoveDamageNullification(this, damage);
        damage = Mathf.Max(0, damage);
        if (damage == 0 || !HasStatus(StatusAilmentType.火傷))
            return damage;
        return damage - 3;
    }

    /// <summary>
    /// 「毒」のダメージ適用。エンドフェイズに 2。このダメージだけでは HP は 1 で止まる。
    /// </summary>
    public int ApplyPoisonDamage()
    {
        if (!HasStatus(StatusAilmentType.毒) || cardKind != CardKind.バトラー)
            return 0;
        if (curHp <= 1)
            return 0;

        int damage = Mathf.Min(2, curHp - 1);
        curHp -= damage;
        return damage;
    }

    /// <summary>
    /// 付与済みの状態異常を、効果ごと外す。新しい状態異常を付ける直前に使う。
    /// </summary>
    public void RemoveGrantedStatusAilments()
    {
        RemovePassiveWhere(skill => skill is ApplyStatusAilmentSkill);
        if (statusAilments == null || statusAilments.Count == 0)
            return;

        statusAilments.Clear();
        RefreshHitRate();
        UpdateDisplay();
    }

    public void ApplyStatus(StatusAilmentType type, int turns, string sourceName, bool syncOnline = true)
    {
        if (type == StatusAilmentType.None)
            return;

        if (statusAilments == null)
            statusAilments = new List<StatusAilment>();

        if (StatusAilment.ClearsWhenDamaged(type))
            turns = -1;

        // 状態異常は同時に1つ。残っているものは外してから、今の1つだけにする。
        statusAilments.Clear();
        statusAilments.Add(new StatusAilment(type, turns, sourceName));
        RefreshHitRate();
        UpdateDisplay();

        if (!syncOnline || BattleManager.Instance == null || !BattleManager.Instance.IsOnlineMode)
            return;
        if (OnlineSyncManager.Instance == null)
            return;
        if (!CardAddress.TryFrom(this, out CardAddress address))
            return;

        OnlineSyncManager.Instance.SyncApplyStatus(address.Kind.ToString(), address.Id, address.ZoneName, address.Index, (int)type, turns, sourceName);
    }

    public void RemoveStatus(StatusAilmentType type)
    {
        if (statusAilments == null) return;
        int removed = statusAilments.RemoveAll(ailment => ailment != null && ailment.Type == type);
        if (removed > 0)
        {
            RefreshHitRate();
            UpdateDisplay();
        }
    }

    public void ClearStatusAilments(bool keepStun = false)
    {
        if (statusAilments == null || statusAilments.Count == 0) return;

        int before = statusAilments.Count;
        if (keepStun)
            statusAilments.RemoveAll(ailment => ailment == null || !StatusAilment.ClearsWhenDamaged(ailment.Type));
        else
            statusAilments.Clear();

        if (statusAilments.Count != before)
        {
            RefreshHitRate();
            UpdateDisplay();
        }
    }

    public void TickStatusAilments()
    {
        if (statusAilments == null || statusAilments.Count == 0) return;

        bool removed = false;
        for (int i = statusAilments.Count - 1; i >= 0; i--)
        {
            var ailment = statusAilments[i];
            if (ailment == null || ailment.RemainingTurns <= 0)
                continue;

            ailment.RemainingTurns--;
            if (ailment.RemainingTurns <= 0)
            {
                statusAilments.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            RefreshHitRate();
            UpdateDisplay();
        }
    }

    /// <summary>
    /// ダメージを受け終わったあと、気絶と混乱を解く。
    /// 技の途中では呼ばず、技撃破時スキルが返ったあとに呼ぶ。
    /// </summary>
    public void ResolveStunAfterReceivingMove()
    {
        if (cardKind != CardKind.バトラー) return;
        RemoveStatus(StatusAilmentType.気絶);
        RemoveStatus(StatusAilmentType.混乱);
        if (passiveEffects == null) return;

        int before = passiveEffects.Count;
        passiveEffects.RemoveAll(effect =>
            effect?.PassiveEffect is ApplyStatusAilmentSkill skill && StatusAilment.ClearsWhenDamaged(skill.状態異常));
        if (passiveEffects.Count != before)
            UpdatePassiveEffectUI();
    }

    /// <summary>
    /// 選んだ種類だけを外す。カード自身の永続効果は消さない。
    /// 付与スキルを外すときは、その付与が書いていた状態異常・パワー・HP保護・属性無効も外す。
    /// </summary>
    public void ApplyCleanse(CleanseCategory categories, List<StatusAilmentType> statusFilter = null)
    {
        if (categories == CleanseCategory.なし)
            return;

        bool allGrants = (categories & CleanseCategory.付与スキル) != 0;

        if ((categories & CleanseCategory.技使用済み) != 0)
            hasUsedMove = false;

        if (allGrants)
        {
            var grantedStatuses = CollectGrantedStatusTypes();
            bool grantedProtection = HasGrantedSkill<HPProtectionSkill>();
            bool grantedInvalid = HasGrantedSkill<InvalidPassiveSkill>();
            RemoveAllPassiveEffects();
            foreach (var type in grantedStatuses)
                RemoveStatus(type);
            if (grantedProtection)
                ClearHpProtection();
            if (grantedInvalid)
                ClearInvalidElements();
        }
        else
        {
            if ((categories & CleanseCategory.状態異常) != 0)
                CleanseStatuses(statusFilter);

            if ((categories & CleanseCategory.属性無効) != 0)
            {
                RemovePassiveWhere(skill => skill is InvalidPassiveSkill);
                ClearInvalidElements();
            }

            if ((categories & CleanseCategory.HP保護) != 0)
            {
                RemovePassiveWhere(skill => skill is HPProtectionSkill);
                ClearHpProtection();
            }

            if ((categories & CleanseCategory.パワー修正) != 0)
            {
                damageBonus = 0;
                ResetBonuses();
                RecalculateModifiedPowers();
            }
        }

        if (allGrants && (categories & CleanseCategory.状態異常) != 0)
            CleanseStatuses(statusFilter);

        if ((categories & CleanseCategory.ターン制限) != 0)
            RemoveAllTurnRestrictions();

        RefreshHitRate();
        UpdateDisplay();
        if (allGrants)
            _ = BattleManager.Instance?.RetireEnhancementsIfSpent(this);
    }

    public static bool CleanseNeedsRecalc(CleanseCategory categories)
    {
        return (categories & (
            CleanseCategory.付与スキル
            | CleanseCategory.状態異常
            | CleanseCategory.属性無効
            | CleanseCategory.HP保護
            | CleanseCategory.パワー修正)) != 0;
    }

    void CleanseStatuses(List<StatusAilmentType> filter)
    {
        bool all = filter == null || filter.Count == 0;
        RemovePassiveWhere(skill =>
            skill is ApplyStatusAilmentSkill statusSkill
            && (all || filter.Contains(statusSkill.状態異常)));

        if (all)
            ClearStatusAilments();
        else
        {
            foreach (var type in filter)
                RemoveStatus(type);
        }
    }

    List<StatusAilmentType> CollectGrantedStatusTypes()
    {
        var types = new List<StatusAilmentType>();
        if (passiveEffects == null)
            return types;

        foreach (var effect in passiveEffects)
        {
            if (effect?.PassiveEffect is ApplyStatusAilmentSkill skill && !types.Contains(skill.状態異常))
                types.Add(skill.状態異常);
        }
        return types;
    }

    bool HasGrantedSkill<T>() where T : SkillDB
    {
        if (passiveEffects == null)
            return false;

        foreach (var effect in passiveEffects)
        {
            if (effect?.PassiveEffect is T)
                return true;
        }
        return false;
    }

    void RemovePassiveWhere(System.Func<SkillDB, bool> match)
    {
        if (passiveEffects == null || match == null)
            return;

        int before = passiveEffects.Count;
        passiveEffects.RemoveAll(effect =>
        {
            var skill = effect?.PassiveEffect;
            return skill != null && match(skill);
        });
        if (passiveEffects.Count != before)
            UpdatePassiveEffectUI();
    }

    void ClearHpProtection()
    {
        hpProtection1 = false;
        hpProtectionTargetElement1 = ElementList.None;
        hpProtection2 = false;
        hpProtectionTargetElement2 = ElementList.None;
    }
}
