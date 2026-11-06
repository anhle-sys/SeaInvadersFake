using System;
using UnityEngine;

public interface IHealthBarTarget
{
    event Action<float> OnHealthChanged;   // 0-1
    event Action OnDied;
    Vector3 BarWorldPosition { get; }
    bool IsAlly { get; }
}