using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace NFLSim.Editor
{
    /// <summary>
    /// Headless build entry points for CI / Unity Build Automation.
    ///
    /// Two methods:
    ///   BuildAndroid() — complete build for `-executeMethod` CI usage
    ///       (scene generation + Quest settings + OpenXR loader + APK).
    ///       Run: Unity -batchmode -quit -projectPath . -executeMethod NFLSim.Editor.CloudBuildEntry.BuildAndroid
    ///
    ///   PreExport() — Unity Build Automation pre-export hook. Set it as the
    ///       "Pre-Export Method" in the build configuration's Advanced settings
    ///       (value: NFLSim.Editor.CloudBuildEntry.PreExport). UBA calls it in
    ///       the Editor context after script compilation and before exporting
    ///       the player; UBA itself performs the export afterwards, using the
    ///       scene list from the build configuration's "Scenes" setting.
    ///       Do NOT use BuildAndroid as the pre-export method — it builds the
    ///       player itself, which would build twice.
    /// </summary>
    public static class CloudBuildEntry
    {
        const string ScenePath = "Assets/_NFLSim/Scenes/RedzoneExperience.unity";
        const string ApkPath = "Builds/redzone-vr.apk";
        const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        /// <summary>Full headless build: scene + settings + XR loader + APK.</summary>
        public static void BuildAndroid()
        {
            PrepareProject();

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;
            Debug.Log($"CloudBuildEntry: build result={summary.result}, size={summary.totalSize} bytes -> {ApkPath}");
            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"CloudBuildEntry: player build failed ({summary.result}). See build log.");
        }

        /// <summary>
        /// Unity Build Automation pre-export hook: prepares the project
        /// (generates the scene, applies Quest settings, enables the OpenXR
        /// loader). UBA exports the player itself afterwards.
        /// </summary>
        public static void PreExport()
        {
            PrepareProject();
            AssetDatabase.Refresh();
            Debug.Log("CloudBuildEntry: PreExport complete — project ready for UBA export.");
        }

        // ---------- shared preparation ----------

        static void PrepareProject()
        {
            SetupNFLSim.BuildScene();        // generates Assets/_NFLSim/Scenes/RedzoneExperience.unity
            SetupNFLSim.ApplyQuestSettings(); // IL2CPP, ARM64, min SDK 32, bundle id, Android target
            EnsureSceneInBuildSettings();
            EnsureOpenXRLoader();
        }

        static void EnsureSceneInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath))
                return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"CloudBuildEntry: added {ScenePath} to Editor build settings.");
        }

        /// <summary>
        /// Enables the OpenXR loader for Android in XR Plug-in Management,
        /// creating the XR settings container if it does not exist yet.
        /// Warns and returns false on failure — the build continues so a
        /// missing/broken XR setup never silently blocks an APK.
        /// </summary>
        static bool EnsureOpenXRLoader()
        {
            try
            {
                var container = FindOrCreateXRSettingsContainer();
                if (container == null)
                {
                    Debug.LogWarning("CloudBuildEntry: no XR settings container; skipping OpenXR loader setup.");
                    return false;
                }
                if (!container.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                    container.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
                if (!container.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                    container.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

                XRManagerSettings manager = container.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                if (manager == null)
                {
                    Debug.LogWarning("CloudBuildEntry: no XR manager settings for Android; skipping OpenXR loader setup.");
                    return false;
                }

                bool ok = XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderTypeName, BuildTargetGroup.Android);
                Debug.Log(ok
                    ? "CloudBuildEntry: OpenXR loader enabled for Android (XR Plug-in Management)."
                    : "CloudBuildEntry: WARNING — OpenXR loader could not be assigned. " +
                      "Is com.unity.xr.openxr resolved? Run the Meta XR Project Setup Tool locally to finish XR setup.");
                return ok;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"CloudBuildEntry: OpenXR loader setup failed ({e.GetType().Name}: {e.Message}); continuing without XR loader.");
                return false;
            }
        }

        static XRGeneralSettingsPerBuildTarget FindOrCreateXRSettingsContainer()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget"))
            {
                var existing = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null)
                    return existing;
            }

            var created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            const string dir = "Assets/_NFLSim/XR";
            EnsureAssetFolder("Assets/_NFLSim");
            EnsureAssetFolder(dir);
            string path = dir + "/XRGeneralSettingsPerBuildTarget.asset";
            AssetDatabase.CreateAsset(created, path);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
            AssetDatabase.SaveAssets();
            Debug.Log($"CloudBuildEntry: created XR settings container at {path}.");
            return created;
        }

        static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int slash = path.LastIndexOf('/');
            EnsureAssetFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
