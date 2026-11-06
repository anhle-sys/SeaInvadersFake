
using UnityEngine;
using DG.Tweening;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera cam;

    public Transform idleCamera;
    public Transform battleCamera;
    public Transform upgrateCamera;

    [SerializeField] private float moveDuration = 0.8f;

    private Tween cameraMoveTween;
    private Tween cameraRotateTween;

    public void ChangeCameraByState()
    {
        switch (GameController.Instance.state)
        {
            case GameState.Idle:
                SetCameraPosition(idleCamera);
                break;

            case GameState.Battle:
                SetCameraPosition(battleCamera);
                break;

            case GameState.Upgrade:
                SetCameraPosition(upgrateCamera);
                break;

            case GameState.GameOver:
                break;
        }
    }

    public void SetCameraPosition(Transform target)
    {
        if (target == null || cam == null) return;

        cameraMoveTween?.Kill();
        cameraRotateTween?.Kill();

        cameraMoveTween = cam.transform.DOMove(
            target.position, moveDuration
        ).SetEase(Ease.InOutSine);

        cameraRotateTween = cam.transform.DORotateQuaternion(
            target.rotation, moveDuration
        ).SetEase(Ease.InOutSine);
    }
}
