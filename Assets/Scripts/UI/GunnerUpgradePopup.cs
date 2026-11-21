using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GunnerUpgradePopup : MonoBehaviour
{
    [Header("Weapon")]
    [SerializeField] private TMP_Text weaponLevelText;
    [SerializeField] private Image weaponProgressFill;
    [SerializeField] private TMP_Text weaponCostText;
    [SerializeField] private Button weaponUpgradeButton;

    [Header("Armor")]
    [SerializeField] private TMP_Text armorLevelText;
    [SerializeField] private Image armorProgressFill;
    [SerializeField] private TMP_Text armorCostText;
    [SerializeField] private Button armorUpgradeButton;

    [Header("Critical")]
    [SerializeField] private TMP_Text criticalValueText;
    [SerializeField] private TMP_Text criticalCostText;
    [SerializeField] private Button criticalUpgradeButton;

    [Header("Evasion")]
    [SerializeField] private TMP_Text evasionValueText;
    [SerializeField] private TMP_Text evasionCostText;
    [SerializeField] private Button evasionUpgradeButton;

    public void Refresh(BoatData data, string instanceId)
    {
        if (data == null)
        {
            Debug.LogError("GunnerUpgradePopup: BoatData is null.");
            return;
        }

        void Redraw() => Refresh(data, instanceId);

        UpgradeRow.RefreshTier(data, instanceId, StatType.Weapon,
            weaponLevelText, weaponProgressFill, weaponCostText, weaponUpgradeButton, Redraw);
        UpgradeRow.RefreshTier(data, instanceId, StatType.Armor,
            armorLevelText, armorProgressFill, armorCostText, armorUpgradeButton, Redraw);
        UpgradeRow.RefreshPercent(data, instanceId, StatType.Critical,
            GameData.GetCritChance(data, instanceId), GameData.CritChanceCap,
            criticalValueText, criticalCostText, criticalUpgradeButton, Redraw);
        UpgradeRow.RefreshPercent(data, instanceId, StatType.Evasion,
            GameData.GetEvasion(data, instanceId), GameData.EvasionCap,
            evasionValueText, evasionCostText, evasionUpgradeButton, Redraw);
    }
}
