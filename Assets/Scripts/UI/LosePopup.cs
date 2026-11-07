using UnityEngine;
using UnityEngine.UI;

public class LosePopup : MonoBehaviour
{
    [SerializeField] private Button replayBtn;
    public void Init()
    {
        replayBtn.onClick.RemoveAllListeners();
        replayBtn.onClick.AddListener(() =>
        {
            GameController.Instance.LoadScene();
            Hide();
        });
    }
    public void Show()
    {
        gameObject.SetActive(true);
        replayBtn.gameObject.SetActive(true);
    }
    public void Hide()
    {
        gameObject.SetActive(false);
        replayBtn.gameObject.SetActive(false);
    }
}
