using UnityEngine;
using UnityEngine.InputSystem;

public class RaisePopup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private InputActionReference clickAction;
    [SerializeField] private PurchasePopup popupController;

    [Header("Settings")]
    [SerializeField] private LayerMask oceanLayer;

    public System.Action<Vector3> OnOceanClicked;

    private void OnEnable()
    {
        mainCamera = mainCamera != null ? mainCamera : Camera.main;
        clickAction.action.performed += OnClick;
        clickAction.action.Enable();
    }

    private void OnDisable()
    {
        clickAction.action.performed -= OnClick;
        clickAction.action.Disable();
    }

    private void OnClick(InputAction.CallbackContext context)
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();

        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            1000f,
            oceanLayer))
        {
            popupController.Show();
        }
    }
}