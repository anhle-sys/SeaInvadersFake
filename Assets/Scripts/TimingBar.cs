using System;
using UnityEngine;
using DG.Tweening;

[Serializable]
public class BuffZone
{
    public string name;
    public float weight = 1f;
    public float buffValue = 1f;
}

public class TimingBar : MonoBehaviour
{
    [SerializeField] private RectTransform bar;
    [SerializeField] private RectTransform arrow;
    [SerializeField] private float speed = 0.8f;
    [SerializeField] private BuffZone[] zones;

    private Tween arrowTween;
    private float n;
    private float totalWeight;

    private void Awake()
    {
        totalWeight = 0f;
        if (zones == null) return;
        foreach (var z in zones) totalWeight += z.weight;
    }

    private void OnEnable() => StartBar();  

    private void OnDisable() => arrowTween?.Kill();

    public void StartBar()
    {
        arrowTween?.Kill();

        n = 0f;
        float oneWayDuration = 1f / (speed * 2f);

        arrowTween = DOTween.To(() => n, x =>
        {
            n = x;
            arrow.anchoredPosition = new Vector2(n * bar.rect.width, arrow.anchoredPosition.y);
        }, 1f, oneWayDuration)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true)   
            .SetLink(gameObject);
    }

    // Button Fight gọi hàm này
    public BuffZone StopAndGetZone()
    {
        arrowTween?.Kill();
        return GetZone(n);
    }

    private BuffZone GetZone(float pos)
    {
        if (zones == null || zones.Length == 0)
            return null;

        float target = pos * totalWeight;
        float acc = 0f;
        foreach (var z in zones)
        {
            acc += z.weight;
            if (target <= acc) return z;
        }
        return zones[zones.Length - 1];
    }
}