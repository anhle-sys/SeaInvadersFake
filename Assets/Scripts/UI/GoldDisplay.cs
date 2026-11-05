using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Hiển thị số vàng của người chơi.
/// - Số chạy dần tới giá trị mới khi vàng thay đổi.
/// - Nảy lên + nháy màu khi nhận vàng, nháy màu khi tiêu vàng.
/// - Rung + nháy đỏ khi mua thất bại vì không đủ vàng.
/// </summary>
public class GoldDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text goldText;
    [Tooltip("Phần nảy/rung khi vàng thay đổi. Để trống sẽ dùng chính transform này.")]
    [SerializeField] private RectTransform animatedRoot;

    [Header("Format")]
    [SerializeField] private string format = "{0:N0}";

    [Header("Animation")]
    [SerializeField] private float countDuration = 0.4f;
    [SerializeField] private float punchScale = 0.15f;
    [SerializeField] private Color gainColor = new Color(0.55f, 1f, 0.45f);
    [SerializeField] private Color spendColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color notEnoughColor = new Color(1f, 0.35f, 0.35f);

    private int shownGold;
    private Color baseColor;
    private Tween countTween;
    private Tween scaleTween;
    private Tween colorTween;

    private void Awake()
    {
        if (animatedRoot == null)
            animatedRoot = transform as RectTransform;
        if (goldText != null)
            baseColor = goldText.color;
    }

    private void OnEnable()
    {
        GameData.OnGoldChanged += HandleGoldChanged;
        GameData.OnNotEnoughGold += HandleNotEnoughGold;
        SetImmediate(GameData.Gold);
    }

    private void OnDisable()
    {
        GameData.OnGoldChanged -= HandleGoldChanged;
        GameData.OnNotEnoughGold -= HandleNotEnoughGold;
        KillTweens();
        if (animatedRoot != null) animatedRoot.localScale = Vector3.one;
        if (goldText != null) goldText.color = baseColor;
    }

    private void SetImmediate(int gold)
    {
        countTween?.Kill();
        shownGold = gold;
        Render(gold);
    }

    private void HandleGoldChanged(int gold)
    {
        if (!isActiveAndEnabled || gold == shownGold)
        {
            SetImmediate(gold);
            return;
        }

        bool gained = gold > shownGold;

        countTween?.Kill();
        countTween = DOTween.To(() => shownGold, v => { shownGold = v; Render(v); }, gold, countDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject);

        if (gained)
            Punch();
        Flash(gained ? gainColor : spendColor);
    }

    private void HandleNotEnoughGold(int required)
    {
        if (!isActiveAndEnabled || animatedRoot == null)
            return;

        scaleTween?.Kill(true);
        animatedRoot.localScale = Vector3.one;
        scaleTween = animatedRoot.DOShakeAnchorPos(0.35f, new Vector2(14f, 0f), 20, 0f)
            .SetUpdate(true)
            .SetLink(gameObject);
        Flash(notEnoughColor);
    }

    private void Punch()
    {
        if (animatedRoot == null) return;
        scaleTween?.Kill(true);
        animatedRoot.localScale = Vector3.one;
        scaleTween = animatedRoot.DOPunchScale(Vector3.one * punchScale, 0.3f, 6, 0.6f)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void Flash(Color color)
    {
        if (goldText == null) return;
        colorTween?.Kill();
        goldText.color = color;
        colorTween = goldText.DOColor(baseColor, 0.5f)
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void Render(int value)
    {
        if (goldText != null)
            goldText.text = string.Format(format, value);
    }

    private void KillTweens()
    {
        countTween?.Kill();
        scaleTween?.Kill(true);
        colorTween?.Kill();
    }
}
