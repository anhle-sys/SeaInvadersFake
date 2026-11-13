using UnityEngine;

public static class TextureMaskProcessor
{
    // Cache mask theo texture: tránh GetPixels32() (cấp phát hàng MB) mỗi lần đạn trúng.
    private static readonly System.Collections.Generic.Dictionary<Texture2D, MaskData> Cache =
        new System.Collections.Generic.Dictionary<Texture2D, MaskData>();

    public sealed class MaskData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;
        public readonly float Threshold;

        public MaskData(int width, int height, Color32[] pixels, float threshold)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
            Threshold = threshold;
        }

        public bool IsDamaged(int x, int y)
        {
            x = Mathf.Clamp(x, 0, Width - 1);
            y = Mathf.Clamp(y, 0, Height - 1);

            Color32 c = Pixels[y * Width + x];

            float brightness = (c.r + c.g + c.b) / (3f * 255f);
            float alpha = c.a / 255f;
            return brightness * alpha >= Threshold;
        }
    }

    public static bool TryCreate(
        Texture2D texture,
        float threshold,
        out MaskData maskData)
    {
        maskData = null;

        if (texture == null)
            return false;

        if (!texture.isReadable)
        {
            Debug.LogError(
                "Destruction mask is not readable. Enable Read/Write in the texture import settings.");
            return false;
        }

        if (Cache.TryGetValue(texture, out MaskData cached) && cached != null
            && Mathf.Approximately(cached.Threshold, Mathf.Clamp01(threshold)))
        {
            maskData = cached;
            return true;
        }

        try
        {
            Color32[] pixels = texture.GetPixels32();

            if (pixels == null || pixels.Length != texture.width * texture.height)
                return false;

            maskData = new MaskData(
                texture.width,
                texture.height,
                pixels,
                Mathf.Clamp01(threshold));

            Cache[texture] = maskData;
            return true;
        }
        catch (UnityException exception)
        {
            Debug.LogError("Could not read destruction mask: " + exception.Message);
            return false;
        }
    }
}
