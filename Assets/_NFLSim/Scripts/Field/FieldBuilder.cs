using UnityEngine;

namespace NFLSim
{
    /// <summary>
    /// Builds the whole stadium field procedurally at runtime — no binary assets.
    /// Regulation 120 x 53.3 yards, drawn at SimConfig.UnitsPerYard.
    /// Team colors are configurable here (defaults: black & gold).
    /// </summary>
    public class FieldBuilder : MonoBehaviour
    {
        [Header("Team colors")]
        public Color homePrimary = new Color(0.02f, 0.02f, 0.02f);
        public Color homeAccent = new Color(1f, 0.71f, 0.09f);
        public Color grass = new Color(0.13f, 0.42f, 0.18f);
        public Color lineWhite = new Color(0.95f, 0.95f, 0.95f);

        void Awake() => Build();

        static Material Mat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            return m;
        }

        void Build()
        {
            float U = SimConfig.UnitsPerYard;
            float L = SimConfig.FieldLengthYards * U;
            float W = SimConfig.FieldWidthYards * U;
            float zc = 60f * U; // field center (0..120 yd)

            var grassMat = Mat(grass);
            var whiteMat = Mat(lineWhite);
            var darkMat = Mat(new Color(0.05f, 0.06f, 0.08f));
            var homeMat = Mat(homePrimary);
            var yellowMat = Mat(new Color(1f, 0.85f, 0.1f));

            // surrounding ground
            var surround = GameObject.CreatePrimitive(PrimitiveType.Plane);
            surround.name = "Surround";
            surround.transform.SetParent(transform);
            surround.transform.localScale = new Vector3(40f, 1f, 40f);
            surround.transform.position = new Vector3(0f, -0.05f, zc);
            surround.GetComponent<Renderer>().material = darkMat;

            // playing surface
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "FieldGround";
            ground.transform.SetParent(transform);
            ground.transform.localScale = new Vector3(W / 10f, 1f, L / 10f);
            ground.transform.position = new Vector3(0f, 0f, zc);
            ground.GetComponent<Renderer>().material = grassMat;

            // end zones (0..10 and 110..120)
            Box(10f * U, 0.02f, W, new Vector3(0f, 0.005f, 5f * U), homeMat);
            Box(10f * U, 0.02f, W, new Vector3(0f, 0.005f, 115f * U), homeMat);
            // accent strips at the goal lines
            Box(0.3f * U, 0.03f, W, new Vector3(0f, 0.008f, 10f * U), Mat(homeAccent));
            Box(0.3f * U, 0.03f, W, new Vector3(0f, 0.008f, 110f * U), Mat(homeAccent));

            // yard lines every yard, heavier every 5
            for (int i = 0; i <= 100; i++)
            {
                float thick = (i % 5 == 0) ? 0.18f : 0.1f;
                var line = Box(W, 0.02f, thick, new Vector3(0f, 0.011f, (10f + i) * U), whiteMat);
                line.name = $"YardLine_{i}";
            }

            // sideline border
            float b = 0.3f;
            Box(W + b * 2f, 0.02f, b, new Vector3(0f, 0.011f, 10f * U - b / 2f), whiteMat).name = "Border_N";
            Box(W + b * 2f, 0.02f, b, new Vector3(0f, 0.011f, 110f * U + b / 2f), whiteMat).name = "Border_S";
            Box(b, 0.02f, 100f * U, new Vector3(-W / 2f - b / 2f, 0.011f, 60f * U), whiteMat).name = "Border_W";
            Box(b, 0.02f, 100f * U, new Vector3(W / 2f + b / 2f, 0.011f, 60f * U), whiteMat).name = "Border_E";

            // goalposts at the back of each end zone
            Goalpost(0f, yellowMat);
            Goalpost(100f, yellowMat);
        }

        GameObject Box(float sx, float sy, float sz, Vector3 pos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(sx, sy, sz);
            go.transform.position = pos;
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        void Goalpost(float yardLine, Material mat)
        {
            float U = SimConfig.UnitsPerYard;
            var root = new GameObject($"Goalpost_{yardLine:0}");
            root.transform.SetParent(transform);
            float z = (10f + yardLine) * U;

            // support
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.transform.SetParent(root.transform);
            post.transform.localScale = new Vector3(0.12f, 1.6f, 0.12f);
            post.transform.position = new Vector3(0f, 1.6f, z);

            // crossbar (18.5 ft = 6.17 yd wide, 10 ft high)
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bar.transform.SetParent(root.transform);
            bar.transform.localScale = new Vector3(0.09f, 6.17f * U, 0.09f);
            bar.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            bar.transform.position = new Vector3(0f, 3.05f, z);

            // uprights
            foreach (float sx in new float[] { -3.08f * U, 3.08f * U })
            {
                var up = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                up.transform.SetParent(root.transform);
                up.transform.localScale = new Vector3(0.09f, 3f, 0.09f);
                up.transform.position = new Vector3(sx, 3.05f + 3f, z);
                up.GetComponent<Renderer>().material = mat;
            }
            post.GetComponent<Renderer>().material = mat;
            bar.GetComponent<Renderer>().material = mat;
        }
    }
}
