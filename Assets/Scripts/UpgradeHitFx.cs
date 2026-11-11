using DG.Tweening;
using UnityEngine;

public class UpgradeHitFx : MonoBehaviour
{
    [Header("VFX")]
    [Tooltip("Prefab particle khói bụi / tia lửa. Tự hủy sau khi chạy xong.")]
    [SerializeField] private GameObject vfxPrefab;
    [Tooltip("Vị trí phát VFX trên thân tàu: 0 = tâm, 1 = mũi")]
    [Range(0f, 1f)] [SerializeField] private float vfxBowRatio = 0.6f;
    [SerializeField] private float vfxHeightOffset = 0.1f;
    [SerializeField] private float vfxLifetime = 2f;

    [Header("Tàu bị dập")]
    [Tooltip("Dập xuống (y âm) và bè ra (x, z dương) như bị búa đập")]
    [SerializeField] private Vector3 punch = new Vector3(0.12f, -0.18f, 0.12f);
    [SerializeField] private float punchDuration = 0.4f;
    [SerializeField] private int punchVibrato = 7;

    [Header("Rung camera")]
    [SerializeField] private float shakeDuration = 0.25f;
    [SerializeField] private float shakeStrength = 0.18f;

    private Tween punchTween;
    private Transform punchedBoat;
    private Vector3 punchedBaseScale;

    public void Play(Transform boat)
    {
        if (boat == null)
            return;

        SpawnVfx(boat);
        PunchBoat(boat);

        if (GameController.Instance != null && GameController.Instance.CameraController != null)
            GameController.Instance.CameraController.Shake(shakeDuration, shakeStrength);
    }

    private void SpawnVfx(Transform boat)
    {
        if (vfxPrefab == null)
            return;

        BoatFraming f = BoatFraming.From(boat);
        Vector3 pos = f.TowardBow(vfxBowRatio) + Vector3.up * vfxHeightOffset;

        GameObject vfx = Instantiate(vfxPrefab, pos, Quaternion.LookRotation(f.Forward, Vector3.up));
        Destroy(vfx, vfxLifetime);
    }

    private void PunchBoat(Transform boat)
    {
        // Đập liên tiếp: trả tàu trước về đúng scale gốc rồi mới đập tiếp, tránh scale bị trôi
        if (punchTween != null && punchTween.IsActive())
        {
            punchTween.Kill();
            if (punchedBoat != null)
                punchedBoat.localScale = punchedBaseScale;
        }

        punchedBoat = boat;
        punchedBaseScale = boat.localScale;

        punchTween = boat.DOPunchScale(Vector3.Scale(punch, punchedBaseScale), punchDuration, punchVibrato, 0.6f)
            .SetLink(boat.gameObject)
            .OnKill(() =>
            {
                if (punchedBoat != null)
                    punchedBoat.localScale = punchedBaseScale;
            });
    }
}
