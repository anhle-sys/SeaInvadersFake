using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Màn đen mờ dần khi đổi scene. Tự tạo khi game chạy, sống qua các scene (DontDestroyOnLoad).
// Dùng coroutine + unscaled time, không dùng DOTween vì LoadScene có gọi DOTween.KillAll().
public class SceneFader : MonoBehaviour
{
    private const float FadeOutDuration = 0.35f;
    private const float FadeInDuration = 0.45f;

    private static SceneFader instance;

    private CanvasGroup group;
    private Coroutine routine;
    private bool loading;

    public static bool IsFading => instance != null && instance.routine != null;
    public static bool IsLoading => instance != null && instance.loading;

    public static SceneFader Instance
    {
        get
        {
            if (instance == null)
                Create();
            return instance;
        }
    }

    // Mở game: hiện từ màn đen
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Instance.group.alpha = 1f;
        instance.FadeIn();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private static void Create()
    {
        var go = new GameObject("SceneFader");
        DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // luôn nằm trên mọi UI khác
        go.AddComponent<GraphicRaycaster>();

        var image = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        image.transform.SetParent(go.transform, false);
        image.color = Color.black;
        var rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        instance = go.AddComponent<SceneFader>();
        instance.group = go.AddComponent<CanvasGroup>();
        instance.SetAlpha(0f);
    }

    // Tối dần -> onCovered (lưu dữ liệu, dọn tween...) -> load scene -> sáng dần
    public static void LoadScene(string sceneName, System.Action onCovered = null)
    {
        if (IsLoading)
            return;   // chặn bấm 2 lần; còn đang sáng dần thì vẫn cho tối lại ngay từ alpha hiện tại
        SceneFader fader = Instance;
        if (fader.routine != null)
            fader.StopCoroutine(fader.routine);
        fader.loading = true;
        fader.routine = fader.StartCoroutine(fader.LoadRoutine(sceneName, onCovered));
    }

    private void FadeIn()
    {
        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(FadeInRoutine());
    }

    private IEnumerator LoadRoutine(string sceneName, System.Action onCovered)
    {
        yield return Fade(1f, FadeOutDuration);

        onCovered?.Invoke();
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        while (!op.isDone)
            yield return null;

        loading = false;
        yield return null;   // chờ 1 frame cho Start() của scene mới chạy xong, tránh giật khi hiện
        yield return Fade(0f, FadeInDuration);
        routine = null;
    }

    private IEnumerator FadeInRoutine()
    {
        yield return null;
        yield return Fade(0f, FadeInDuration);
        routine = null;
    }

    private IEnumerator Fade(float target, float duration)
    {
        group.blocksRaycasts = true;   // chặn bấm trong lúc chuyển
        float start = group.alpha;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        SetAlpha(target);
    }

    private void SetAlpha(float alpha)
    {
        group.alpha = alpha;
        group.blocksRaycasts = alpha > 0f;
    }
}
