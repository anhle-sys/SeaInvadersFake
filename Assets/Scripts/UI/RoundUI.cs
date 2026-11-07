using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Thanh round của battle (nhóm "Top"): mỗi ô con là 1 round.
/// Số ô = số round của một battle. Đã thắng / đang đánh / chưa tới hiển thị khác màu.
/// </summary>
public class RoundUI : MonoBehaviour
{
    [Tooltip("Các ô round theo thứ tự. Để trống thì tự lấy Image của các con trực tiếp")]
    [SerializeField] private Image[] rounds;

    [SerializeField] private Color doneColor = new(0.35f, 1f, 0f, 1f);
    [SerializeField] private Color currentColor = new(1f, 0.85f, 0.1f, 1f);
    [SerializeField] private Color upcomingColor = new(0.55f, 0.55f, 0.55f, 0.8f);
    [SerializeField] private float currentScale = 1.15f;

    public int RoundCount => Rounds.Length;

    private Image[] Rounds
    {
        get
        {
            if (rounds == null || rounds.Length == 0)
            {
                rounds = new Image[transform.childCount];
                for (int i = 0; i < rounds.Length; i++)
                    rounds[i] = transform.GetChild(i).GetComponent<Image>();
            }
            return rounds;
        }
    }

    public void Refresh()
    {
        Image[] cells = Rounds;
        for (int i = 0; i < cells.Length; i++)
        {
            Image cell = cells[i];
            if (cell == null)
                continue;

            int round = i + 1;
            bool done = round <= GameData.CompletedRounds;
            bool current = !done && GameData.IsBattleActive && round == GameData.CurrentRound;

            cell.color = done ? doneColor : current ? currentColor : upcomingColor;
            cell.transform.localScale = Vector3.one * (current ? currentScale : 1f);

            TMP_Text label = cell.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = round.ToString();
        }
    }
}
