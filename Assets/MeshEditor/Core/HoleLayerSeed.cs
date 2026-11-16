using System;
using UnityEngine;

// 1 lỗ 2 lớp: thân tàu bị cắt bằng mask phóng to (lớp to), mảnh lấp của lỗ đó được
// cắt lại bằng cùng mask ở kích thước gốc để tách lõi bé ra khỏi vành.
[Serializable]
public struct HoleLayerSeed
{
    // Tâm và pháp tuyến của nhát cắt, theo không gian local của mesh thân tàu.
    public Vector3 localPoint;
    public Vector3 localNormal;

    // Kích thước (world, m) của lớp bé.
    public float maskSize;

    // Lớp to = maskSize x bigScale.
    public float bigScale;
    public float projectionDepth;
    public float rotation;
}
