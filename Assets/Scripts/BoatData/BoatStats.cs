using UnityEngine;

public class BoatStats
{
    public const float CritMultiplier = 2f;

    public int Level;
    public float MaxHealth;
    public float Damage;
    public float Armor;       // giảm sát thương phẳng
    public float CritChance;  // %
    public float Evasion;     // %

public static BoatStats Create(BoatData data, bool usePlayerProgress, string instanceId = null)
    {
        int level = usePlayerProgress ? GameData.GetBoatLevel(data.id) : 0;
        BoatLevelStats levelStats = data.GetLevel(level);

        var stats = new BoatStats
        {
            Level = level,
            MaxHealth = levelStats.maxHealth,
            Damage = levelStats.damage,
        };

        if (usePlayerProgress)
        {
            // Nâng cấp tính theo từng con tàu (instanceId), không theo loại tàu
            switch (data.boatType)
            {
                case BoatType.Banger:
                    stats.Damage += GameData.GetUpgradeBonus(data, instanceId, StatType.Explosive);
                    break;
                case BoatType.Gunner:
                    stats.Damage += GameData.GetUpgradeBonus(data, instanceId, StatType.Weapon);
                    break;
                case BoatType.Carrier:
                    stats.Damage += GameData.GetUpgradeBonus(data, instanceId, StatType.Air);
                    break;
            }
            stats.Armor = GameData.GetArmor(data, instanceId);
            stats.CritChance = GameData.GetCritChance(data, instanceId);
            stats.Evasion = GameData.GetEvasion(data, instanceId);
        }

        return stats;
    }

    public float RollDamage() =>
        Random.value * 100f < CritChance ? Damage * CritMultiplier : Damage;
}
