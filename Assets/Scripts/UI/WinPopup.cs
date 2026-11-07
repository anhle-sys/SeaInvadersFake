using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class WinPopup : MonoBehaviour
{
    [SerializeField] private Button nextBtn;
    public void Init()
    {
        nextBtn.onClick.RemoveAllListeners();
        nextBtn.onClick.AddListener(() =>
        {
            GameController.Instance.LoadScene();
            Hide();
        });
    }
    public void Show()
    {
        gameObject.SetActive(true);
        nextBtn.gameObject.SetActive(true);
    }
    public void Hide()
    {
        gameObject.SetActive(false);
        nextBtn.gameObject.SetActive(false);
    }
}
