using UnityEngine;

public readonly struct BoatSelection
{
    public readonly BoatData Data;
    public readonly Transform Boat;
    public readonly string InstanceId;   // id riêng của con tàu được chọn (nâng cấp theo từng tàu)

    public BoatSelection(BoatData data, Transform boat, string instanceId = null)
    {
        Data = data;
        Boat = boat;
        InstanceId = instanceId;
    }
}
[CreateAssetMenu(menuName = "Events/BoatData Channel", fileName = "BoatClicked")]
public class BoatDataEventChannel : EventChannel<BoatSelection> { }
