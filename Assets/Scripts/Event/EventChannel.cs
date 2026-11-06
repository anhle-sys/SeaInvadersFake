using System;
using UnityEngine;

public abstract class EventChannel<T> : ScriptableObject
{
    public event Action<T> OnRaised;

    public void Raise(T value) => OnRaised?.Invoke(value);
}
