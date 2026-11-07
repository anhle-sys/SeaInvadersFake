using UnityEngine;

public struct BoatMovementContext
{
    public BoatHandlingProfile profile;
    public BoatSteeringSensors sensors;
    public float dt;
    public float moveSpeed;         // tốc độ tối đa gốc
    public float maxSpeed;          // moveSpeed × speedMultiplier
    public float rotateSpeed;
    public float rotScale;
    public float radiusScale;
    public float brakeDecel;
    public float measuredForward;   // tốc độ thực tế dọc thân tàu
    public float currentSpeed;
    public float currentYawRate;    // độ/giây
}

// Thông số tăng tốc / giảm tốc khi cua của bước hiện tại: mặc định lấy từ BoatHandlingProfile, modifier có thể chỉnh
public struct BoatSpeedTuning
{
    public float acceleration;          // gia tốc tối đa khi tăng tốc
    public float speedResponse;         // độ nhanh bám tốc độ đích khi tăng tốc
    public float sharpTurnSpeedFactor;  // tốc độ còn lại khi cua gắt nhất (0..1)
}

public interface IBoatMovementModifier
{
    // Sau khi cảm biến chạy: chỉnh giới hạn tốc độ (phanh) và lực bẻ lái né vật cản / đồng đội
    void ModifyObstacle(in BoatMovementContext ctx, ref float speedLimit, ref Vector3 steer) { }

    // Hướng né mềm đồng đội (độ dài 0..1). Trả về Vector3.zero để tàu bỏ qua đồng đội, không né
    Vector3 ModifyAvoidance(in BoatMovementContext ctx, Vector3 avoidance) => avoidance;

    // Chỉnh góc lệch tới hướng mong muốn (vd: chọn chiều quay đầu)
    float ModifyHeadingAngle(in BoatMovementContext ctx, float angle) => angle;

    // Chỉnh tốc độ thực tế dọc thân tàu trước khi tính gia tốc / phanh
    float ModifyMeasuredForward(in BoatMovementContext ctx, float measuredForward) => measuredForward;

    // Sau khi tính tốc độ: ghi đè currentSpeed nếu cần. Trả về false = giữ nguyên
    bool TryOverrideSpeed(in BoatMovementContext ctx, float measuredForward, out float speed)
    {
        speed = 0f;
        return false;
    }

    // Tốc độ xoay tối đa (độ/giây). turnAuthority (0.3..1) là khả năng bẻ lái theo tốc độ hiện tại
    float GetMaxYawRate(in BoatMovementContext ctx, float turnAuthority) => ctx.rotateSpeed * ctx.rotScale * turnAuthority;

    // Chỉnh gia tốc và mức giảm tốc khi cua gắt (vd: Banger lao vào địch tăng tốc gấp, không chậm lại khi cua)
    void ModifySpeedTuning(in BoatMovementContext ctx, ref BoatSpeedTuning tuning) { }

    // Gọi khi BoatMovementController.ResetMovement
    void OnMovementReset() { }
}
