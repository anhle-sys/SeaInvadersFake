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
        clickAction.action.Enable();
    }

    private void Update()
    {
        if(GameController.Instance == null || GameController.Instance.IsBattleStarted)
            return;
        if (!clickAction.action.WasPerformedThisFrame())
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Mouse.current == null)
            return;

        Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, clickableLayers,
                            QueryTriggerInteraction.Collide))
        {
            IClickable clickable = hit.collider.GetComponentInParent<IClickable>();
            clickable?.OnClicked(hit);
        }
    }
}