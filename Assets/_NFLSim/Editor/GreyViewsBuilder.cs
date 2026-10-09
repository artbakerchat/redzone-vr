using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NFLSim.Editor
{
    /// <summary>
    /// Builds five neutral-grey "clay render" scenes (the look the user liked
    /// in the Unity Studio publication): four camera views of the grey field
    /// (Overhead / Endzone / AtTheLine / OverTheShoulder) plus a 3v3 formation
    /// diagram (one lineman per team; the runner takes it to the end zone).
    /// Headless entry points render PNGs and export FBXs.
    ///
    /// Run: Unity -batchmode -projectPath . -executeMethod
    ///      NFLSim.Editor.GreyViewsBuilder.BuildRenderExportAll
    /// (wrapped in xvfb-run on headless machines).
    /// </summary>
    public static class GreyViewsBuilder
    {
        const string SceneDir = "Assets/_NFLSim/GreyViews/Scenes";
        const string PngDir = "/home/hatch/workspace/grey-views/png";
        const string FbxDir = "/home/hatch/workspace/grey-views/fbx";

        static readonly string[] ViewNames =
            { "Overhead", "Endzone", "AtTheLine", "OverTheShoulder", "Formation3v3" };

        static Color Grey(float v) => new Color(v, v, v);

        static Material LitGrey(float v)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { color = Grey(v) };
        }

        // ---------------- entry points ----------------

        [MenuItem("NFLSim/Grey Views/Build + Render + Export All")]
        public static void BuildRenderExportAll()
        {
            BuildAll();
            RenderAll();
            ExportAll();
            Debug.Log("<b>GreyViews:</b> build + render + export complete.");
        }

        public static void BuildAll()
        {
            EnsureFolder("Assets/_NFLSim");
            EnsureFolder("Assets/_NFLSim/GreyViews");
            EnsureFolder(SceneDir);
            BuildFormationScene();
            Debug.Log("<b>GreyViews:</b> 3v3 formation scene built.");
        }

        public static void RenderAll()
        {
            Directory.CreateDirectory(PngDir);
            foreach (var v in ViewNames)
            {
                var scene = EditorSceneManager.OpenScene($"{SceneDir}/Grey_{v}.unity");
                var cam = GameObject.Find("ViewCamera")?.GetComponent<Camera>();
                if (cam == null) { Debug.LogError($"GreyViews: no ViewCamera in Grey_{v}"); continue; }
                var rt = new RenderTexture(1600, 900, 24);
                cam.targetTexture = rt;
                cam.Render();
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                var png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                var path = $"{PngDir}/grey-{ToKebab(v)}.png";
                File.WriteAllBytes(path, png);
                Debug.Log($"<b>GreyViews:</b> rendered {path}");
            }
        }

        public static void ExportAll()
        {
            Directory.CreateDirectory(FbxDir);
            foreach (var v in ViewNames)
            {
                EditorSceneManager.OpenScene($"{SceneDir}/Grey_{v}.unity");
                var roots = new System.Collections.Generic.List<GameObject>();
                foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                    roots.Add(go);
                var path = $"{FbxDir}/grey-{ToKebab(v)}.fbx";
                FbxAsciiExporter.ExportScene(path, roots.ToArray());
                Debug.Log($"<b>GreyViews:</b> exported {path}");
            }
        }

        static string ToKebab(string v)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                bool boundary = i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && char.IsLetter(v[i - 1])));
                if (boundary) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // ---------------- field scenes ----------------

        static void BuildFieldScene(string view, Vector3 camPos, Vector3 camTarget)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GreyField");

            // grey field (reuse FieldBuilder geometry, grey palette)
            FieldBuilder.SuppressAutoBuild = true;
            var fieldGo = new GameObject("Field");
            fieldGo.transform.SetParent(root.transform);
            var fb = fieldGo.AddComponent<FieldBuilder>();
            FieldBuilder.SuppressAutoBuild = false;
            fb.BuildGrey();
            // Bake to a static snapshot: drop the builder component so reopening
            // the scene in the editor never rebuilds (wrong palette / duplicates).
            Object.DestroyImmediate(fb);

            // players: 5 offense (light grey capsules) vs 5 defense (dark grey)
            var offMat = LitGrey(0.80f);
            var defMat = LitGrey(0.35f);
            AddCapsule(root.transform, "QB", offMat, new Vector3(0f, 0.9f, 52.2f));
            AddCapsule(root.transform, "WR_X", offMat, new Vector3(-6f, 0.9f, 54.0f));
            AddCapsule(root.transform, "WR_Z", offMat, new Vector3(-2.5f, 0.9f, 54.6f));
            AddCapsule(root.transform, "WR_S", offMat, new Vector3(2.5f, 0.9f, 54.6f));
            AddCapsule(root.transform, "WR_T", offMat, new Vector3(6f, 0.9f, 54.0f));
            AddCapsule(root.transform, "DB_1", defMat, new Vector3(-5f, 0.9f, 57.0f));
            AddCapsule(root.transform, "DB_2", defMat, new Vector3(-2.5f, 0.9f, 57.6f));
            AddCapsule(root.transform, "DB_3", defMat, new Vector3(0f, 0.9f, 58.0f));
            AddCapsule(root.transform, "DB_4", defMat, new Vector3(2.5f, 0.9f, 57.6f));
            AddCapsule(root.transform, "DB_5", defMat, new Vector3(5f, 0.9f, 57.0f));

            // ball
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.SetParent(root.transform);
            ball.transform.position = new Vector3(0f, 0.35f, 55.2f);
            ball.transform.localScale = new Vector3(0.35f, 0.35f, 0.55f);
            ball.GetComponent<Renderer>().material = LitGrey(0.50f);
            Object.DestroyImmediate(ball.GetComponent<Collider>());

            // simple grey scoreboard above the far goalpost
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Scoreboard";
            board.transform.SetParent(root.transform);
            board.transform.position = new Vector3(0f, 7f, 66.6f);
            board.transform.localScale = new Vector3(9f, 3f, 0.5f);
            board.GetComponent<Renderer>().material = LitGrey(0.30f);
            // scoreboard labels, one facing each way so the text reads
            // correctly from both sides of the field
            AddScoreLabel(root.transform, new Vector3(0f, 7f, 66.3f), 180f);
            AddScoreLabel(root.transform, new Vector3(0f, 7f, 66.9f), 0f);

            AddLight(root.transform);
            AddCamera(root.transform, camPos, camTarget);

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/Grey_{view}.unity");
            Debug.Log($"<b>GreyViews:</b> saved Grey_{view}.unity");
        }

        // ---------------- 3v3 formation scene ----------------

        static void BuildFormationScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Formation3v3");

            // grey ground plane
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            ground.GetComponent<Renderer>().material = LitGrey(0.58f);

            // subtle grid lines every 2 units
            var gridMat = LitGrey(0.70f);
            for (int i = -7; i <= 7; i++)
            {
                var lx = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lx.transform.SetParent(root.transform);
                lx.transform.position = new Vector3(i * 2f, 0.005f, 0f);
                lx.transform.localScale = new Vector3(0.03f, 0.01f, 30f);
                lx.GetComponent<Renderer>().material = gridMat;
                Object.DestroyImmediate(lx.GetComponent<Collider>());
                var lz = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lz.transform.SetParent(root.transform);
                lz.transform.position = new Vector3(0f, 0.005f, i * 2f);
                lz.transform.localScale = new Vector3(30f, 0.01f, 0.03f);
                lz.GetComponent<Renderer>().material = gridMat;
                Object.DestroyImmediate(lz.GetComponent<Collider>());
            }

            // 3v3: one lineman per team; passing only — the skill man runs
            // to the end zone while the QB scrambles right to dodge the sack
            var offMat = LitGrey(0.60f);
            var defMat = LitGrey(0.52f);
            AddSphere(root.transform, "OL", offMat, new Vector3(0f, 0.5f, -2.0f));
            AddSphere(root.transform, "QB", offMat, new Vector3(0f, 0.5f, -4.5f));
            AddSphere(root.transform, "WR", offMat, new Vector3(2.5f, 0.5f, -3.0f));
            AddSphere(root.transform, "DL", defMat, new Vector3(0f, 0.5f, 0.8f));
            AddSphere(root.transform, "Rusher_L", defMat, new Vector3(-2.5f, 0.5f, 0.5f));
            AddSphere(root.transform, "DB_R", defMat, new Vector3(3.0f, 0.5f, 1.5f));

            // QB scrambles sharply right to dodge the left-side sack, then throws
            AddRouteLine(root.transform, new Vector3(0f, 0f, -4.5f), new Vector3(4.0f, 0f, -3.5f));
            // skill man's route to the end zone
            AddRouteLine(root.transform, new Vector3(2.5f, 0f, -3.0f), new Vector3(3.5f, 0f, 10f));

            AddLight(root.transform);
            AddCamera(root.transform, new Vector3(0f, 10f, -9f), new Vector3(0f, 0.3f, 0.5f));

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/Grey_Formation3v3.unity");
            Debug.Log("<b>GreyViews:</b> saved Grey_Formation3v3.unity");
        }

        // ---------------- helpers ----------------

        static void AddCapsule(Transform parent, string name, Material mat, Vector3 pos)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            p.name = name;
            p.transform.SetParent(parent);
            p.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            p.transform.position = pos;
            p.GetComponent<Renderer>().material = mat;
            Object.DestroyImmediate(p.GetComponent<Collider>());
        }

        static void AddSphere(Transform parent, string name, Material mat, Vector3 pos)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = name;
            s.transform.SetParent(parent);
            s.transform.position = pos;
            s.GetComponent<Renderer>().material = mat;
            Object.DestroyImmediate(s.GetComponent<Collider>());
        }

        static void AddScoreLabel(Transform parent, Vector3 pos, float rotY)
        {
            var label = new GameObject("ScoreLabel");
            label.transform.SetParent(parent);
            label.transform.position = pos;
            label.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = "4TH & GOAL";
            tm.fontSize = 64;
            tm.characterSize = 0.08f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Grey(0.88f);
        }

        static void AddRouteLine(Transform parent, Vector3 from, Vector3 to)
        {
            var dir = to - from;
            float len = new Vector3(dir.x, 0f, dir.z).magnitude;
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "RouteLine";
            line.transform.SetParent(parent);
            line.transform.position = new Vector3((from.x + to.x) / 2f, 0.02f, (from.z + to.z) / 2f);
            line.transform.localScale = new Vector3(0.08f, 0.02f, len);
            line.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
            line.GetComponent<Renderer>().material = LitGrey(0.42f);
            Object.DestroyImmediate(line.GetComponent<Collider>());
        }

        static void AddLight(Transform parent)
        {
            var go = new GameObject("Directional Light");
            go.transform.SetParent(parent);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 1f, 1f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = Grey(0.45f);
        }

        static void AddCamera(Transform parent, Vector3 pos, Vector3 target)
        {
            var go = new GameObject("ViewCamera");
            go.transform.SetParent(parent);
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.transform.position = pos;
            cam.transform.LookAt(target);
            cam.fieldOfView = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Grey(0.78f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
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
