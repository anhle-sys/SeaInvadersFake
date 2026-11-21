using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BangerUpgradePopup : MonoBehaviour
{
    [Header("Explosive")]
    [SerializeField] private TMP_Text explosiveLevelText;
    [SerializeField] private Image explosiveProgressFill;
    [SerializeField] private TMP_Text explosiveCostText;
    [SerializeField] private Button explosiveUpgradeButton;

    [Header("Critical")]
    [SerializeField] private TMP_Text criticalValueText;
    [SerializeField] private Button criticalUpgradeButton;
    [SerializeField] private TMP_Text criticalCostText;

    [Header("Evasion")]
    [SerializeField] private TMP_Text evasionValueText;
    [SerializeField] private Button evasionUpgradeButton;
    [SerializeField] private TMP_Text evasionCostText;

    public void Refresh(BoatData data, string instanceId)
    {
        if (data == null)
        {
            Debug.LogError("BangerUpgradePopup: BoatData is null.");
            return;
        }

        void Redraw() => Refresh(data, instanceId);

        UpgradeRow.RefreshTier(data, instanceId, StatType.Explosive,
            explosiveLevelText, explosiveProgressFill, explosiveCostText, explosiveUpgradeButton, Redraw);
        UpgradeRow.RefreshPercent(data, instanceId, StatType.Critical,
            GameData.GetCritChance(data, instanceId), GameData.CritChanceCap,
            criticalValueText, criticalCostText, criticalUpgradeButton, Redraw);
        UpgradeRow.RefreshPercent(data, instanceId, StatType.Evasion,
            GameData.GetEvasion(data, instanceId), GameData.EvasionCap,
            evasionValueText, evasionCostText, evasionUpgradeButton, Redraw);
    }
}
