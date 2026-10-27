using TMPro;
using UnityEngine;

public class GrantedSkillRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sourceNameText;
    [SerializeField] private TextMeshProUGUI remainingTurnsText;

    public void Set(string sourceName, string remainingTurns)
    {
        if (sourceNameText != null)
            sourceNameText.text = sourceName;
        if (remainingTurnsText != null)
            remainingTurnsText.text = remainingTurns;
    }
}
