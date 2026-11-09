using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PurchaseCostText : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private BoatData data;

    private void OnEnable() => Refresh();

    public void Refresh()
    {
        if (data == null || costText == null)
            return;

        costText.text = data.cost.ToString();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (button == null)
            button = GetComponentInParent<Button>();
        if (costText == null)
            costText = GetComponentInChildren<TMP_Text>();

        TryFillDataFromButton();
    }

    [ContextMenu("Lấy data từ listener của Button")]
    private void TryFillDataFromButton()
    {
        if (button == null)
            return;

        var so = new UnityEditor.SerializedObject(button);
        var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        for (int i = 0; i < calls.arraySize; i++)
        {
            var call = calls.GetArrayElementAtIndex(i);
            var arg = call.FindPropertyRelative("m_Arguments.m_ObjectArgument");
            if (arg != null && arg.objectReferenceValue is BoatData found)
            {
                data = found;
                return;
            }
        }
    }
#endif
}