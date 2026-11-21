using UnityEngine;
using UnityEngine.UI;

public class WinPopup : MonoBehaviour
{
    [SerializeField] private Button nextButton;

    public void BindButtons()
    {
        nextButton.onClick.RemoveAllListeners();
        // Không Hide: popup giữ nguyên trong lúc màn tối dần, scene mới sẽ tự dựng lại
        nextButton.onClick.AddListener(() => GameController.Instance.LoadScene());
    }

    public void Show()
    {
        gameObject.SetActive(true);
        nextButton.gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        nextButton.gameObject.SetActive(false);
    }
}
