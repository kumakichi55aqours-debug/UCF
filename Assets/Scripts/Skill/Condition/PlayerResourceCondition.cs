using System;
using UnityEngine;

/// <summary>
/// プレイヤーのリソース（コスト、エネルギー、HP）をチェックする条件
/// </summary>
[Serializable]
public class PlayerResourceCondition : ISkillCondition
{
    /// <summary>
    /// チェック対象のプレイヤー
    /// </summary>
    [SerializeField] private PlayerType 対象プレイヤー = PlayerType.プレイヤー;
    
    /// <summary>
    /// チェックするリソースの種類
    /// </summary>
    [SerializeField] private ResourceType リソースタイプ = ResourceType.HP;
    
    /// <summary>
    /// 比較タイプ（以上/以下）
    /// </summary>
    [SerializeField] private MatchType 比較タイプ = MatchType.以上;
    
    /// <summary>
    /// 基準値
    /// </summary>
    [SerializeField] private int 基準値 = 0;

    public PlayerResourceCondition()
    {
        対象プレイヤー = PlayerType.プレイヤー;
        リソースタイプ = ResourceType.HP;
        比較タイプ = MatchType.以上;
        基準値 = 0;
    }

    public PlayerResourceCondition(PlayerType target, ResourceType resourceType, MatchType comparisonType, int threshold)
    {
        対象プレイヤー = target;
        リソースタイプ = resourceType;
        比較タイプ = comparisonType;
        基準値 = threshold;
    }

    public bool IsConditionMet(CardController card)
    {
        return IsConditionMetInternal();
    }

    public bool IsConditionMet(SkillContext context)
    {
        return IsConditionMetInternal();
    }

    private bool IsConditionMetInternal()
    {
        if (BattleManager.Instance == null)
        {
            Debug.LogWarning("[PlayerResourceCondition] BattleManager.Instance is null");
            return false;
        }

        bool checkPlayer = 対象プレイヤー == PlayerType.プレイヤー || 対象プレイヤー == PlayerType.両方;
        bool checkEnemy = 対象プレイヤー == PlayerType.相手 || 対象プレイヤー == PlayerType.両方;

        // どちらもチェックする場合、両方が条件を満たす必要がある
        if (対象プレイヤー == PlayerType.両方)
        {
            bool playerResult = checkPlayer && CheckResource(BattleManager.Instance.pResources);
            bool enemyResult = checkEnemy && CheckResource(BattleManager.Instance.eResources);
            return playerResult && enemyResult;
        }
        // どちらか一方をチェックする場合
        else if (checkPlayer)
        {
            return CheckResource(BattleManager.Instance.pResources);
        }
        else if (checkEnemy)
        {
            return CheckResource(BattleManager.Instance.eResources);
        }

        return false;
    }

    private bool CheckResource(ResourceGroup resources)
    {
        if (resources == null)
        {
            Debug.LogWarning("[PlayerResourceCondition] ResourceGroup is null");
            return false;
        }

        int currentValue = 0;
        ResourceSystem targetResource = null;

        switch (リソースタイプ)
        {
            case ResourceType.HP:
                targetResource = resources.Hp;
                if (targetResource != null)
                {
                    currentValue = targetResource.currentHp;
                }
                break;
            case ResourceType.コスト:
                targetResource = resources.Cost;
                if (targetResource != null)
                {
                    currentValue = targetResource.currentCost;
                }
                break;
            case ResourceType.エネルギー:
                targetResource = resources.Energy;
                if (targetResource != null)
                {
                    currentValue = targetResource.currentEnergy;
                }
                break;
        }

        if (targetResource == null)
        {
            Debug.LogWarning($"[PlayerResourceCondition] ResourceSystem ({リソースタイプ}) is null");
            return false;
        }

        return 比較タイプ.Matches(currentValue, 基準値);
    }
}
