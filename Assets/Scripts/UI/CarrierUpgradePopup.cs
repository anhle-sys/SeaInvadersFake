using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CarrierUpgradePopup : MonoBehaviour
{
    [Header("Air")]
    [SerializeField] private TMP_Text airLevelText;
    [SerializeField] private Image airProgressFill;
    [SerializeField] private TMP_Text airCostText;
    [SerializeField] private Button airUpgradeButton;

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
            Debug.LogError("CarrierUpgradePopup: BoatData is null.");
            return;
        }

        void Redraw() => Refresh(data, instanceId);

        UpgradeRow.RefreshTier(data, instanceId, StatType.Air,
            airLevelText, airProgressFill, airCostText, airUpgradeButton, Redraw);
        UpgradeRow.RefreshPercent(data, instanceId, StatType.Critical,
            GameData.GetCritChance(data, instanceId), GameData.CritChanceCap,
            criticalValueText, criticalCostText, criticalUpgradeButton, Redraw);
        UpgradeRow.RefreshPercent(data, instanceId, StatType.Evasion,
            GameData.GetEvasion(data, instanceId), GameData.EvasionCap,
            evasionValueText, evasionCostText, evasionUpgradeButton, Redraw);
    }
}
