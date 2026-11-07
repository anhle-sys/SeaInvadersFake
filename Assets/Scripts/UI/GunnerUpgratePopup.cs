using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GunnerUpgratePopup : MonoBehaviour
{
    [Header("Weapon Upgrade UI")]
    [SerializeField] private TMP_Text weaponLevelText;
    [SerializeField] private Image weaponImage;
    [SerializeField] private TMP_Text coinCostWeapon;
    [SerializeField] private Button upgrateWeaponButton;
    [Header("Armor Upgrade UI")]
    [SerializeField] private TMP_Text amorLevelText;
    [SerializeField] private Image amorImage;
    [SerializeField] private TMP_Text coinCostAmor;
    [SerializeField] private Button upgrateAmorButton;
    [Header("Critical Upgrades UI")]
    [SerializeField] private TMP_Text criticalStatus;
    [SerializeField] private TMP_Text coinCostCritical;
    [SerializeField] private Button criticalButton;
    [Header("Evasion Upgrades UI")]
    [SerializeField] private TMP_Text evasionStatus;
    [SerializeField] private TMP_Text coinCostEvasion;
    [SerializeField] private Button evasionButton;

    public void SetupUI(BoatData boatData, string instanceId)
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
            int weaponLevel = GameData.GetUpgradeLevel(instanceId, StatType.Weapon);
            weaponLevelText.text = $"Level: {weaponLevel}";
            coinCostWeapon.text = $"{GameData.GetUpgradeCost(boatData, instanceId, StatType.Weapon)}";
            BindUpgrade(upgrateWeaponButton, boatData, instanceId, StatType.Weapon);
            upgrateWeaponButton.interactable = weaponLevel < weaponUpgrade.maxLevel;
        }
        // Setup armor upgrade UI
        var armorUpgrade = boatData.GetUpgrade(StatType.Armor);
        if (armorUpgrade != null)
        {
            int armorLevel = GameData.GetUpgradeLevel(instanceId, StatType.Armor);
            amorLevelText.text = $"Level: {armorLevel}";
            coinCostAmor.text = $"{GameData.GetUpgradeCost(boatData, instanceId, StatType.Armor)}";
            BindUpgrade(upgrateAmorButton, boatData, instanceId, StatType.Armor);
            upgrateAmorButton.interactable = armorLevel < armorUpgrade.maxLevel;
        }
        // Setup critical upgrade UI
        var criticalUpgrade = boatData.GetUpgrade(StatType.Critical);
        if (criticalUpgrade != null)
        {
            int criticalLevel = GameData.GetUpgradeLevel(instanceId, StatType.Critical);
            float critChance = GameData.GetCritChance(boatData, instanceId);
            criticalStatus.text = $"{critChance:0.#}%";   // % thực tế, không phải cấp
            coinCostCritical.text = $"{GameData.GetUpgradeCost(boatData, instanceId, StatType.Critical)}";
            BindUpgrade(criticalButton, boatData, instanceId, StatType.Critical);
            criticalButton.interactable = criticalLevel < criticalUpgrade.maxLevel && critChance < GameData.CritChanceCap;
        }
        // Setup evasion upgrade UI
        var evasionUpgrade = boatData.GetUpgrade(StatType.Evasion);
        if (evasionUpgrade != null)
        {
            int evasionLevel = GameData.GetUpgradeLevel(instanceId, StatType.Evasion);
            float evasion = GameData.GetEvasion(boatData, instanceId);
            evasionStatus.text = $"{evasion:0.#}%";
            coinCostEvasion.text = $"{GameData.GetUpgradeCost(boatData, instanceId, StatType.Evasion)}";
            BindUpgrade(evasionButton, boatData, instanceId, StatType.Evasion);
            evasionButton.interactable = evasionLevel < evasionUpgrade.maxLevel && evasion < GameData.EvasionCap;
        }
    }

private void BindUpgrade(Button button, BoatData boatData, string instanceId, StatType stat)
    {
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            if (GameData.TryUpgrade(boatData, instanceId, stat))
                SetupUI(boatData, instanceId);
        });
    }
}
