// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace osu.Android
{
    /// <summary>
    /// Device capability checks which decide whether the Vulkan renderer may be used on the current device.
    /// </summary>
    public static class AndroidVulkanSupport
    {
        /// <summary>
        /// <c>VK_API_VERSION_1_3</c>, as expected by <see cref="PackageManager.FeatureVulkanHardwareVersion"/>.
        /// </summary>
        private const int vulkan_1_3 = 0x403000;

        /// <summary>
        /// The number of consecutive Vulkan sessions which never got confirmed (see <see cref="ConfirmStartup"/>),
        /// after which Vulkan is no longer chosen automatically.
        /// </summary>
        private const int max_unconfirmed_startups = 2;

        private const string preferences_name = "renderer";
        private const string unconfirmed_startups_key = "vulkan_unconfirmed_startups";
        private const string package_update_time_key = "package_update_time";

        /// <summary>
        /// Whether the game surface can be kept alive while the activity is in the background.
        /// </summary>
        /// <remarks>
        /// The Vulkan swapchain is bound to the native window it was created with, and the renderer has no way of recreating it.
        /// Before Android 14 (where <see cref="global::Android.Views.SurfaceView.SetSurfaceLifecycle"/> was added) the surface is always destroyed
        /// when the activity is hidden, which would break the swapchain on resume.
        /// </remarks>
        [SupportedOSPlatformGuard("android34.0")]
        public static bool SupportsPersistentSurface => OperatingSystem.IsAndroidVersionAtLeast(34);

        /// <summary>
        /// Whether Vulkan should be used when the renderer is set to "Automatic".
        /// </summary>
        /// <remarks>
        /// This is limited to Android 14+ on Qualcomm SoCs with a Vulkan 1.3 capable driver (such as Snapdragon 8 Gen 1 and newer, including Snapdragon 8 Elite),
        /// where Adreno drivers are known to be mature. Other devices keep using the legacy GL renderer by default.
        /// </remarks>
        public static bool PreferredByDefault { get; } = checkPreferredByDefault();

        /// <summary>
        /// Whether Vulkan may be chosen for the automatic renderer, which stops being the case after it either failed to initialise,
        /// or recent Vulkan sessions repeatedly ended before being confirmed (e.g. due to a driver crash, or the user closing a black screen).
        /// </summary>
        /// <remarks>
        /// This is reset whenever the app is updated or reinstalled.
        /// </remarks>
        public static bool AutomaticSelectionAllowed
        {
            get
            {
                var prefs = preferences;

                if (prefs == null)
                    return true;

                long packageUpdateTime = getPackageUpdateTime();

                if (prefs.GetLong(package_update_time_key, 0) != packageUpdateTime)
                {
                    prefs.Edit()?.PutLong(package_update_time_key, packageUpdateTime)?.PutInt(unconfirmed_startups_key, 0)?.Commit();
                    return true;
                }

                return prefs.GetInt(unconfirmed_startups_key, 0) < max_unconfirmed_startups;
            }
        }

        private static bool startupPending;

        /// <summary>
        /// Records that the game is about to start up with a Vulkan renderer.
        /// Must be followed by <see cref="ConfirmStartup"/> once the game has been running successfully for a while.
        /// </summary>
        public static void BeginStartup()
        {
            unconfirmedStartups++;
            startupPending = true;
        }

        /// <summary>
        /// Records that the game has been running successfully since <see cref="BeginStartup"/>.
        /// </summary>
        public static void ConfirmStartup()
        {
            if (!startupPending)
                return;

            startupPending = false;
            unconfirmedStartups = 0;
        }

        /// <summary>
        /// Stops Vulkan from being chosen for the automatic renderer until the app is updated or reinstalled.
        /// </summary>
        public static void DisableAutomaticSelection()
        {
            startupPending = false;
            unconfirmedStartups = max_unconfirmed_startups;
        }

        private static int unconfirmedStartups
        {
            get => preferences?.GetInt(unconfirmed_startups_key, 0) ?? 0;
            // Committed synchronously, as the process may crash right after.
            set => preferences?.Edit()?.PutInt(unconfirmed_startups_key, value)?.Commit();
        }

        private static ISharedPreferences? preferences => Application.Context.GetSharedPreferences(preferences_name, FileCreationMode.Private);

        private static long getPackageUpdateTime()
        {
            var context = Application.Context;
            return context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.LastUpdateTime ?? 0;
        }

        private static bool checkPreferredByDefault()
        {
            if (!SupportsPersistentSurface)
                return false;

            string manufacturer = Build.SocManufacturer ?? string.Empty;

            if (!manufacturer.Equals("QTI", StringComparison.OrdinalIgnoreCase) && !manufacturer.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase))
                return false;

            return Application.Context.PackageManager?.HasSystemFeature(PackageManager.FeatureVulkanHardwareVersion, vulkan_1_3) == true;
        }
    }
}
