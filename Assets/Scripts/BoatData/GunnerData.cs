using UnityEngine;

public enum CannonSide { Right, Left, Both }

[CreateAssetMenu(menuName = "Boats/Gunner Data", fileName = "GunnerData")]
public class GunnerData : BoatData
{
    [Header("Gunner")]
    public GameObject bulletPrefab;
    public float attackRange = 8f;
    public float attackCooldown = 2f;
    public float volleyDelay = 0.12f;
    public CannonSide cannonSide = CannonSide.Both;

    [Header("Combat Movement")]
    [Range(0.05f, 1f)] public float combatSpeedMultiplier = 0.4f;
    [Tooltip("Tốc độ (tỉ lệ so với tốc độ combat) khi đang quay để đưa hông về phía địch. " +
             "1 = không giảm tốc")]
    [Range(0.1f, 1f)] public float turningSpeedFactor = 0.35f;
    [Tooltip("Tốc độ (tỉ lệ so với tốc độ combat) khi hông đã chĩa vào địch và đang lượn bắn. " +
             "Tàu chỉ tăng tốc lại khi địch ở xa hoặc không còn địch (đi tìm địch khác)")]
    [Range(0.1f, 1f)] public float engagedSpeedFactor = 0.4f;
    [Tooltip("Hông lệch so với hướng tới địch từ góc này trở lên thì chạy chậm nhất (turningSpeedFactor)")]
    public float turningSlowAngle = 35f;
    [Tooltip("Hông lệch dưới góc này thì chạy tốc độ combat bình thường")]
    public float turningFullSpeedAngle = 10f;
    [Range(0.3f, 1f)] public float combatDistanceRatio = 0.8f;
    public float radiusCorrection = 1.5f;
    public float turnRadius = 3f;
    [Tooltip("Kết thúc lướt khi địch ở sau ngang hông bao xa. 0 = bẻ lái ngay khi địch ngang hông")]
    public float passDistance = 0f;
    [Tooltip("Thời gian tối đa chạy thẳng lướt qua địch khi vào combat. 0 = tắt, bẻ lái ngay để hông hướng về địch")]
    public float passMaxTime = 3f;
    [Tooltip("Khoảng cách ngang khi lướt qua địch. 0 = bằng bán kính lượn (attackRange × combatDistanceRatio)")]
    public float passOffset = 0f;
    public float orbitCorrectionGain = 8f;
    [Tooltip("Góc bẻ tối đa hướng VÀO tâm khi tàu ở xa hơn bán kính lượn")]
    public float maxOrbitCorrection = 40f;
    [Tooltip("Góc bẻ tối đa hướng RA NGOÀI khi tàu ở gần hơn bán kính lượn. " +
             "Nhỏ = vừa lướt qua địch là quay đầu ngay về phía địch, vòng quay gọn, rồi nới dần ra")]
    public float maxOutwardCorrection = 15f;
}