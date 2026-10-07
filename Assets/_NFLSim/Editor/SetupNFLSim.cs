using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.Input.UnityXR;
using UpdateModeFlags = Oculus.Interaction.Input.DataSource<Oculus.Interaction.Input.HandDataAsset>.UpdateModeFlags;

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
            var handGrab = ballGo.AddComponent<HandGrabInteractable>();
            handGrab.InjectRigidbody(rb);
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

            BuildHandRig(root.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"<b>Redzone:</b> experience scene built at {ScenePath} (field, players, ball, hand-tracking rig).");
        }

        /// <summary>
        /// Builds the hand-tracking XR rig in code: Meta OVRCameraRig plus, per hand,
        /// a Unity-XR hand data source feeding an ISDK Hand, wired to a HandGrabInteractor
        /// instantiated from the SDK prefab. This makes the Football grabbable with bare
        /// hands — no controllers, no manual Editor rig setup.
        /// </summary>
        static void BuildHandRig(Transform parent)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            GameObject rig;
            if (rigPrefab != null)
            {
                rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, parent);
                rig.name = "OVRCameraRig";
            }
            else
            {
                Debug.LogWarning("Redzone: OVRCameraRig prefab not found; using fallback camera rig.");
                rig = new GameObject("XRRig");
                rig.transform.SetParent(parent, false);
                var fallbackCam = new GameObject("Camera");
                fallbackCam.transform.SetParent(rig.transform, false);
                fallbackCam.tag = "MainCamera";
                fallbackCam.AddComponent<Camera>();
            }

            // The desktop preview camera is superseded by the XR rig.
            var preview = GameObject.Find("PreviewCamera (disable when XR rig is added)");
            if (preview != null) preview.SetActive(false);

            var cam = rig.GetComponentInChildren<Camera>();
            if (cam == null)
            {
                Debug.LogError("Redzone: no camera in XR rig; hand rig aborted.");
                return;
            }

            var transformer = cam.gameObject.AddComponent<TransformTrackingToWorldTransformer>();
            SetObjectField(transformer, "TrackingSpace", cam.transform);

            var interPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/HandGrabInteractor.prefab");
            if (interPrefab == null)
                Debug.LogError("Redzone: HandGrabInteractor prefab not found; hands will not grab.");

            foreach (Handedness handedness in new[] { Handedness.Left, Handedness.Right })
            {
                var dsGo = new GameObject($"HandDataSource_{handedness}");
                dsGo.transform.SetParent(rig.transform, false);
                var ds = dsGo.AddComponent<FromUnityXRHandDataSource>();
                SetIntField(ds, "_handedness", (int)handedness);
                ds.InjectTrackingToWorldTransformer(transformer);

                var handGo = new GameObject($"Hand_{handedness}");
                handGo.transform.SetParent(rig.transform, false);
                var hand = handGo.AddComponent<Hand>();
                hand.InjectAllDataModifier(UpdateModeFlags.UnityUpdate, ds, ds, false);

                if (interPrefab == null) continue;
                var interGo = (GameObject)PrefabUtility.InstantiatePrefab(interPrefab, rig.transform);
                interGo.name = $"HandGrabInteractor_{handedness}";
                var handRef = interGo.GetComponent<HandRef>();
                if (handRef != null) handRef.InjectHand(hand);
                else Debug.LogError($"Redzone: HandRef missing on HandGrabInteractor prefab ({handedness}).");
            }

            Debug.Log("<b>Redzone:</b> hand-tracking rig built (OVRCameraRig + HandGrabInteractor per hand).");
            BuildBlueGloveVisuals(rig.transform);
        }

        static void BuildBlueGloveVisuals(Transform rig)
        {
            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab");
            if (handPrefab == null)
            {
                Debug.LogWarning("Redzone: OVRHandPrefab not found; hands will be invisible.");
                return;
            }

            // Blue glove material (URP Lit with Standard fallback).
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var gloveMat = new Material(shader);
            gloveMat.name = "BlueGlove";
            gloveMat.color = new Color(0.12f, 0.32f, 0.95f); // vivid blue
            if (gloveMat.HasProperty("_Metallic")) gloveMat.SetFloat("_Metallic", 0.1f);
            if (gloveMat.HasProperty("_Smoothness")) gloveMat.SetFloat("_Smoothness", 0.6f);

            foreach (Handedness handedness in new[] { Handedness.Left, Handedness.Right })
            {
                var hvGo = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, rig);
                hvGo.name = $"BlueGlove_{handedness}";

                var skeleton = hvGo.GetComponent<OVRSkeleton>();
                if (skeleton != null)
                {
                    var so = new SerializedObject(skeleton);
                    var prop = so.FindProperty("_skeletonType");
                    if (prop != null)
                    {
                        prop.intValue = handedness == Handedness.Left ? 0 : 1; // HandLeft=0, HandRight=1
                        so.ApplyModifiedProperties();
                    }
                }
                else Debug.LogWarning($"Redzone: OVRSkeleton missing on BlueGlove_{handedness}.");

                var smr = hvGo.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) smr.sharedMaterial = gloveMat;
                else Debug.LogWarning($"Redzone: no SkinnedMeshRenderer on BlueGlove_{handedness}.");
            }

            Debug.Log("<b>Redzone:</b> blue glove visuals added (OVRHandPrefab per hand).");
        }

        static void SetObjectField(Object obj, string field, Object value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"Redzone: field '{field}' not found on {obj.GetType().Name}.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        static void SetIntField(Object obj, string field, int value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"Redzone: field '{field}' not found on {obj.GetType().Name}.");
                return;
            }
            prop.intValue = value;
            so.ApplyModifiedProperties();
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
