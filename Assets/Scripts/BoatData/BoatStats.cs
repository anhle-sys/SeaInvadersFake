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

    public static BoatStats Create(BoatData data, bool usePlayerProgress)
    {
        int level = usePlayerProgress ? GameData.GetBoatLevel(data.id) : 0;
        BoatLevelStats levelStats = data.GetLevel(level);

        var stats = new BoatStats
        {
            Level = level,
            MaxHealth = levelStats.maxHealth,
            Damage = data is BangerData banger ? banger.explosionDamage : levelStats.damage,
        };

        if (usePlayerProgress)
        {
            switch (data.boatType)
            {
                case BoatType.Banger:
                    stats.Damage += GameData.GetUpgradeBonus(data, StatType.Explosive);
                    break;
                case BoatType.Gunner:
                    stats.Damage += GameData.GetUpgradeBonus(data, StatType.Weapon);
                    break;
                case BoatType.Carrier:
                    stats.Damage += GameData.GetUpgradeBonus(data, StatType.Air);
                    break;
            }
            stats.Armor = GameData.GetArmor(data);
            stats.CritChance = GameData.GetCritChance(data);
            stats.Evasion = GameData.GetEvasion(data);
        }

        return stats;
    }

    public float RollDamage() =>
        Random.value * 100f < CritChance ? Damage * CritMultiplier : Damage;
}
