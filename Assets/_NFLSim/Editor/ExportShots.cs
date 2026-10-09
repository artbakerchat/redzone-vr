using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NFLSim.Editor
{
    /// <summary>
    /// Headless FBX export pass. Generates the Redzone scene, enters play mode,
    /// steps Broadcast → Wipe → PreSnap → Live, and exports the scene to
    /// 00_wide / 01_broadcast / 02_presnap / 03_live .fbx in ~/workspace/unity-fbx/
    /// via the dependency-free FbxAsciiExporter. Also writes refs_&lt;name&gt;.txt
    /// (world transforms of probe objects) for the Validate pass.
    /// Run: Unity -batchmode -projectPath . -executeMethod NFLSim.Editor.ExportShots.ExportAll
    /// (wrapped in xvfb-run on headless machines).
    /// </summary>
    [InitializeOnLoad]
    public static class ExportShots
    {
        const string OutDir = "/home/hatch/workspace/unity-fbx";
        const string SessionKey = "NFLSim_Exporting";

        // Static ctor runs on editor load AND after every domain reload, so the
        // playModeStateChanged subscription is always fresh. Entering play mode
        // triggers a domain reload that wipes ALL editor delegates subscribed
        // beforehand — subscribing here is the only subscription that survives.
        static ExportShots()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool(SessionKey, false))
            {
                SessionState.SetBool(SessionKey, false); // one-shot
                // Recreate the shot camera: the domain reload wiped the edit-mode
                // GameObject and the static reference.
                var camGo = new GameObject("ShotCam");
                shotCam = camGo.AddComponent<Camera>();
                shotCam.enabled = false;
                EditorApplication.update += Tick;
                bootTime = (float)EditorApplication.timeSinceStartup;
                Debug.Log("ExportShots: play mode entered, tick subscribed.");
            }
        }

        static int stage;
        static float stageStart;
        static float bootTime;
        static Camera shotCam;
        static ExperienceDirector director;

        public static void ExportAll()
        {
            Directory.CreateDirectory(OutDir);
            try
            {
                SetupNFLSim.BuildScene();
                EditorSceneManager.OpenScene("Assets/_NFLSim/Scenes/RedzoneExperience.unity");
            }
            catch (Exception e)
            {
                Debug.LogError($"ExportShots: scene setup failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            // ShotCam is created in OnPlayModeChanged (after the domain reload).

            stage = 0;
            stageStart = 0f;
            // Flag survives the play-mode domain reload (SessionState is editor-side).
            // The [InitializeOnLoad] static ctor re-subscribes playModeStateChanged
            // after the reload and picks this up.
            SessionState.SetBool(SessionKey, true);
            EditorApplication.EnterPlaymode();
            Debug.Log("ExportShots: entering play mode.");
        }

        static bool xrDisabled;

        /// <summary>
        /// Deactivates XR/MR components AFTER play mode starts. Must run in play mode:
        /// entering play mode reloads the scene from disk, wiping any edit-mode
        /// SetActive(false). The native OVRPlugin can't load without a headset runtime,
        /// and per-frame polling (OVRManager, MRUKGlobalContext.Update →
        /// IsOpenXRAvailable) throws DllNotFoundException hundreds of times per second,
        /// flooding the log and stalling the director. The export uses its own ShotCam;
        /// the director is rig-independent.
        /// </summary>
        static void DisableXrRig()
        {
            int n = 0;
            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null) { rig.SetActive(false); n++; }
            // MRUK global context polls OVRPlugin.initialized every frame
            foreach (var mb in GameObject.FindObjectsOfType<MonoBehaviour>(true))
            {
                var t = mb.GetType();
                if (t.FullName == "Meta.XR.MRUtilityKit.MRUKGlobalContext" ||
                    t.FullName == "OVRManager")
                {
                    mb.gameObject.SetActive(false); n++;
                }
            }
            Debug.Log($"ExportShots: disabled {n} XR/MR objects for headless pass.");
        }

        static void Tick()
        {
            try
            {
                float now = (float)EditorApplication.timeSinceStartup;
                if (now - bootTime > 600f) { Fail("timeout waiting for play-mode stages"); return; }

                if (director == null)
                {
                    director = ExperienceDirector.Instance;
                    if (director == null) return; // still booting
                    if (!xrDisabled) { DisableXrRig(); xrDisabled = true; }
                }

                switch (stage)
                {
                    case 0: // settle, then wide + broadcast exports
                        if (director.State == MomentState.Broadcast && Time.timeSinceLevelLoad > 2f)
                        {
                            WidePose();
                            ExportFbx("00_wide");
                            QbSpotPose();
                            ExportFbx("01_broadcast");
                            Debug.Log("ExportShots: wide + broadcast exported.");
                            director.TakeTheField();
                            stage = 1; stageStart = now;
                        }
                        break;
                    case 1: // wait for the wipe to clear into PreSnap
                        if (director.State == MomentState.PreSnap)
                        {
                            QbSpotPose();
                            ExportFbx("02_presnap");
                            Debug.Log("ExportShots: presnap exported.");
                            director.Snap();
                            stage = 2; stageStart = now;
                        }
                        else if (now - stageStart > 30f) { Fail("wipe never reached PreSnap"); return; }
                        break;
                    case 2: // live play in motion
                        if (now - stageStart > 2.0f)
                        {
                            QbSpotPose();
                            ExportFbx("03_live");
                            Debug.Log("ExportShots: live exported.");
                            Done();
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Fail("exception in Tick: " + e);
            }
        }

        static void Fail(string msg)
        {
            Debug.LogError("ExportShots: " + msg);
            Cleanup();
            EditorApplication.Exit(1);
        }

        static void Done()
        {
            Cleanup();
            Debug.Log("ExportShots: all FBX files exported, exiting.");
            EditorApplication.Exit(0);
        }

        static void Cleanup()
        {
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        // ---------- camera poses (mirror RenderShots) ----------
        static float U => SimConfig.UnitsPerYard;

        static void WidePose()
        {
            shotCam.transform.position = new Vector3(0f, 22f, 20f);
            shotCam.transform.LookAt(new Vector3(0f, 0f, 50f * U));
        }

        static void QbSpotPose()
        {
            float qbZ = Mathf.Max(director.CurrentScenario.ballOn - 5f, 1f) * U;
            shotCam.transform.position = new Vector3(0f, 1.6f, qbZ);
            shotCam.transform.rotation = Quaternion.identity; // faces +z, downfield
        }

        // ---------- export + refs ----------
        static void ExportFbx(string name)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            string path = Path.Combine(OutDir, name + ".fbx");
            if (File.Exists(path)) File.Delete(path);
            FbxAsciiExporter.ExportScene(path, roots);
            WriteRefs(name);
        }

        /// <summary>
        /// World-space transforms (meters, Unity convention) of probe objects,
        /// for the Validate pass to compare against the re-imported FBX.
        /// </summary>
        static void WriteRefs(string name)
        {
            var sb = new StringBuilder();
            Probe(sb, "QB");
            Probe(sb, "Football");
            Probe(sb, "WR_X");
            Probe(sb, "ShotCam");
            // crossbar: has a real rotation (z=90°), good rotation probe
            var gp = GameObject.Find("Redzone/Field/Goalpost_0");
            if (gp != null)
            {
                foreach (Transform c in gp.transform)
                {
                    float z = c.localEulerAngles.z;
                    if (Mathf.Abs(Mathf.DeltaAngle(z, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(z, 270f)) < 1f)
                    {
                        AppendProbe(sb, "Crossbar", c);
                        break;
                    }
                }
            }
            File.WriteAllText(Path.Combine(OutDir, "refs_" + name + ".txt"), sb.ToString());
            Debug.Log($"ExportShots: wrote refs_{name}.txt");
        }

        static void Probe(StringBuilder sb, string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go == null) { Debug.LogWarning($"ExportShots: probe '{objectName}' not found"); return; }
            AppendProbe(sb, objectName, go.transform);
        }

        static void AppendProbe(StringBuilder sb, string label, Transform t)
        {
            Vector3 p = t.position;
            Quaternion q = t.rotation;
            sb.AppendLine($"{label}={R(p.x)},{R(p.y)},{R(p.z)}|{R(q.x)},{R(q.y)},{R(q.z)},{R(q.w)}");
        }

        static string R(float v) => v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);

        // ---------- validation (separate edit-mode run) ----------
        /// <summary>
        /// Run: -executeMethod NFLSim.Editor.ExportShots.Validate
        /// Re-imports each FBX from ~/workspace/unity-fbx/ and checks mesh counts
        /// plus probe transforms of 00_wide against refs_00_wide.txt.
        /// </summary>
        public static void Validate()
        {
            const string checkDir = "Assets/_NFLSim/ImportCheck";
            try
            {
                if (AssetDatabase.IsValidFolder(checkDir))
                    AssetDatabase.DeleteAsset(checkDir);
                AssetDatabase.CreateFolder("Assets/_NFLSim", "ImportCheck");

                var files = Directory.GetFiles(OutDir, "*.fbx");
                if (files.Length == 0) { Debug.LogError("Validate: no FBX files found"); EditorApplication.Exit(1); return; }

                foreach (var fbx in files)
                {
                    string dest = checkDir + "/" + Path.GetFileName(fbx);
                    File.Copy(fbx, dest, true);
                    AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dest);
                    if (prefab == null) { Debug.LogError($"Validate: failed to load {dest}"); continue; }
                    int meshCount = prefab.GetComponentsInChildren<MeshFilter>(true).Length;
                    Debug.Log($"Validate: {Path.GetFileName(fbx)} imported OK, {meshCount} MeshFilters");
                }

                // deep check on 00_wide: probe transforms vs refs
                var wide = AssetDatabase.LoadAssetAtPath<GameObject>(checkDir + "/00_wide.fbx");
                if (wide != null) CheckProbes(wide, "00_wide");

                AssetDatabase.DeleteAsset(checkDir);
                Debug.Log("Validate: done.");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"Validate failed: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void CheckProbes(GameObject prefabRoot, string name)
        {
            string refsPath = Path.Combine(OutDir, "refs_" + name + ".txt");
            if (!File.Exists(refsPath)) { Debug.LogWarning("Validate: no refs file"); return; }
            foreach (var line in File.ReadAllLines(refsPath))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                var vals = parts[1].Split('|');
                var pp = vals[0].Split(',');
                var qq = vals[1].Split(',');
                var expected = new Vector3(Parse(pp[0]), Parse(pp[1]), Parse(pp[2]));
                var expectedQ = new Quaternion(Parse(qq[0]), Parse(qq[1]), Parse(qq[2]), Parse(qq[3]));

                var found = FindDeep(prefabRoot.transform, parts[0]);
                if (found == null) { Debug.LogWarning($"Validate: probe '{parts[0]}' not found in import"); continue; }

                float pd = Vector3.Distance(found.localPosition, expected);
                float qd = Quaternion.Angle(found.localRotation, expectedQ);
                string verdict = (pd < 0.01f && qd < 1f) ? "OK" : "MISMATCH";
                Debug.Log($"Validate: probe {parts[0]}: posErr={pd:F4}m rotErr={qd:F2}deg -> {verdict}");
            }

            // geometry spot check: FieldGround vertex 0 round-trips exactly
            var fg = FindDeep(prefabRoot.transform, "FieldGround");
            if (fg != null)
            {
                var mf = fg.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
                    Debug.Log($"Validate: FieldGround imported verts={mf.sharedMesh.vertexCount}");
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static float Parse(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    }
}
