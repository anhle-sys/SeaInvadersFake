using UnityEngine;

public class BoatClickable : MonoBehaviour, IClickable
{
    [Tooltip("Dữ liệu dự phòng. Nếu tàu có BoatAIController thì dùng data mà tàu thực sự được khởi tạo với.")]
    [SerializeField] private BoatData boatData;
    [SerializeField] private BoatDataEventChannel channel; // kéo asset BoatClicked vào

    private BoatAIController ai;

    private void Awake() => ai = GetComponent<BoatAIController>();

public void OnClicked(RaycastHit hit)
    {
        if (channel == null)
            return;

        BoatData data = ai != null && ai.Data != null ? ai.Data : boatData;
        if (data != null)
            channel.Raise(new BoatSelection(data, transform, ai != null ? ai.InstanceId : null));
    }
}
