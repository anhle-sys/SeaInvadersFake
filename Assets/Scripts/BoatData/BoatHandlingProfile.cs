using UnityEngine;

/// <summary>
/// Thông số "cảm giác lái" của tàu, dùng chung cho nhiều prefab.
/// Tốc độ tối đa và tốc độ quay tối đa KHÔNG nằm ở đây: chúng lấy từ BoatData theo cấp tàu.
/// </summary>
[CreateAssetMenu(menuName = "Boats/Handling Profile", fileName = "BoatHandlingProfile")]
public class BoatHandlingProfile : ScriptableObject
{
    [Header("Tốc độ")]
    [Tooltip("Gia tốc tối đa khi tăng tốc (đơn vị/giây²)")]
    public float acceleration = 7f;
    [Tooltip("Gia tốc tối đa khi phanh / giảm tốc (đơn vị/giây²)")]
    public float deceleration = 14f;
    [Tooltip("Độ nhanh bám tốc độ đích khi TĂNG tốc (1/giây). Nhỏ = êm hơn")]
    public float speedResponse = 1.8f;
    [Tooltip("Độ nhanh bám tốc độ đích khi GIẢM tốc (1/giây). Lớn = thả ga dứt khoát hơn")]
    public float brakeResponse = 3.5f;
    [Tooltip("Khoảng cách bắt đầu giảm tốc khi tới đích (MoveTowards)")]
    public float slowingDistance = 7f;
    [Tooltip("Tốc độ còn lại khi cua gắt nhất (0..1)")]
    [Range(0.1f, 1f)] public float sharpTurnSpeedFactor = 0.6f;

    [Header("Bẻ lái")]
    [Tooltip("Độ nhạy lái: tốc độ quay mong muốn = góc lệch × hệ số này")]
    public float turnResponsiveness = 2.2f;
    [Tooltip("Gia tốc quay tối đa (độ/giây²). Nhỏ = tàu 'ì' hơn khi bẻ lái")]
    public float yawAcceleration = 150f;
    [Tooltip("Độ nhanh bám tốc độ quay mong muốn (1/giây). Nhỏ = vào/ra cua êm hơn")]
    public float yawResponse = 3.5f;
    [Tooltip("Bù trước tốc độ xoay của hướng mong muốn (0 = tắt, 1 = bù đủ). " +
             "Giúp lượn tròn / bám mục tiêu đang chạy mà không bị quay trễ")]
    [Range(0f, 1.5f)] public float turnFeedForward = 1f;

    [Header("Trôi ngang")]
    [Tooltip("Độ bám ngang: lớn = ít trôi ngang khi cua / bị va chạm")]
    public float lateralGrip = 3.5f;

    [Header("Tránh tàu cùng phe")]
    [Tooltip("Bán kính né tàu đồng đội. 0 = tắt")]
    public float avoidanceRadius = 4f;
    public float avoidanceWeight = 1.2f;

    [Header("Vật cản (đảo, đá...)")]
    [Tooltip("Layer của vật cản tĩnh cần phanh / né. Nothing = tắt")]
    public LayerMask obstacleMask = 1 << 0;
    [Tooltip("Khoảng trống giữ lại giữa mũi tàu và vật cản khi dừng")]
    public float obstacleMargin = 1f;
    [Tooltip("Độ mạnh lái né khi vật cản ở chéo phía trước")]
    public float obstacleSteerWeight = 1.5f;
}
