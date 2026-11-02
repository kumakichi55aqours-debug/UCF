using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// エネルギーかコストの所持量を、ターン数のあいだ増減する。
/// プレイヤーは発動したカードの持ち主。相手はその反対。両方はそれぞれ。
/// HPは所持量をその場で増減する。減るときはダメージ、増えるときは回復。
/// </summary>
[Serializable]
[SkillDisplayName("リソース")]
public class PlayerResourceModifierSkill : SkillDB
{
    public ResourceType リソースタイプ = ResourceType.エネルギー;
    public int 効果数 = 1;
    public int 持続ターン数 = 1;
    public PlayerType 対象プレイヤー = PlayerType.プレイヤー;

    public override async Task ActivateEffect(SkillContext context)
    {
        var battle = BattleManager.Instance;
        if (battle == null)
            return;

        if (リソースタイプ == ResourceType.HP)
        {
            await ApplyHp(battle, context);
            return;
        }

        int duration = Mathf.Max(0, 持続ターン数);
        string sourceName = context != null && context.発動カード != null ? context.発動カード.cardName : "リソース";
        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;

        if (対象プレイヤー != PlayerType.相手)
            battle.ScheduleResourceBuff(ownerIsLocal, リソースタイプ, 効果数, duration, sourceName, true);
        if (対象プレイヤー != PlayerType.プレイヤー)
            battle.ScheduleResourceBuff(!ownerIsLocal, リソースタイプ, 効果数, duration, sourceName, true);
    }

    async Task ApplyHp(BattleManager battle, SkillContext context)
    {
        if (効果数 == 0)
            return;

        bool ownerIsLocal = context?.発動カード == null || context.発動カード.isMyCard;
        var changed = default(HpChange);

        if (対象プレイヤー != PlayerType.相手)
            changed = changed.Add(await ChangeHp(battle, ownerIsLocal));
        if (対象プレイヤー != PlayerType.プレイヤー)
            changed = changed.Add(await ChangeHp(battle, !ownerIsLocal));

        int damageToLocal = changed.DamageToLocal;
        int damageToEnemy = changed.DamageToEnemy;
        int healToLocal = changed.HealToLocal;
        int healToEnemy = changed.HealToEnemy;

        if (!battle.IsOnlineMode || OnlineSyncManager.Instance == null)
            return;

        if (damageToLocal > 0 || damageToEnemy > 0)
            OnlineSyncManager.Instance.SyncDirectHpDamage(damageToLocal, damageToEnemy);
        if (healToLocal > 0 || healToEnemy > 0)
            OnlineSyncManager.Instance.SyncDirectHpHeal(healToLocal, healToEnemy);
        if (damageToLocal > 0 || damageToEnemy > 0 || healToLocal > 0 || healToEnemy > 0)
            await OnlineSyncManager.Instance.WaitForOpponentAnimation();
    }

    async Task<HpChange> ChangeHp(BattleManager battle, bool forLocal)
    {
        var hp = forLocal ? battle.pResources?.Hp : battle.eResources?.Hp;
        if (hp == null)
            return default;

        Transform zone = forLocal ? battle.pFieldZone : battle.eFieldZone;
        if (効果数 < 0)
        {
            int loss = -効果数;
            hp.TakeDamage(loss);
            if (zone != null)
                await battle.ShowEffect(zone.position, loss, Color.yellow);
            return forLocal
                ? new HpChange { DamageToLocal = loss }
                : new HpChange { DamageToEnemy = loss };
        }

        hp.Heal(効果数);
        if (zone != null)
            await battle.ShowEffect(zone.position, 効果数, Color.green);
        return forLocal
            ? new HpChange { HealToLocal = 効果数 }
            : new HpChange { HealToEnemy = 効果数 };
    }

    struct HpChange
    {
        public int DamageToLocal;
        public int DamageToEnemy;
        public int HealToLocal;
        public int HealToEnemy;

        public HpChange Add(HpChange other)
        {
            return new HpChange
            {
                DamageToLocal = DamageToLocal + other.DamageToLocal,
                DamageToEnemy = DamageToEnemy + other.DamageToEnemy,
                HealToLocal = HealToLocal + other.HealToLocal,
                HealToEnemy = HealToEnemy + other.HealToEnemy
            };
        }
    }
}
