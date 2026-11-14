using System;
using System.Collections.Generic;
using UnityEngine;

// Cấu hình cắt lỗ cho 1 loại tàu. Đổi mask / kích thước / bố cục rồi bấm Bake
// trong Inspector để cắt lại từ mesh gốc và lưu đè lên mesh đầu ra.
[CreateAssetMenu(menuName = "Mesh Editor/Hole Pattern Config", fileName = "HolePatternConfig")]
public class HolePatternConfig : ScriptableObject
{
    public enum Axis { X, Y, Z }
    public enum Placement { Side, Top }
    public enum SideMode { Both, Positive, Negative }

    [Serializable]
    public class HoleGroup
    {
        public string name = "Group";
        public bool enabled = true;

        [Tooltip("Side: tia bắn ngang vào mạn tàu. Top: tia bắn từ trên xuống (sàn, boong).")]
        public Placement placement = Placement.Side;

        [Tooltip("Bật: lỗ có trên mesh hiển thị (MeshFilter). Tắt: chỉ có trên Source Mesh " +
                 "(dùng cho sàn tàu ở chế độ Upward Regions As Decals).")]
        public bool includeInDisplay = true;

        [Header("Kích thước (world, m)")]
        public float maskSize = 0.45f;

        [Tooltip("Độ sâu cắt (m). Lớn hơn độ dày vỏ để lỗ xuyên 2 lớp vỏ; rất nhỏ (vd. 0.006) để chỉ cắt lớp mặt trên cùng.")]
        public float projectionDepth = 0.12f;

        [Header("Bố cục (đơn vị local của mesh)")]
        [Tooltip("Trục dọc thân tàu (local) mà các lỗ trải theo.")]
        public Axis alongAxis = Axis.Z;

        public float alongFrom = -1f;
        public float alongTo = 1f;
        public float alongStep = 0.5f;

        [Tooltip("Side: các độ cao (trục Y local), dùng xen kẽ theo từng lỗ.\n" +
                 "Top: các hàng theo trục ngang (vuông góc trục dọc), mỗi hàng 1 dãy lỗ.")]
        public List<float> offsets = new List<float> { 0f };

        [Tooltip("Top: dịch các hàng lẻ dọc thân tàu khoảng này để lỗ so le.")]
        public float oddRowShift = 0f;

        [Tooltip("Side: cắt ở mạn nào (theo dấu của trục ngang).")]
        public SideMode sides = SideMode.Both;

        [Tooltip("Thêm các điểm lẻ: x = vị trí dọc, y = độ cao (Side) hoặc vị trí ngang (Top).")]
        public List<Vector2> extraPoints = new List<Vector2>();

        [Header("Lọc bề mặt")]
        [Tooltip("Chỉ cắt khi tia trúng submesh này (-1 = bất kỳ).")]
        public int requiredSubmesh = -1;

        [Tooltip("Chỉ cắt khi |dot(pháp tuyến mặt trúng, hướng tia)| >= giá trị này.")]
        [Range(0f, 1f)] public float minSurfaceAlignment = 0.5f;

        [Header("Biến thể")]
        [Tooltip("Góc xoay mask tăng thêm sau mỗi lỗ (độ), để các lỗ không giống hệt nhau.")]
        public float rotationStep = 47f;
    }

    // Lỗ cắt tay trong Mesh Editor: lưu vị trí trên mesh (local) để bake lại với mask mới.
    [Serializable]
    public class ManualHole
    {
        public Vector3 localPoint;
        public Vector3 localNormal = Vector3.up;
        public float maskSize = 0.45f;
        public float projectionDepth = 0.12f;
        public float rotation;
        public bool includeInDisplay = true;
    }

    [Header("Nguồn")]
    [Tooltip("Mesh gốc CHƯA cắt.")]
    public Mesh sourceMesh;

    [Tooltip("Prefab tàu: dùng transform của object thân tàu để đổi kích thước lỗ (world) sang local. " +
             "Để trống nếu không phải prefab (dùng Fallback Hull Scale).")]
    public GameObject prefab;

    [Tooltip("Đường dẫn tới object thân tàu trong prefab (vd. 'model/Mesh' hoặc chỉ tên 'Mesh'). " +
             "Để trống: object có RuntimeMeshFillController.")]
    public string hullPath = "";

    [Tooltip("Scale (world) của object thân tàu, dùng khi không có Prefab.")]
    public Vector3 fallbackHullScale = Vector3.one;

    [Header("Mask")]
    public Texture2D mask;
    [Range(0f, 1f)] public float alphaThreshold = 0.5f;

    [Tooltip("Độ mịn viền lỗ. 6 là cân bằng; mỗi bậc tăng ~gấp đôi số đỉnh.")]
    [Range(4, 10)] public int edgeDetail = 6;

    [Tooltip("Số lần thử lại (đổi góc xoay / dịch nhẹ) khi 1 lỗ không tạo được vùng lấp kín.")]
    [Range(0, 10)] public int maxRetries = 5;

    [Header("Lỗ cắt tay (ghi tự động từ Mesh Editor)")]
    public List<ManualHole> manualHoles = new List<ManualHole>();

    [Header("Nhóm lỗ theo bố cục")]
    public List<HoleGroup> groups = new List<HoleGroup>();

    [Header("Đầu ra")]
    [Tooltip("Mesh hiển thị (gán vào MeshFilter). Để trống sẽ tạo mới cạnh file cấu hình.")]
    public Mesh displayOutput;

    [Tooltip("Mesh nguồn cho RuntimeMeshFillController (có thêm các nhóm không hiển thị). " +
             "Để trống: nếu mọi nhóm đều hiển thị thì dùng chung Display Output.")]
    public Mesh sourceOutput;

    [Tooltip("Sau khi bake: gán MeshFilter + Source Mesh trên prefab.")]
    public bool assignToPrefab = true;

    [HideInInspector] public string lastBakeReport = "";
}
