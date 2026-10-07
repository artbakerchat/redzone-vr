using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace NFLSim.Editor
{
    /// <summary>
    /// One-click project setup. In Unity: NFLSim → Build Experience Scene,
    /// then NFLSim → Apply Quest Build Settings.
    /// </summary>
    public static class SetupNFLSim
    {
        const string ScenePath = "Assets/_NFLSim/Scenes/RedzoneExperience.unity";

        [MenuItem("NFLSim/Build Experience Scene")]
        public static void BuildScene()
        {
            EnsureFolder("Assets/_NFLSim");
            EnsureFolder("Assets/_NFLSim/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var root = new GameObject("Redzone");
            var dir = root.AddComponent<ExperienceDirector>();

            var field = new GameObject("Field").AddComponent<FieldBuilder>();
            field.transform.SetParent(root.transform);

            var presenter = new GameObject("BroadcastPresenter").AddComponent<BroadcastPresenter>();
            presenter.transform.SetParent(root.transform);

            float U = SimConfig.UnitsPerYard;

            // QB lines up 5 yards behind the ball; the first scenario is on the 8.
            var qbGo = new GameObject("QB");
            qbGo.transform.SetParent(root.transform);
            qbGo.transform.position = new Vector3(0f, 0f, 87f * U);
            var qb = qbGo.AddComponent<QBController>();
            dir.qb = qb;

            // football: brown spheroid + stripes + trail
            var ballGo = new GameObject("Football");
            ballGo.transform.SetParent(root.transform);
            ballGo.transform.position = new Vector3(0f, 1.2f, 20f * U);
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mesh.name = "BallMesh";
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.SetParent(ballGo.transform);
            mesh.transform.localScale = new Vector3(0.18f, 0.18f, 0.3f);
            mesh.GetComponent<Renderer>().material = Lit(new Color(0.35f, 0.2f, 0.1f));
            foreach (float sz in new float[] { -0.09f, 0.09f })
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
                stripe.transform.SetParent(ballGo.transform);
                stripe.transform.localScale = new Vector3(0.185f, 0.04f, 0.035f);
                stripe.transform.localPosition = new Vector3(0f, 0.05f, sz);
                stripe.GetComponent<Renderer>().material = Lit(Color.white);
            }
            var col = ballGo.AddComponent<SphereCollider>();
            col.radius = 0.16f;
            var rb = ballGo.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            ballGo.AddComponent<XRGrabInteractable>();
            var trail = ballGo.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.startWidth = 0.05f;
            trail.endWidth = 0.005f;
            trail.material = Lit(new Color(1f, 0.9f, 0.7f));
            var football = ballGo.AddComponent<Football>();
            dir.ball = football;

            // receivers (white) and defenders (black)
            var offMat = Lit(new Color(0.92f, 0.92f, 0.92f));
            var defMat = Lit(new Color(0.08f, 0.08f, 0.08f));
            string[] ids = { "X", "Z", "S", "T" };
            for (int i = 0; i < 4; i++)
            {
                var p = MakePlayer($"WR_{ids[i]}", offMat, root.transform, U);
                var rr = p.AddComponent<RouteRunner>();
                rr.receiverId = ids[i];
                rr.CatchPoint = AddAnchor(p.transform, "CatchPoint", new Vector3(0f, 1.25f, 0f));
                dir.receivers.Add(rr);
            }
            for (int i = 0; i < 5; i++)
            {
                var p = MakePlayer($"DB_{i + 1}", defMat, root.transform, U);
                var d = p.AddComponent<DefenderAI>();
                d.InterceptPoint = AddAnchor(p.transform, "InterceptPoint", new Vector3(0f, 1.25f, 0f));
                dir.defenders.Add(d);
            }

            dir.field = field;
            dir.presenter = presenter;

            // reuse the default camera as a desktop preview cam at the QB spot
            var cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.name = "PreviewCamera (disable when XR rig is added)";
                cam.transform.position = new Vector3(0f, 1.6f, 87f * U);
                cam.transform.rotation = Quaternion.identity; // faces +z, downfield
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"<b>Redzone:</b> experience scene built at {ScenePath}\n" +
                      "Next: add an XR rig (OVRCameraRig or XR Origin), disable PreviewCamera, press Play.");
        }

        [MenuItem("NFLSim/Apply Quest Build Settings")]
        public static void ApplyQuestSettings()
        {
            PlayerSettings.companyName = "Marley";
            PlayerSettings.productName = "NFLSim VR";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.artbakerchat.redzonevr");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log("<b>NFLSim:</b> Quest build settings applied (IL2CPP, ARM64, min SDK 32, com.artbakerchat.redzonevr).\n" +
                      "Still manual: Project Settings → XR Plug-in Management → Android → check Oculus, " +
                      "then add your Meta XR SDK rig to the scene.");
        }

        // ---------- helpers ----------
        static GameObject MakePlayer(string name, Material mat, Transform parent, float U)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            p.name = name;
            Object.DestroyImmediate(p.GetComponent<Collider>()); // catch logic is radius-based
            p.transform.SetParent(parent);
            p.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f); // ~1.8 m
            p.transform.position = new Vector3(0f, 0.9f, 25f * U);
            p.GetComponent<Renderer>().material = mat;
            return p;
        }

        static Transform AddAnchor(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent);
            t.localPosition = localPos;
            return t;
        }

        static Material Lit(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { color = c };
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
