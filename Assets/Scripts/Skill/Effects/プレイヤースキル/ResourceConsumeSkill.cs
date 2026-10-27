using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
[SkillDisplayName("リソース消費選択")]
public class ResourceConsumeSkill : SkillDB
{
    [SerializeField] public ResourceType リソースタイプ = ResourceType.エネルギー;

    // 0以下なら所持数まで。1以上なら、所持数とこの値の小さい方まで
    [SerializeField] public int 最大消費数 = 0;

    // コンテキストへ書き込むキー名（デフォルトはドロー数に使う）
    [SerializeField] public string 出力キー = "DrawCount";

    public override async Task ActivateEffect(SkillContext context)
    {
        // 実行者がプレイヤーかどうかで参照先を分岐
        bool isPlayer = context.発動カード != null && context.発動カード.isMyCard;
        var resources = isPlayer ? BattleManager.Instance.pResources : BattleManager.Instance.eResources;

        int current = GetCurrentAmount(resources);
        if (current <= 0)
        {
            context.IntVariables[出力キー] = 0;
            return;
        }

        string label = GetResourceLabel();
        int selectableMax = current;
        if (最大消費数 > 0)
            selectableMax = Mathf.Min(current, 最大消費数);

        // 数値選択UIを表示（0〜選択上限）
        int chosen = await NumberSelectionUI.Instance.ShowAsync(
            title: $"消費{label}を選択して下さい",
            min: 0,
            max: selectableMax,
            defaultValue: selectableMax
        );

        // 何も選ばれなかった場合のフォールバック
        if (chosen < 0) chosen = 0;
        if (chosen > selectableMax) chosen = selectableMax;

        Consume(resources, chosen);

        if (isPlayer)
        {
            SyncConsumption(chosen);
        }

        // コンテキストに選択値を書き込む
        context.IntVariables[出力キー] = chosen;

        await Task.CompletedTask;
    }

    int GetCurrentAmount(ResourceGroup resources)
    {
        if (resources == null) return 0;

        switch (リソースタイプ)
        {
            case ResourceType.コスト:
                return resources.Cost != null ? resources.Cost.currentCost : 0;
            case ResourceType.HP:
                return resources.Hp != null ? resources.Hp.currentHp : 0;
            default:
                return resources.Energy != null ? resources.Energy.currentEnergy : 0;
        }
    }

    void Consume(ResourceGroup resources, int amount)
    {
        if (resources == null || amount <= 0) return;

        switch (リソースタイプ)
        {
            case ResourceType.コスト:
                resources.Cost.TakeCost(amount);
                break;
            case ResourceType.HP:
                resources.Hp.TakeDamage(amount);
                break;
            default:
                resources.Energy.TakeEnergy(amount);
                break;
        }
    }

    void SyncConsumption(int amount)
    {
        if (amount <= 0) return;
        if (OnlineSyncManager.Instance == null || !OnlineSyncManager.Instance.IsOnlineMode) return;

        switch (リソースタイプ)
        {
            case ResourceType.コスト:
                OnlineSyncManager.Instance.SyncOpponentCostConsumption(amount);
                break;
            case ResourceType.エネルギー:
                OnlineSyncManager.Instance.SyncOpponentEnergyChange(-amount);
                break;
        }
    }

    string GetResourceLabel()
    {
        switch (リソースタイプ)
        {
            case ResourceType.コスト:
                return "コスト";
            case ResourceType.HP:
                return "HP";
            default:
                return "エネルギー";
        }
    }
}
