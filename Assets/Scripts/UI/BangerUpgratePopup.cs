using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BangerUpgratePopup : MonoBehaviour
{
    [SerializeField] private TMP_Text explosiveLevelText;
    [SerializeField] private Image explosiveImage;
    [SerializeField] private Button upgrateExplosiveButton;
    [SerializeField] private TMP_Text criticalStatus;
    [SerializeField] private Button criticalButton;
    [SerializeField] private TMP_Text evasionStatus;
    [SerializeField] private Button evasionButton;

    public void SetupUI(BoatData boatData, string instanceId)
    {
        if (boatData == null)
        {
            Debug.LogError("BoatData is null. Cannot setup UI.");
            return;
        }
        // Setup explosive upgrade UI
        var explosiveUpgrade = boatData.GetUpgrade(StatType.Explosive);
        if (explosiveUpgrade != null)
        {
            int explosiveLevel = GameData.GetUpgradeLevel(instanceId, StatType.Explosive);
            explosiveLevelText.text = $"Level: {explosiveLevel}";
            BindUpgrade(upgrateExplosiveButton, boatData, instanceId, StatType.Explosive);
            upgrateExplosiveButton.interactable = explosiveLevel < explosiveUpgrade.maxLevel;
        }
        // Setup critical upgrade UI
        var criticalUpgrade = boatData.GetUpgrade(StatType.Critical);
        if (criticalUpgrade != null)
        {
            int criticalLevel = GameData.GetUpgradeLevel(instanceId, StatType.Critical);
            float critChance = GameData.GetCritChance(boatData, instanceId);
            criticalStatus.text = $"{critChance:0.#}%";   // % thực tế, không phải cấp
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
