// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Android;
using osu.Framework.Configuration;
using osu.Framework.Logging;

namespace osu.Android
{
    public class OsuAndroidGameHost : AndroidGameHost
    {
        public OsuAndroidGameHost(AndroidGameActivity activity)
            : base(activity)
        {
        }

        protected override void ChooseAndSetupRenderer()
        {
            var configRenderer = Config.GetBindable<RendererType>(FrameworkSetting.Renderer);

            if (isVulkan(configRenderer.Value) && !AndroidVulkanSupport.SupportsPersistentSurface)
            {
                Logger.Log($"🖼️ {configRenderer.Value} renderer requires Android 14 or newer. Reverting renderer selection to automatic.", level: LogLevel.Important);
                configRenderer.Value = RendererType.Automatic;
            }

            if (configRenderer.Value != RendererType.Automatic || !AndroidVulkanSupport.PreferredByDefault)
            {
                base.ChooseAndSetupRenderer();
                return;
            }

            if (!AndroidVulkanSupport.AutomaticSelectionAllowed)
            {
                Logger.Log("🖼️ Vulkan previously failed on this device. Not using it for the automatic renderer.", level: LogLevel.Important);
                base.ChooseAndSetupRenderer();
                return;
            }

            Logger.Log("🖼️ Device is suitable for Vulkan, attempting to use it for the automatic renderer.");

            // Recorded before initialising, so that crashes during initialisation are also accounted for.
            AndroidVulkanSupport.BeginStartup();

            // The framework only lists the legacy GL renderer for Android, so try Vulkan first by temporarily presenting it as the user's choice.
            // If it fails to initialise, the framework resets the setting and falls back to GL by itself.
            configRenderer.Value = RendererType.Deferred_Vulkan;
            base.ChooseAndSetupRenderer();

            // Keep the stored setting as automatic, so that settings show "Automatic (Deferred Vulkan)" and future logic changes still apply.
            configRenderer.Value = RendererType.Automatic;

            if (!isVulkan(ResolvedRenderer))
            {
                // A failed Vulkan initialisation may leave the surface in a state where even the GL fallback fails, so don't risk it again.
                Logger.Log("🖼️ Vulkan failed to initialise, and will not be used for the automatic renderer until the game is updated.", level: LogLevel.Important);
                AndroidVulkanSupport.DisableAutomaticSelection();
            }
        }

        private static bool isVulkan(RendererType renderer) => renderer == RendererType.Vulkan || renderer == RendererType.Deferred_Vulkan;
    }
}
