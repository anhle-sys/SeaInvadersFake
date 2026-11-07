
using UnityEngine;
using DG.Tweening;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera cam;

    public Transform idleCamera;
    public Transform battleCamera;
    public Transform upgrateCamera;

    [SerializeField] private float moveDuration = 0.8f;

    [Header("Focus mũi tàu (popup nâng cấp)")]
    [SerializeField] private float focusDistance = 5.5f;
    [SerializeField] private float focusHeight = 2.6f;
    [SerializeField] private float focusSide = 2.2f;
    [SerializeField] private float framePadding = 1.25f;
    [Range(0.3f, 1f)] [SerializeField] private float freeScreenHeight = 0.65f;
    [Range(0f, 1f)] [SerializeField] private float lookAtBowRatio = 0.6f;
    [SerializeField] private float lookHeight = 0.4f;
    [Range(0.2f, 0.9f)] [SerializeField] private float focusViewportY = 0.68f;
    [SerializeField] private float focusDuration = 0.6f;
    [Header("Camera FOV")]
    [SerializeField] private float idleFOV = 33f;
    [SerializeField] private float battleFOV = 41f;
    [SerializeField] private float upgradeFOV = 33f;
    [SerializeField] private float fovDuration = 0.8f;
    private Tween cameraMoveTween;
    private Tween cameraRotateTween;
    private Tween shakeTween;
    private Tween cameraFOVTween;

    public void ChangeCameraByState()
    {
        switch (GameController.Instance.state)
        {
            case GameState.Idle:
                SetCameraPosition(idleCamera);
                SetCameraFOV(idleFOV);
                break;

            case GameState.Battle:
                SetCameraPosition(battleCamera);
                SetCameraFOV(battleFOV);
                break;

            case GameState.Upgrade:
                SetCameraPosition(upgrateCamera);
                SetCameraFOV(upgradeFOV);
                break;

            case GameState.GameOver:
                break;
        }
    }

    public void SetCameraPosition(Transform target)
    {
        if (target == null || cam == null) return;
        MoveTo(target.position, target.rotation, moveDuration);
    }

    public void FocusOnBoat(Transform boat)
    {
        if (boat == null || cam == null) return;

        BoatFraming f = BoatFraming.From(boat);
        Vector3 look = f.TowardBow(lookAtBowRatio) + Vector3.up * lookHeight;

        // Hướng nhìn chéo 3/4 từ phía trước mũi tàu
        Vector3 dir = f.Forward * focusDistance + f.Right * focusSide + Vector3.up * focusHeight;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : (f.Forward + Vector3.up).normalized;

        float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect);
        float limitHalf = Mathf.Min(hHalf, Mathf.Atan(Mathf.Tan(vHalf) * freeScreenHeight));
        float distance = f.Length * framePadding * 0.5f / Mathf.Tan(limitHalf);

        Vector3 pos = look + dir * distance;
        Quaternion rot = Quaternion.LookRotation(look - pos, Vector3.up);

        // Cúi camera thêm một chút để điểm nhìn nằm ở focusViewportY thay vì giữa màn hình
        float pitch = Mathf.Atan(2f * (focusViewportY - 0.5f) * Mathf.Tan(vHalf)) * Mathf.Rad2Deg;
        rot *= Quaternion.Euler(pitch, 0f, 0f);

        MoveTo(pos, rot, focusDuration);
    }

    public void Shake(float duration, float strength)
    {
        if (cam == null || strength <= 0f) return;

        if (cameraMoveTween != null && cameraMoveTween.IsActive() && cameraMoveTween.IsPlaying())
        {
            cameraMoveTween.Complete();
            cameraRotateTween?.Complete();
        }

        shakeTween?.Complete();
        shakeTween = cam.transform
            .DOShakePosition(duration, strength, 18, 90f, false, true)
            .SetLink(cam.gameObject);
    }

    private void MoveTo(Vector3 position, Quaternion rotation, float duration)
    {
        shakeTween?.Complete();
        cameraMoveTween?.Kill();
        cameraRotateTween?.Kill();

        cameraMoveTween = cam.transform.DOMove(position, duration)
            .SetEase(Ease.InOutSine)
            .SetLink(cam.gameObject);

        cameraRotateTween = cam.transform.DORotateQuaternion(rotation, duration)
            .SetEase(Ease.InOutSine)
            .SetLink(cam.gameObject);
    }

    private void SetCameraFOV(float targetFOV)
    {
        if (cam == null) return;

        cameraFOVTween?.Kill();

        cameraFOVTween = cam.DOFieldOfView(targetFOV, fovDuration)
            .SetEase(Ease.InOutSine)
            .SetLink(cam.gameObject);
    }
}
