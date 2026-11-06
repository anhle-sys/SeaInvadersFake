using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    public Image fill;
    IHealthBarTarget target;
    Camera cam;
    HealthBarManager owner;
    public Color allyColor = Color.yellow;
    public Color enemyColor = Color.red;

    [Header("Hiệu ứng mất máu")]
    public float punchScale = 0.25f;
    public float punchDuration = 0.25f;
    public Color hitFlashColor = Color.white;
    public float hitFlashDuration = 0.15f;

    Color teamColor;
    Tween punchTween;
    Tween flashTween;

    public void Bind(IHealthBarTarget t, Camera c, HealthBarManager manager)
    {
        target = t;
        cam = c;
        owner = manager;
        ResetHitFx();
        fill.fillAmount = 1f;
        teamColor = t.IsAlly ? allyColor : enemyColor;
        fill.color = teamColor;
        target.OnHealthChanged += SetFill;
        target.OnDied += Release;
        gameObject.SetActive(true);
    }

    void SetFill(float pct)
    {
        bool lost = pct < fill.fillAmount - 0.0001f;
        fill.fillAmount = pct;
        if (lost && isActiveAndEnabled)
            PlayHitFx();
    }

    // Thanh máu nảy lên + nháy trắng khi mất máu
    void PlayHitFx()
    {
        punchTween?.Kill(true);
        transform.localScale = Vector3.one;
        punchTween = transform.DOPunchScale(Vector3.one * punchScale, punchDuration, 8, 0.5f).SetLink(gameObject);

        flashTween?.Kill();
        fill.color = hitFlashColor;
        flashTween = fill.DOColor(teamColor, hitFlashDuration).SetEase(Ease.InQuad).SetLink(gameObject);
    }

    void ResetHitFx()
    {
        punchTween?.Kill();
        flashTween?.Kill();
        transform.localScale = Vector3.one;
    }

    void Release()
    {
        if (target != null)
        {
            target.OnHealthChanged -= SetFill;
            target.OnDied -= Release;
            target = null;
        }

        // Thanh máu / manager có thể đã bị hủy trước (đổi scene, thoát Play)
        if (this == null)
            return;

        ResetHitFx();

        if (owner != null)
            owner.Return(this);
        else
            gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Mục tiêu bị Destroy mà không báo OnDied: interface != null vẫn true, phải check kiểu Unity
        if (target is Object obj && obj == null)
        {
            Release();
            return;
        }

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 pos = cam.WorldToScreenPoint(target.BarWorldPosition);
        bool visible = pos.z > 0;
        fill.transform.parent.gameObject.SetActive(visible);
        if (visible) transform.position = pos;
    }
}
