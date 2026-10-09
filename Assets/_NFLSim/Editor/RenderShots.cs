using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NFLSim.Editor
{
    /// <summary>
    /// Headless flat-render pass. Generates the Redzone scene, enters play mode,
    /// steps Broadcast → Wipe → PreSnap → Live, and renders PNGs from fixed
    /// camera positions into ~/workspace/unity-renders/.
    /// Run: Unity -batchmode -projectPath . -executeMethod NFLSim.Editor.RenderShots.CaptureAll
    /// (no -nographics: rendering is the point).
    /// </summary>
    [InitializeOnLoad]
    public static class RenderShots
    {
        const string OutDir = "/home/hatch/workspace/unity-renders";
        const string SessionKey = "NFLSim_Rendering";

        // Static ctor runs on editor load AND after every domain reload, so the
        // playModeStateChanged subscription survives the reload that entering
        // play mode triggers (it wipes ALL editor delegates subscribed beforehand).
        static RenderShots()
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
                // Recreate the render camera: the domain reload wiped the edit-mode
                // GameObject and the static reference.
                var camGo = new GameObject("RenderCam");
                renderCam = camGo.AddComponent<Camera>();
                renderCam.enabled = false; // manual Camera.Render() only
                renderCam.fieldOfView = 90f;
                renderCam.clearFlags = CameraClearFlags.Skybox;
                renderCam.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
                EditorApplication.update += Tick;
                bootTime = (float)EditorApplication.timeSinceStartup;
                Debug.Log("RenderShots: play mode entered, tick subscribed.");
            }
        }

        static int stage;
        static float stageStart;
        static float bootTime;
        static Camera renderCam;
        static ExperienceDirector director;

        public static void CaptureAll()
        {
            Directory.CreateDirectory(OutDir);
            try
            {
                SetupNFLSim.BuildScene();
                EditorSceneManager.OpenScene("Assets/_NFLSim/Scenes/RedzoneExperience.unity");
            }
            catch (Exception e)
            {
                Debug.LogError($"RenderShots: scene setup failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            // Render camera is created in OnPlayModeChanged (after the domain reload).

            stage = 0;
            stageStart = 0f;
            // Flag survives the play-mode domain reload (SessionState is editor-side).
            // The [InitializeOnLoad] static ctor re-subscribes playModeStateChanged
            // after the reload and picks this up.
            SessionState.SetBool(SessionKey, true);
            EditorApplication.EnterPlaymode();
            Debug.Log("RenderShots: entering play mode.");
        }

        static bool xrDisabled;

        // Headless: OVRPlugin DLL absent; OVRManager/MRUKGlobalContext would spam
        // DllNotFoundException every frame. Must run AFTER play mode starts — entering
        // play mode reloads the scene from disk, wiping edit-mode SetActive(false).
        static void DisableXrRig()
        {
            var rig = GameObject.Find("OVRCameraRig");
            if (rig != null) rig.SetActive(false);
            foreach (var mb in GameObject.FindObjectsOfType<MonoBehaviour>(true))
            {
                var t = mb.GetType();
                if (t.FullName == "Meta.XR.MRUtilityKit.MRUKGlobalContext" ||
                    t.FullName == "OVRManager")
                    mb.gameObject.SetActive(false);
            }
            Debug.Log("RenderShots: XR/MR objects disabled for headless pass.");
        }

        static void Tick()
        {
            try
            {
                float now = (float)EditorApplication.timeSinceStartup;
                if (now - bootTime > 300f) { Fail("timeout waiting for play-mode stages"); return; }

                if (director == null)
                {
                    director = ExperienceDirector.Instance;
                    if (director == null) return; // still booting
                    if (!xrDisabled) { DisableXrRig(); xrDisabled = true; }
                }

                switch (stage)
                {
                    case 0: // settle, then broadcast shot
                        if (director.State == MomentState.Broadcast && Time.timeSinceLevelLoad > 2f)
                        {
                            WideShot();
                            QbSpotShot("01_broadcast");
                            Debug.Log("RenderShots: broadcast captured.");
                            director.TakeTheField();
                            stage = 1; stageStart = now;
                        }
                        break;
                    case 1: // wait for the wipe to clear into PreSnap
                        if (director.State == MomentState.PreSnap)
                        {
                            QbSpotShot("02_presnap");
                            Debug.Log("RenderShots: presnap captured.");
                            director.Snap();
                            stage = 2; stageStart = now;
                        }
                        else if (now - stageStart > 20f) { Fail("wipe never reached PreSnap"); return; }
                        break;
                    case 2: // live play in motion
                        if (now - stageStart > 2.0f)
                        {
                            QbSpotShot("03_live");
                            Debug.Log("RenderShots: live captured.");
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
            Debug.LogError("RenderShots: " + msg);
            Cleanup();
            EditorApplication.Exit(1);
        }

        static void Done()
        {
            Cleanup();
            Debug.Log("RenderShots: all shots captured, exiting.");
            EditorApplication.Exit(0);
        }

        static void Cleanup()
        {
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        // ---------- shots ----------
        static float U => SimConfig.UnitsPerYard;

        static void WideShot()
        {
            // high behind the play, looking at the field — checks the stadium build
            renderCam.transform.position = new Vector3(0f, 22f, 20f);
            renderCam.transform.LookAt(new Vector3(0f, 0f, 50f * U));
            Snap("00_wide");
        }

        static void QbSpotShot(string name)
        {
            // the intended player eye: at the QB spot, 1.6 m up, facing downfield (+z)
            float qbZ = Mathf.Max(director.CurrentScenario.ballOn - 5f, 1f) * U;
            renderCam.transform.position = new Vector3(0f, 1.6f, qbZ);
            renderCam.transform.rotation = Quaternion.identity; // faces +z, downfield
            Snap(name);
        }

        static void Snap(string name)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            renderCam.targetTexture = rt;
            renderCam.Render();
            renderCam.targetTexture = null;

            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string path = Path.Combine(OutDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"RenderShots: wrote {path}");

            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
