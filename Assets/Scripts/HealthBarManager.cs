using System.Collections.Generic;
using UnityEngine;

public class HealthBarManager : MonoBehaviour
{
    public HealthBar prefab;
    Camera cam;
    readonly Queue<HealthBar> pool = new Queue<HealthBar>();

    void Awake() { cam = Camera.main; }

    public void Register(IHealthBarTarget t)
    {
        HealthBar bar = pool.Count > 0 ? pool.Dequeue() : Instantiate(prefab, transform);
        bar.Bind(t, cam);
    }

    public void Return(HealthBar bar)
    {
        bar.gameObject.SetActive(false);
        pool.Enqueue(bar);
    }
}