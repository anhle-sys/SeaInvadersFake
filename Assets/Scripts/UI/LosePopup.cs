using UnityEngine;
using UnityEngine.UI;

public class LosePopup : MonoBehaviour
{
    [SerializeField] private Button retryButton;

    public void BindButtons()
    {
        retryButton.onClick.RemoveAllListeners();
        // Không Hide: popup giữ nguyên trong lúc màn tối dần, scene mới sẽ tự dựng lại
        retryButton.onClick.AddListener(() => GameController.Instance.LoadScene());
    }

    public void Show()
    {
        gameObject.SetActive(true);
        retryButton.gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        retryButton.gameObject.SetActive(false);
    }
}
