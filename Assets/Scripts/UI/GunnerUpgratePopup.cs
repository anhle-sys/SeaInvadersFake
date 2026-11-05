using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GunnerUpgratePopup : MonoBehaviour
{
    [SerializeField] private TMP_Text weaponLevelText;
    [SerializeField] private Image weaponImage;
    [SerializeField] private Button upgrateWeaponButton;
    [SerializeField] private TMP_Text amorLevelText;
    [SerializeField] private Image amorImage;
    [SerializeField] private Button upgrateAmorButton;
    [SerializeField] private TMP_Text criticalStatus;
    [SerializeField] private Button criticalButton;
    [SerializeField] private TMP_Text evasionStatus;
    [SerializeField] private Button evasionButton;

    public void SetupUI(BoatData boatData)
    {
        if (boatData == null)
        {
            Debug.LogError("BoatData is null. Cannot setup UI.");
            return;
        }
        // Setup weapon upgrade UI
        var weaponUpgrade = boatData.GetUpgrade(StatType.Weapon);
        if (weaponUpgrade != null)
        {
            int weaponLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Weapon);
            weaponLevelText.text = $"Level: {weaponLevel}";
            upgrateWeaponButton.interactable = weaponLevel < weaponUpgrade.maxLevel;
        }
        // Setup armor upgrade UI
        var armorUpgrade = boatData.GetUpgrade(StatType.Armor);
        if (armorUpgrade != null)
        {
            int armorLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Armor);
            amorLevelText.text = $"Level: {armorLevel}";
            upgrateAmorButton.interactable = armorLevel < armorUpgrade.maxLevel;
        }
        // Setup critical upgrade UI
        var criticalUpgrade = boatData.GetUpgrade(StatType.Critical);
        if (criticalUpgrade != null)
        {
            int criticalLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Critical);
            criticalStatus.text = $"{criticalLevel}%";
            criticalButton.interactable = criticalLevel < criticalUpgrade.maxLevel;
        }
        // Setup evasion upgrade UI
        var evasionUpgrade = boatData.GetUpgrade(StatType.Evasion);
        if (evasionUpgrade != null)
        {
            int evasionLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Evasion);
            evasionStatus.text = $"{evasionLevel}%";
            evasionButton.interactable = evasionLevel < evasionUpgrade.maxLevel;
        }
    }
}
