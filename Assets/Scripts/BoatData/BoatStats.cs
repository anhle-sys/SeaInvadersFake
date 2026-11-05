using UnityEngine;

/// <summary>
/// Chỉ số cuối cùng của một con tàu khi vào trận: chỉ số theo cấp + bonus nâng cấp.
/// Tàu phe Enemy không dùng tiến trình của người chơi (luôn cấp 0, không bonus).
/// </summary>
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
            // Mỗi loại tàu chỉ có nâng cấp sát thương của loại mình (Weapon / Explosive / Air),
            // nâng cấp không tồn tại trả về 0 nên cộng cả ba là an toàn.
            stats.Damage += GameData.GetUpgradeBonus(data, StatType.Weapon)
                          + GameData.GetUpgradeBonus(data, StatType.Explosive)
                          + GameData.GetUpgradeBonus(data, StatType.Air);
            stats.Armor = GameData.GetArmor(data);
            stats.CritChance = GameData.GetCritChance(data);
            stats.Evasion = GameData.GetEvasion(data);
        }

        return stats;
    }

    /// <summary>Sát thương của một đòn đánh, đã tính chí mạng.</summary>
    public float RollDamage() =>
        Random.value * 100f < CritChance ? Damage * CritMultiplier : Damage;
}
