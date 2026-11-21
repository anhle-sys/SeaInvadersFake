
using UnityEngine;
using DG.Tweening;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera cam;

    public Transform idleCamera;
    public Transform battleCamera;
    public Transform upgradeCamera;

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
    [SerializeField] private float battleFOV = 54f;
    [SerializeField] private float upgradeFOV = 33f;
    [SerializeField] private float fovDuration = 0.8f;

    [Header("Giới hạn tàu trong tầm nhìn trận đấu")]
    [Tooltip("Độ cao mặt nước")]
    [SerializeField] private float waterHeight = 0f;
    [Tooltip("Lề trái/phải màn hình tàu không được vào (tỉ lệ màn hình)")]
    [Range(0f, 0.3f)] [SerializeField] private float viewMarginX = 0.05f;
    [Tooltip("Lề trên/dưới màn hình tàu không được vào (tỉ lệ màn hình)")]
    [Range(0f, 0.3f)] [SerializeField] private float viewMarginY = 0.03f;
    [Tooltip("Giới hạn xa nhất tính từ camera (mép trên màn hình nhìn rất xa nên phải chặn)")]
    [SerializeField] private float maxViewDistance = 170f;
    [Tooltip("Tâm tàu cách mép vùng nhìn ít nhất khoảng này để cả thân tàu còn trong hình")]
    [SerializeField] private float boundsPadding = 2f;
    [Tooltip("Cách mép khoảng này tàu bắt đầu bẻ lái quay vào")]
    [SerializeField] private float boundsSoftMargin = 4f;

    [Header("Camera bám theo trận đánh")]
    [Tooltip("Bật: trong trận camera tự di chuyển / lùi ra xa để luôn thấy hết các tàu")]
    [SerializeField] private bool followBattle = true;
    [Tooltip("Khoảng trống quanh mỗi tàu cần nằm trong hình")]
    [SerializeField] private float trackPadding = 5f;
    [Tooltip("Lề màn hình mỗi bên để trống (tỉ lệ màn hình)")]
    [Range(0f, 0.3f)] [SerializeField] private float trackScreenMargin = 0.08f;
    [Tooltip("Độ cao camera thấp nhất = mức zoom gần nhất")]
    [SerializeField] private float trackMinHeight = 32f;
    [Tooltip("Độ cao camera cao nhất = mức zoom xa nhất")]
    [SerializeField] private float trackMaxHeight = 80f;
    [Tooltip("Thời gian làm mượt chuyển động camera (giây)")]
    [SerializeField] private float trackSmoothTime = 0.7f;

    private readonly System.Collections.Generic.List<BoatHealth> trackedBoats = new System.Collections.Generic.List<BoatHealth>();
    private bool tracking;
    private Vector3 trackVelocity;
    private float boatRefreshTime;

    private BattleBounds battleBounds;
    private float boundsAspect;

    // Vùng mặt nước nhìn thấy từ góc camera trận đấu (tính theo vị trí đích battleCamera, không theo camera đang tween)
    public BattleBounds BattleBounds
    {
        get
        {
            if (cam == null || battleCamera == null)
                return null;

            if (battleBounds == null || !Mathf.Approximately(boundsAspect, cam.aspect))
            {
                boundsAspect = cam.aspect;
                battleBounds = new BattleBounds(battleCamera.position, battleCamera.rotation, battleFOV, cam.aspect,
                                                waterHeight, viewMarginX, viewMarginY, maxViewDistance,
                                                boundsPadding, boundsSoftMargin);
            }
            return battleBounds;
        }
    }

    private void OnValidate() => battleBounds = null;

    private void OnDrawGizmosSelected()
    {
        battleBounds = null; // battleCamera có thể vừa bị kéo trong editor
        BattleBounds bounds = BattleBounds;
        if (bounds == null) return;

        Gizmos.color = Color.yellow;
        Vector3[] c = bounds.Corners;
        for (int i = 0; i < c.Length; i++)
            Gizmos.DrawLine(c[i], c[(i + 1) % c.Length]);
    }

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
                if (followBattle && battleCamera != null && cam != null)
                    StartBattleTracking();
                else
                    SetCameraPosition(battleCamera);
                SetCameraFOV(battleFOV);
                break;

            case GameState.Upgrade:
                SetCameraPosition(upgradeCamera);
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

    // Vào trận: chỉ tween góc quay về góc battleCamera, vị trí do LateUpdate bám theo các tàu
    private void StartBattleTracking()
    {
        shakeTween?.Complete();
        cameraMoveTween?.Kill();
        cameraRotateTween?.Kill();

        cameraRotateTween = cam.transform.DORotateQuaternion(battleCamera.rotation, moveDuration)
            .SetEase(Ease.InOutSine)
            .SetLink(cam.gameObject);

        tracking = true;
        trackVelocity = Vector3.zero;
        boatRefreshTime = 0f;
    }

    private void LateUpdate()
    {
        if (!tracking || cam == null || battleCamera == null)
            return;

        GameController gc = GameController.Instance;
        if (gc == null || gc.state != GameState.Battle)
            return;

        if (!TryGetBattleFramePosition(out Vector3 target))
            return;

        cam.transform.position = Vector3.SmoothDamp(cam.transform.position, target, ref trackVelocity, trackSmoothTime);
    }

    // Vị trí camera (giữ nguyên góc battleCamera và battleFOV) gần nhất mà vẫn thấy hết các tàu còn sống:
    // tàu tản rộng -> camera lùi ra xa, tàu dồn lại -> camera tiến vào, tâm khung hình luôn theo trận đánh
    private bool TryGetBattleFramePosition(out Vector3 position)
    {
        position = default;

        if (Time.time >= boatRefreshTime)
        {
            boatRefreshTime = Time.time + 0.5f;
            trackedBoats.Clear();
            trackedBoats.AddRange(FindObjectsByType<BoatHealth>(FindObjectsSortMode.None));
        }

        Quaternion rot = battleCamera.rotation;
        Quaternion inv = Quaternion.Inverse(rot);
        float tanV = Mathf.Tan(battleFOV * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * cam.aspect * (1f - 2f * trackScreenMargin);
        tanV *= 1f - 2f * trackScreenMargin;

        // Tâm trận đánh = tâm vùng bao các tàu còn sống trên mặt nước: điểm này luôn nằm giữa màn hình
        // (không lệch về phía tàu to / tàu gần camera)
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        int count = 0;

        for (int i = 0; i < trackedBoats.Count; i++)
        {
            BoatHealth boat = trackedBoats[i];
            if (boat == null || boat.IsDead || !boat.gameObject.activeInHierarchy)
                continue;

            Vector3 p = boat.transform.position;
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            count++;
        }

        if (count == 0)
            return false;

        Vector3 focus = new Vector3((minX + maxX) * 0.5f, waterHeight, (minZ + maxZ) * 0.5f);

        // Camera nằm trên trục nhìn đi qua tâm trận, cách tâm một khoảng distance. Trong không gian camera,
        // điểm (x, y, z) tính từ tâm nằm trong hình khi |x| <= (z + distance) * tanH (tương tự cho y)
        // -> lấy khoảng cách nhỏ nhất mà mọi tàu (kèm khoảng trống quanh nó) đều lọt vào hình
        float distance = 0f;
        for (int i = 0; i < trackedBoats.Count; i++)
        {
            BoatHealth boat = trackedBoats[i];
            if (boat == null || boat.IsDead || !boat.gameObject.activeInHierarchy)
                continue;

            Vector3 center = boat.transform.position;
            center.y = waterHeight;

            for (int k = 0; k < 4; k++)
            {
                Vector3 offset = k == 0 ? Vector3.right : k == 1 ? Vector3.left : k == 2 ? Vector3.forward : Vector3.back;
                Vector3 p = inv * (center + offset * trackPadding - focus);
                distance = Mathf.Max(distance, Mathf.Abs(p.x) / tanH - p.z, Mathf.Abs(p.y) / tanV - p.z);
            }
        }

        // Giới hạn zoom theo độ cao camera
        Vector3 forward = rot * Vector3.forward;
        if (forward.y < -0.01f)
        {
            float minDistance = trackMinHeight / -forward.y;
            float maxDistance = Mathf.Max(trackMinHeight, trackMaxHeight) / -forward.y;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        position = focus - forward * distance;
        return true;
    }

    private void MoveTo(Vector3 position, Quaternion rotation, float duration)
    {
        tracking = false;
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
