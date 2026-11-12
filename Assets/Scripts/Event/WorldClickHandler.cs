using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class WorldClickHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private InputActionReference clickAction;

    [Header("Settings")]
    [SerializeField] private LayerMask clickableLayers;
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

        if (!clickAction.action.WasPressedThisFrame())
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

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