using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// App icon (incl. Android adaptive icon) and startup screen from Assets/FreeVelo/Branding.
    /// Applied automatically once per editor session when the Player Settings don't use them yet;
    /// menu FreeVelo → Apply app icon and startup screen applies them again.
    /// The images are rendered by tools/branding/render.js.
    /// </summary>
    [InitializeOnLoad]
    public static class FreeVeloBranding
    {
        private const string Folder = "Assets/FreeVelo/Branding/";
        private const string IconPath = Folder + "icon.png";
        private const string AdaptiveBackgroundPath = Folder + "icon_adaptive_bg.png";
        private const string AdaptiveForegroundPath = Folder + "icon_adaptive_fg.png";
        private const string SplashLogoPath = Folder + "splash_logo.png";
        private const string SessionKey = "FreeVelo.BrandingChecked";

        private static readonly Color Background = new Color32(0x0C, 0x10, 0x15, 0xFF);

        static FreeVeloBranding()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false)) return;
                SessionState.SetBool(SessionKey, true);
                Apply(force: false);
            };
        }

        [MenuItem("FreeVelo/Apply app icon and startup screen")]
        private static void ApplyFromMenu() => Apply(force: true);

        private static void Apply(bool force)
        {
            ConfigureImporter(IconPath, sprite: false);
            ConfigureImporter(AdaptiveBackgroundPath, sprite: false);
            ConfigureImporter(AdaptiveForegroundPath, sprite: false);
            ConfigureImporter(SplashLogoPath, sprite: true);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBackgroundPath);
            var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveForegroundPath);
            var splash = AssetDatabase.LoadAssetAtPath<Sprite>(SplashLogoPath);
            if (icon == null || background == null || foreground == null || splash == null)
            {
                Debug.LogWarning("FreeVelo: branding images missing in " + Folder);
                return;
            }

            var current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            bool applied = current.Length > 0 && current[0] == icon
                && PlayerSettings.SplashScreen.logos.Any(l => l.logo == splash);
            if (applied && !force) return;

            // Default icon (iOS and everything without its own icons)
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            // Android: adaptive (background + foreground layer), round and legacy icons.
            // Kinds are looked up by name so this compiles without the Android module.
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var platformIcon in icons)
                {
                    if (kind.ToString() == "Adaptive") platformIcon.SetTextures(background, foreground);
                    else platformIcon.SetTextures(icon);
                }

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }

            // Startup screen: FreeVelo logo on the app's dark background.
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, splash) };
            PlayerSettings.SplashScreen.backgroundColor = Background;
            PlayerSettings.SplashScreen.background = null;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.overlayOpacity = 0f;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;

            AssetDatabase.SaveAssets();
            Debug.Log("FreeVelo: app icon and startup screen applied");
        }

        private static void ConfigureImporter(string path, bool sprite)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;

            bool changed = false;
            var type = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
        }
    }
}
