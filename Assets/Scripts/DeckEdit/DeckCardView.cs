using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckCardView : MonoBehaviour
{
    [Header("基本UI")]
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI costText;
    [SerializeField] Image artworkImage;
    [SerializeField] private Image typeImage;

    [Header("Battler用UI")]
    [SerializeField] private GameObject onlyBattler;
    [SerializeField] TextMeshProUGUI hpText;
    [SerializeField] private Image elementImage1;
    [SerializeField] private Image elementImage2;
    [SerializeField] private GameObject moves;

    [Header("Tactics用UI")]
    [SerializeField] private GameObject onlyTactics;

    [Header("Field用UI")]
    [SerializeField] private GameObject onlyField;

    CardController cc;

    // CardController への参照は通常 GameObject にあるので
    private CardController cardController;

    private void Start()
    {
        cc = GetComponent<CardController>();
    }

    public void Show(object model)
    {
        // 表示内容の更新
        if (model is BattlerModel battler)
        {
            nameText.text = battler.cardName;
            artworkImage.sprite = battler.artwork;
            costText.text = (battler.cost == 0) ? "-" : battler.cost.ToString();
            onlyBattler.SetActive(true);
            hpText.text = (cc != null) ? cc.curHp.ToString() : battler.maxHp.ToString();
            hpText.transform.parent.gameObject.SetActive(true);
            typeImage.sprite = CardIconManager.Instance.GetCardTypeSprite(battler.type);
            elementImage1.sprite = CardIconManager.Instance.GetElementSprite(battler.moveElement1);
            elementImage2.sprite = CardIconManager.Instance.GetElementSprite(battler.moveElement2);
            moves.SetActive(true);

            onlyTactics.SetActive(false);
            onlyField.SetActive(false);
        }
        else if (model is TacticsModel tactics)
        {
            nameText.text = tactics.cardName;
            artworkImage.sprite = tactics.artwork;
            costText.text = (tactics.cost == 0) ? "-" : tactics.cost.ToString();
            onlyTactics.SetActive(true);
            onlyBattler.SetActive(false);
            onlyField.SetActive(false);
            hpText.transform.parent.gameObject.SetActive(false);
            typeImage.sprite = CardIconManager.Instance.GetCardKindSprite(CardKind.作戦);
            moves.SetActive(false);
        }
        else if (model is FieldModel field)
        {
            nameText.text = field.cardName;
            artworkImage.sprite = field.artwork;
            costText.text = (field.cost == 0) ? "-" : field.cost.ToString();
            onlyField.SetActive(true);
            hpText.text = (cc != null) ? cc.curHp.ToString() : field.maxHp.ToString();
            hpText.transform.parent.gameObject.SetActive(true);
            typeImage.sprite = CardIconManager.Instance.GetCardKindSprite(CardKind.フィールド);
            moves.SetActive(false);
            onlyBattler.SetActive(false);
            onlyTactics.SetActive(false);
        }
    }
}
