using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

public class ResourceSystem : MonoBehaviour
{
    [SerializeField] public int maxHp;
    public int currentHp;
    [SerializeField] private TextMeshProUGUI hpText;

    [SerializeField] public int maxCost;
    public int currentCost;
    [SerializeField] private TextMeshProUGUI costText;

    [SerializeField] public int maxEnergy;
    public int currentEnergy;
    [SerializeField] private TextMeshProUGUI energyText;

    public event Action OnHpToZero; // HP 0 のときのイベント

    private void Start()
    {
        currentHp = maxHp;
        currentCost = maxCost;
        currentEnergy = maxEnergy;
        UpdateHPUI();
    }

    public void TakeDamage(int damage)
    {
        LoseHp(damage);

        if (currentHp <= 0)
        {
            OnHpToZero?.Invoke(); // HPが0になったときの処理を実行
        }
    }

    /// <summary>
    /// HPだけ減らし、0になっても敗北通知は出さない。複数ヒットの途中用。
    /// </summary>
    public void LoseHp(int damage)
    {
        if (damage <= 0)
            return;

        currentHp -= damage;
        if (currentHp < 0) currentHp = 0;
        UpdateHPUI();
    }

    /// <summary>
    /// 複数ヒットがすべて終わったあと、HPが0なら敗北を1回だけ通知する。
    /// </summary>
    public void NotifyHpDepleted()
    {
        if (currentHp <= 0)
            OnHpToZero?.Invoke();
    }

    public void TakeCost(int cost)
    {
        currentCost -= cost;
        if (currentCost < 0) currentCost = 0;
        UpdateCostUI();

        if (currentCost <= 0)
        {
            // Costが0になったときの処理
        }
    }

    public void TakeEnergy(int energy)
    {
        currentEnergy -= energy;
        if (currentEnergy < 0) currentEnergy = 0;
        UpdateEnergyUI();

        if (currentEnergy <= 0)
        {
            // Energyが0になったときの処理
        }
    }

    public void AddEnegry(int energy)
    {
        currentEnergy += energy;
        UpdateEnergyUI();
    }

    public void AddCost(int cost)
    {
        currentCost += cost;
        if (currentCost < 0) currentCost = 0;
        UpdateCostUI();
    }

    public void ResetCost(int cost)
    {
        currentCost = cost;
        UpdateCostUI();
    }

    public void ResetEnergy(int energy)
    {
        currentEnergy = energy;
        UpdateEnergyUI();
    }

    public void Heal(int amount)
    {
        currentHp += amount;
        // if (currentHP > maxHP) currentHP = maxHP;
        UpdateHPUI();
    }

    private void UpdateHPUI()
    {
        if (hpText != null)
            hpText.text = "" + currentHp;
    }

    private void UpdateCostUI()
    {
        if (costText != null)
        {
            costText.text = "" + currentCost;
        }
    }

    private void UpdateEnergyUI()
    {
        if (energyText != null)
        {
            energyText.text = "" + currentEnergy;
        }
    }
}

[System.Serializable]
public class ResourceGroup
{
    public ResourceSystem Hp;
    public ResourceSystem Cost;
    public ResourceSystem Energy;
}
