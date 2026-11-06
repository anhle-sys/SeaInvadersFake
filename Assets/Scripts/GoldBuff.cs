using UnityEngine;

public static class GoldBuff
{
    public static float Multiplier { get; private set; } = 1f;

    public static void Set(float value) => Multiplier = Mathf.Max(0f, value);
    public static void Reset() => Multiplier = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Reset();
}
