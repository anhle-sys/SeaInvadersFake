using UnityEngine;

// Thông số bay dùng chung cho mọi máy bay (Fly). Chỉnh 1 asset là đổi cho cả đàn, giống BoatHandlingProfile của tàu.
[CreateAssetMenu(menuName = "Boats/Flight Profile", fileName = "FlightProfile")]
public class FlightProfile : ScriptableObject
{
    [Header("Kích thước")]
    [Tooltip("Scale thế giới khi đang bay = scale lúc đỗ trên tàu × hệ số này")]
    public float flightScaleMultiplier = 2f;
    [Tooltip("Thời gian phóng to / thu nhỏ khi cất cánh / hạ cánh (giây)")]
    public float scaleTransitionTime = 0.8f;

    [Header("Bay")]
    [Tooltip("Độ cao bay hành trình (so với mặt biển y = seaLevel)")]
    public float flyHeight = 10f;
    [Tooltip("Độ cao khi bổ nhào bắn mục tiêu")]
    public float attackHeight = 6f;
    public float seaLevel = 0f;
    [Tooltip("Tốc độ lên / xuống tối đa (đơn vị/giây)")]
    public float climbSpeed = 5f;
    [Tooltip("Gia tốc tiến (đơn vị/giây²)")]
    public float acceleration = 6f;
    [Tooltip("Độ nhạy lái: tốc độ quay mong muốn = góc lệch × hệ số này (1/giây)")]
    public float turnGain = 2.5f;
    [Tooltip("Gia tốc quay (độ/giây²). Nhỏ = vào / ra cua êm hơn")]
    public float yawAcceleration = 300f;
    [Tooltip("Tốc độ còn lại khi cua gắt nhất (0..1)")]
    [Range(0.3f, 1f)] public float turnSpeedFactor = 0.8f;
    [Tooltip("Góc ngóc mũi tối đa (chỉ để nhìn)")]
    public float maxPitch = 25f;
    [Tooltip("Góc nghiêng cánh tối đa khi cua (chỉ để nhìn)")]
    public float maxBank = 40f;
    [Tooltip("Độ mượt của góc nghiêng / ngóc mũi (1/giây)")]
    public float attitudeResponse = 5f;

    [Header("Né nhau")]
    [Tooltip("Máy bay khác nằm trong bán kính này là bắt đầu né")]
    public float avoidRadius = 3.5f;
    [Tooltip("Độ mạnh lái tránh theo phương ngang (so với hướng đang muốn bay tới)")]
    public float avoidWeight = 1.6f;
    [Tooltip("Độ lệch cao tối đa khi tách nhau theo phương dọc (đơn vị)")]
    public float avoidVertical = 1.5f;

    [Header("Cất cánh")]
    [Tooltip("Thời gian chạy thẳng theo hướng tàu trước khi bẻ lái (giây)")]
    public float takeoffRollTime = 0.6f;
    [Tooltip("Tốc độ ban đầu khi rời tàu (tỉ lệ so với planeSpeed)")]
    [Range(0f, 1f)] public float takeoffSpeedRatio = 0.45f;

    [Header("Tấn công")]
    [Tooltip("Chỉ bắn khi mục tiêu nằm trong nón phía trước mũi (độ, mặt phẳng ngang)")]
    public float fireConeAngle = 25f;
    [Tooltip("Bắt đầu bổ nhào khi cách mục tiêu (planeAttackRange × hệ số này)")]
    public float diveStartMultiplier = 2.5f;
    [Tooltip("Bay ra xa tới (bán kính cua × hệ số này) rồi mới vòng lại đánh tiếp. Nhỏ = bám sát địch hơn")]
    public float extendTurnRadiusMultiplier = 2f;
    [Tooltip("Khi vòng lại (mũi còn lệch xa mục tiêu) cua gắt hơn bình thường bấy nhiêu lần")]
    public float repositionTurnBoost = 1.8f;
    [Tooltip("Tốc độ còn lại khi đang lao vào bắn (0..1). Nhỏ = ở trên mục tiêu lâu hơn, bắn được nhiều phát hơn mỗi lượt")]
    [Range(0.3f, 1f)] public float attackRunSpeedFactor = 0.65f;
    [Tooltip("Mất dấu địch quá thời gian này mới quay về tàu (giây). Tránh bay qua bay lại khi địch ở mép tầm phát hiện")]
    public float lostTargetGraceTime = 1f;

    [Header("Quay về tàu")]
    [Tooltip("Cách điểm đỗ (ngang) bao nhiêu thì bắt đầu hạ độ cao")]
    public float descendDistance = 18f;
    [Tooltip("Cách điểm đỗ bao nhiêu thì bắt đầu giảm tốc và lái gắt hơn")]
    public float approachDistance = 8f;
    [Tooltip("Cách điểm đỗ (3D) bao nhiêu thì vào chặng chạm boong")]
    public float touchdownDistance = 2f;
    public float landSnapDistance = 0.15f;
}
