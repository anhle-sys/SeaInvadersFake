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

    public void SetupUI(BoatData boatData)
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
            int explosiveLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Explosive);
            explosiveLevelText.text = $"Level: {explosiveLevel}";
            BindUpgrade(upgrateExplosiveButton, boatData, StatType.Explosive);
            upgrateExplosiveButton.interactable = explosiveLevel < explosiveUpgrade.maxLevel;
        }
        // Setup critical upgrade UI
        var criticalUpgrade = boatData.GetUpgrade(StatType.Critical);
        if (criticalUpgrade != null)
        {
            int criticalLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Critical);
            criticalStatus.text = $"{criticalLevel}%";
            BindUpgrade(criticalButton, boatData, StatType.Critical);
            criticalButton.interactable = criticalLevel < criticalUpgrade.maxLevel;
        }
        // Setup evasion upgrade UI
        var evasionUpgrade = boatData.GetUpgrade(StatType.Evasion);
        if (evasionUpgrade != null)
        {
            int evasionLevel = GameData.GetUpgradeLevel(boatData.id, StatType.Evasion);
            evasionStatus.text = $"{evasionLevel}%";
            BindUpgrade(evasionButton, boatData, StatType.Evasion);
            evasionButton.interactable = evasionLevel < evasionUpgrade.maxLevel;
        }
    }

    private void BindUpgrade(Button button, BoatData boatData, StatType stat)
    {
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            if (GameData.TryUpgrade(boatData, stat))
                SetupUI(boatData);
        });
    }
}
