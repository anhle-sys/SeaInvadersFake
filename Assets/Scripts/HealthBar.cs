using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    public Image fill;
    IHealthBarTarget target;
    Camera cam;
    public Color allyColor = Color.yellow;
    public Color enemyColor = Color.red;

    public void Bind(IHealthBarTarget t, Camera c)
    {
        target = t;
        cam = c;
        fill.fillAmount = 1f;
        fill.color = t.IsAlly ? allyColor : enemyColor;
        target.OnHealthChanged += SetFill;
        target.OnHealthChanged += SetFill;
        target.OnDied += Release;
        gameObject.SetActive(true);
    }

    void SetFill(float pct) => fill.fillAmount = pct;

    void Release()
    {
        target.OnHealthChanged -= SetFill;
        target.OnDied -= Release;
        target = null;  
        GameController.Instance.HealthBarManager.Return(this);
    }

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 pos = cam.WorldToScreenPoint(target.BarWorldPosition);
        bool visible = pos.z > 0;
        fill.transform.parent.gameObject.SetActive(visible);
        if (visible) transform.position = pos;
    }
}