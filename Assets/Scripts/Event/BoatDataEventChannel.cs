using UnityEngine;

/// <summary>Tàu người chơi vừa click: dữ liệu + transform của chính con tàu đó (để camera/VFX biết vị trí).</summary>
public readonly struct BoatSelection
{
    public readonly BoatData Data;
    public readonly Transform Boat;

    public BoatSelection(BoatData data, Transform boat)
    {
        Data = data;
        Boat = boat;
    }
}

// Giữ tên class cũ để asset "BoatClicked" (cùng script GUID) vẫn dùng được, chỉ đổi kiểu dữ liệu gửi đi.
[CreateAssetMenu(menuName = "Events/BoatData Channel", fileName = "BoatClicked")]
public class BoatDataEventChannel : EventChannel<BoatSelection> { }
