using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class WorldClickHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private InputActionReference clickAction;

    [Header("Settings")]
    [SerializeField] private LayerMask clickableLayers; // chọn cả Boat + Ocean
    [SerializeField] private float maxDistance = 1000f;

    private void OnEnable()
    {
        mainCamera = mainCamera != null ? mainCamera : Camera.main;
        if (clickAction != null)
            clickAction.action.Enable();
        else
            Debug.LogError($"{name}: chưa gán clickAction cho WorldClickHandler.", this);
    }

    private void Update()
    {
        if (!GameController.CanPrepare || clickAction == null)
            return;
        // Action "Click" là PassThrough: WasPerformedThisFrame() đúng cả lúc nhấn lẫn lúc thả -> click bị xử lý 2 lần.
        // Chỉ xử lý lúc nhấn.
        if (!clickAction.action.WasPressedThisFrame())
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // Pointer.current = chuột, bút hoặc màn hình cảm ứng (thiết bị vừa dùng gần nhất).
        // Mouse.current luôn null trên điện thoại không có chuột.
        Pointer pointer = Pointer.current;
        if (pointer == null || mainCamera == null)
            return;

        Ray ray = mainCamera.ScreenPointToRay(pointer.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, clickableLayers,
                            QueryTriggerInteraction.Collide))
        {
            IClickable clickable = hit.collider.GetComponentInParent<IClickable>();
            clickable?.OnClicked(hit);
        }
    }
}