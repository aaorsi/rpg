using UnityEngine;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Player builds can strip built-in UI shaders; runtime-created uGUI uses Resources materials here.
    /// </summary>
    public static class RuntimeUiBuildMaterials
    {
        static Material _spriteImageMaterial;
        static Material _legacyTextMaterial;
        static bool _loggedSpriteMissing;
        static bool _loggedTextMissing;

        public static Material TryGetSpriteImageMaterial()
        {
            if (_spriteImageMaterial != null)
                return _spriteImageMaterial;
            _spriteImageMaterial = Resources.Load<Material>("UI/RpgTitleUi");
            if (_spriteImageMaterial == null && !_loggedSpriteMissing)
            {
                _loggedSpriteMissing = true;
                Debug.LogError(
                    "Missing Resources/UI/RpgTitleUi — runtime Images may render magenta in the player.");
            }
            return _spriteImageMaterial;
        }

        public static Material TryGetLegacyTextMaterial()
        {
            if (_legacyTextMaterial != null)
                return _legacyTextMaterial;
            _legacyTextMaterial = Resources.Load<Material>("UI/RpgUiSimpleText");
            if (_legacyTextMaterial == null && !_loggedTextMissing)
            {
                _loggedTextMissing = true;
                Debug.LogError(
                    "Missing Resources/UI/RpgUiSimpleText — runtime Text may render magenta in the player.");
            }
            return _legacyTextMaterial;
        }

        public static void TryApplySpriteImageMaterial(MaskableGraphic graphic)
        {
            if (graphic == null)
                return;
            var m = TryGetSpriteImageMaterial();
            if (m != null)
                graphic.material = m;
        }

        public static void TryApplyLegacyTextMaterial(Text text)
        {
            if (text == null)
                return;
            var m = TryGetLegacyTextMaterial();
            if (m != null)
                text.material = m;
        }

        public static Font BuiltinUiFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
