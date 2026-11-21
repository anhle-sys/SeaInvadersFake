using System;
using TMPro;
using UnityEngine.UI;

// Cập nhật 1 dòng nâng cấp trên popup, dùng chung cho Gunner / Banger / Carrier
public static class UpgradeRow
{
    // Chỉ số theo bậc (Weapon, Armor, Explosive, Air): "Level: N" + thanh tiến độ trong bậc
    public static void RefreshTier(BoatData data, string instanceId, StatType stat,
        TMP_Text levelText, Image progressFill, TMP_Text costText, Button upgradeButton, Action onUpgraded)
    {
        StatUpgrade upgrade = data.GetUpgrade(stat);
        if (upgrade == null)
            return;

        int level = GameData.GetUpgradeLevel(instanceId, stat);
        levelText.text = "Level: " + upgrade.GetTier(level);
        if (progressFill != null)
            progressFill.fillAmount = upgrade.GetTierFill(level);

        costText.text = GameData.GetUpgradeCost(data, instanceId, stat).ToString();
        BindUpgradeButton(upgradeButton, data, instanceId, stat, onUpgraded);
        upgradeButton.interactable = level < upgrade.maxLevel;
    }

    // Chỉ số theo % (Critical, Evasion): hiện % thực tế, khóa nút khi chạm trần
    public static void RefreshPercent(BoatData data, string instanceId, StatType stat, float percent, float percentCap,
        TMP_Text valueText, TMP_Text costText, Button upgradeButton, Action onUpgraded)
    {
        StatUpgrade upgrade = data.GetUpgrade(stat);
        if (upgrade == null)
            return;

        int level = GameData.GetUpgradeLevel(instanceId, stat);
        valueText.text = $"{percent:0.#}%";
        costText.text = GameData.GetUpgradeCost(data, instanceId, stat).ToString();
        BindUpgradeButton(upgradeButton, data, instanceId, stat, onUpgraded);
        upgradeButton.interactable = level < upgrade.maxLevel && percent < percentCap;
    }

    private static void BindUpgradeButton(Button button, BoatData data, string instanceId, StatType stat, Action onUpgraded)
    {
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            if (GameData.TryUpgrade(data, instanceId, stat))
                onUpgraded?.Invoke();
        });
    }
}
