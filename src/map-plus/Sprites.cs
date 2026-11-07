using UnityEngine;

namespace QbanRambo.MapPlus;

internal static class Sprites
{
  internal static Sprite Create(int size, Color32[] pixels)
  {
    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
    {
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
      hideFlags = HideFlags.DontSave
    };
    texture.SetPixels32(pixels);
    texture.Apply();
    var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    sprite.hideFlags = HideFlags.DontSave;
    return sprite;
  }
}
