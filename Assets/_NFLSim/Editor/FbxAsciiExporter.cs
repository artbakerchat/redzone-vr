using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace NFLSim.Editor
{
    /// <summary>
    /// Minimal FBX 7.4 ASCII exporter with zero package dependencies.
    /// (UPM tarball installs are broken on this VM, so com.unity.formats.fbx
    /// can never be added — this exists instead.)
    ///
    /// Exports: MeshFilter + baked SkinnedMeshRenderer geometry, diffuse-color
    /// materials, the full node hierarchy (TRS), and cameras.
    /// Convention: Unity (meters, left-handed, Y-up) -> FBX (centimeters,
    /// right-handed, Y-up): positions/normals (x, y, -z); triangle winding is
    /// flipped iff Unity's native winding is CCW-from-outside (determined
    /// empirically from the first mesh); node rotations via matrix conjugation
    /// M' = F*M*F then XYZ-euler extraction (R = Rx*Ry*Rz).
    /// </summary>
    public static class FbxAsciiExporter
    {
        const float Cm = 100f;

        static long _nextId;
        static StringBuilder _objects;
        static StringBuilder _connects;
        static int _modelCount, _geoCount, _matCount, _attrCount;
        static Dictionary<Mesh, long> _geoIds;
        static Dictionary<Material, long> _matIds;
        static bool? _swapWinding;

        public static void ExportScene(string filePath, GameObject[] roots)
        {
            _nextId = 1000;
            _objects = new StringBuilder(1 << 20);
            _connects = new StringBuilder(1 << 16);
            _modelCount = _geoCount = _matCount = _attrCount = 0;
            _geoIds = new Dictionary<Mesh, long>();
            _matIds = new Dictionary<Material, long>();
            _swapWinding = null;

            foreach (var r in roots)
                if (r.activeInHierarchy) WriteNode(r.transform, 0);

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var w = new StreamWriter(filePath, false, Encoding.ASCII))
            {
                w.WriteLine("; FBX 7.4.0 project file");
                w.WriteLine("; Created by NFLSim.Editor.FbxAsciiExporter");
                w.WriteLine();
                WriteHeader(w);
                WriteDefinitions(w);
                w.WriteLine("Objects:  {");
                w.Write(_objects.ToString());
                w.WriteLine("}");
                w.WriteLine("Connects:  {");
                w.Write(_connects.ToString());
                w.WriteLine("}");
                w.WriteLine("Takes:  {");
                w.WriteLine("}");
            }
            var info = new FileInfo(filePath);
            Debug.Log($"FbxAsciiExporter: wrote {filePath} ({info.Length / 1024} KB; " +
                      $"{_modelCount} models, {_geoCount} geometries, {_matCount} materials, swapWinding={_swapWinding})");
        }

        // ---------------- nodes ----------------
        static void WriteNode(Transform t, long parentId)
        {
            long modelId = _nextId++;
            _modelCount++;
            bool isCamera = t.GetComponent<Camera>() != null;

            Vector3 p = t.localPosition;
            Vector3 e = FlipEuler(t.localRotation);
            Vector3 s = t.localScale;

            Mesh mesh = null;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
            if (mesh == null)
            {
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null)
                {
                    mesh = new Mesh();
                    smr.BakeMesh(mesh);
                }
            }

            long geoId = 0;
            if (mesh != null && mesh.GetTriangles(0).Length > 0)
            {
                if (!_geoIds.TryGetValue(mesh, out geoId))
                {
                    geoId = _nextId++;
                    _geoCount++;
                    WriteGeometry(geoId, mesh, t.name);
                    _geoIds[mesh] = geoId;
                }
            }

            long matId = 0;
            if (geoId != 0)
            {
                var rend = t.GetComponent<Renderer>();
                Material m = (rend != null && rend.sharedMaterials.Length > 0) ? rend.sharedMaterials[0] : null;
                if (m != null)
                {
                    if (!_matIds.TryGetValue(m, out matId))
                    {
                        matId = _nextId++;
                        _matCount++;
                        WriteMaterial(matId, m);
                        _matIds[m] = matId;
                    }
                }
            }

            string nodeType = isCamera ? "Camera" : "Mesh";
            _objects.Append($"    Model: {modelId}, \"Model::{San(t.name)}\", \"{nodeType}\" {{\n");
            _objects.Append("        Version: 232\n");
            _objects.Append("        Properties70:  {\n");
            _objects.Append($"            P: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{F(p.x * Cm)},{F(p.y * Cm)},{F(-p.z * Cm)}\n");
            _objects.Append($"            P: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",{F(e.x)},{F(e.y)},{F(e.z)}\n");
            _objects.Append($"            P: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",{F(s.x)},{F(s.y)},{F(s.z)}\n");
            _objects.Append("        }\n");
            _objects.Append("        Shading: Y\n");
            _objects.Append("        Culling: \"CullingOff\"\n");
            _objects.Append("    }\n");
            _connects.Append($"        C: \"OO\",{modelId},{parentId}\n");
            if (geoId != 0) _connects.Append($"        C: \"OO\",{geoId},{modelId}\n");
            if (matId != 0) _connects.Append($"        C: \"OO\",{matId},{modelId}\n");

            if (isCamera)
            {
                long attrId = _nextId++;
                _attrCount++;
                _objects.Append($"    NodeAttribute: {attrId}, \"NodeAttribute::{San(t.name)}\", \"Camera\" {{\n");
                _objects.Append("        TypeFlags: \"Camera\"\n");
                _objects.Append("    }\n");
                _connects.Append($"        C: \"OO\",{attrId},{modelId}\n");
            }

            foreach (Transform c in t)
                if (c.gameObject.activeSelf) WriteNode(c, modelId);
        }

        /// <summary>
        /// Unity quaternion -> FBX XYZ euler (degrees), via M' = F*M*F with
        /// F = diag(1,1,-1) (left-handed -> right-handed), R = Rx*Ry*Rz.
        /// </summary>
        static Vector3 FlipEuler(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float m00 = 1 - 2 * (y * y + z * z);
            float m01 = 2 * (x * y - z * w);
            float m02 = 2 * (x * z + y * w);
            float m10 = 2 * (x * y + z * w);
            float m11 = 1 - 2 * (x * x + z * z);
            float m12 = 2 * (y * z - x * w);
            float m20 = 2 * (x * z - y * w);
            float m21 = 2 * (y * z + x * w);
            float m22 = 1 - 2 * (x * x + y * y);
            // conjugate: negate row 2 and column 2
            m02 = -m02; m12 = -m12; m20 = -m20; m21 = -m21;
            float ey = Mathf.Asin(Mathf.Clamp(m02, -1f, 1f)) * Mathf.Rad2Deg;
            float ex = Mathf.Atan2(-m12, m22) * Mathf.Rad2Deg;
            float ez = Mathf.Atan2(-m01, m00) * Mathf.Rad2Deg;
            return new Vector3(ex, ey, ez);
        }

        // ---------------- geometry ----------------
        static void WriteGeometry(long id, Mesh mesh, string name)
        {
            int[] tris = mesh.GetTriangles(0);
            Vector3[] v = mesh.vertices;
            Vector3[] n = mesh.normals;
            Vector2[] uv = mesh.uv;
            bool hasN = n != null && n.Length == v.Length;
            bool hasUv = uv != null && uv.Length == v.Length;

            if (_swapWinding == null)
            {
                _swapWinding = ComputeSwapWinding(tris, v, n, hasN);
                Debug.Log($"FbxAsciiExporter: swapWinding={_swapWinding} (from mesh '{name}')");
            }
            bool swap = _swapWinding.Value;

            int corners = tris.Length;
            _objects.Append($"    Geometry: {id}, \"Geometry::{San(name)}\", \"Mesh\" {{\n");

            _objects.Append($"        Vertices: *{corners * 3} {{\n            a: ");
            for (int k = 0; k < corners; k++)
            {
                Vector3 p = v[tris[k]];
                _objects.Append($"{F(p.x * Cm)},{F(p.y * Cm)},{F(-p.z * Cm)}");
                if (k < corners - 1) _objects.Append(",");
            }
            _objects.Append("\n        }\n");

            _objects.Append($"        PolygonVertexIndex: *{corners} {{\n            a: ");
            for (int t = 0; t < corners / 3; t++)
            {
                int a = 3 * t, b = 3 * t + 1, c = 3 * t + 2;
                _objects.Append(swap ? $"{a},{c},{-b}" : $"{a},{b},{-c}");
                if (t < corners / 3 - 1) _objects.Append(",");
            }
            _objects.Append("\n        }\n");
            _objects.Append("        GeometryVersion: 124\n");

            _objects.Append("        LayerElementNormal: 0 {\n");
            _objects.Append("            MappingInformationType: \"ByPolygonVertex\"\n");
            _objects.Append("            ReferenceInformationType: \"Direct\"\n");
            _objects.Append($"            Normals: *{corners * 3} {{\n                a: ");
            for (int k = 0; k < corners; k++)
            {
                Vector3 nn = hasN ? n[tris[k]] : Vector3.up;
                _objects.Append($"{F(nn.x)},{F(nn.y)},{F(-nn.z)}");
                if (k < corners - 1) _objects.Append(",");
            }
            _objects.Append("\n            }\n        }\n");

            _objects.Append("        LayerElementUV: 0 {\n");
            _objects.Append("            MappingInformationType: \"ByPolygonVertex\"\n");
            _objects.Append("            ReferenceInformationType: \"Direct\"\n");
            _objects.Append($"            UV: *{corners * 2} {{\n                a: ");
            for (int k = 0; k < corners; k++)
            {
                Vector2 t2 = hasUv ? uv[tris[k]] : Vector2.zero;
                _objects.Append($"{F(t2.x)},{F(t2.y)}");
                if (k < corners - 1) _objects.Append(",");
            }
            _objects.Append("\n            }\n        }\n");

            _objects.Append("        Layer: 0 {\n");
            _objects.Append("            Version: 100\n");
            _objects.Append("            LayerElement:  { \n                Type: \"LayerElementNormal\",\n                TypedIndex: 0\n            }\n");
            _objects.Append("            LayerElement:  { \n                Type: \"LayerElementUV\",\n                TypedIndex: 0\n            }\n");
            _objects.Append("        }\n");
            _objects.Append("    }\n");
        }

        /// <summary>
        /// Determines whether Unity's native triangle winding is CCW when viewed
        /// from outside (in which case the z-mirror turns it CW and indices must
        /// be swapped for a correct right-handed FBX).
        /// </summary>
        static bool ComputeSwapWinding(int[] tris, Vector3[] v, Vector3[] n, bool hasN)
        {
            if (!hasN || tris.Length < 3) return false;
            Vector3 a = v[tris[0]], b = v[tris[1]], c = v[tris[2]];
            Vector3 f = Vector3.Cross(b - a, c - a);
            if (f.sqrMagnitude < 1e-12f) return false;
            Vector3 nn = (n[tris[0]] + n[tris[1]] + n[tris[2]]).normalized;
            return Vector3.Dot(f, nn) > 0f;
        }

        // ---------------- material ----------------
        static void WriteMaterial(long id, Material m)
        {
            Color c = m.HasProperty("_Color") ? m.color : Color.white;
            _objects.Append($"    Material: {id}, \"Material::{San(m.name)}\", \"\" {{\n");
            _objects.Append("        Version: 102\n");
            _objects.Append("        ShadingModel: \"phong\"\n");
            _objects.Append("        MultiLayer: 0\n");
            _objects.Append("        Properties70:  {\n");
            _objects.Append($"            P: \"DiffuseColor\", \"Color\", \"\", \"A\",{F(c.r)},{F(c.g)},{F(c.b)}\n");
            _objects.Append($"            P: \"AmbientColor\", \"Color\", \"\", \"A\",{F(c.r)},{F(c.g)},{F(c.b)}\n");
            _objects.Append("            P: \"Diffuse\", \"Number\", \"\", \"A\",1\n");
            _objects.Append("            P: \"Ambient\", \"Number\", \"\", \"A\",1\n");
            _objects.Append("        }\n");
            _objects.Append("    }\n");
        }

        // ---------------- file framing ----------------
        static void WriteHeader(StreamWriter w)
        {
            var now = DateTime.Now;
            w.WriteLine("FBXHeaderExtension:  {");
            w.WriteLine("    FBXHeaderVersion: 1003");
            w.WriteLine("    FBXVersion: 7400");
            w.WriteLine("    CreationTimeStamp:  {");
            w.WriteLine("        Version: 1000");
            w.WriteLine($"        Year: {now.Year}");
            w.WriteLine($"        Month: {now.Month}");
            w.WriteLine($"        Day: {now.Day}");
            w.WriteLine($"        Hour: {now.Hour}");
            w.WriteLine($"        Minute: {now.Minute}");
            w.WriteLine($"        Second: {now.Second}");
            w.WriteLine("        Millisecond: 0");
            w.WriteLine("    }");
            w.WriteLine("    Creator: \"NFLSim FBX ASCII Exporter\"");
            w.WriteLine("}");
            w.WriteLine("GlobalSettings:  {");
            w.WriteLine("    Version: 1000");
            w.WriteLine("    Properties70:  {");
            w.WriteLine("        P: \"UpAxis\", \"int\", \"Integer\", \"\", 1");
            w.WriteLine("        P: \"UpAxisSign\", \"int\", \"Integer\", \"\", 1");
            w.WriteLine("        P: \"FrontAxis\", \"int\", \"Integer\", \"\", 2");
            w.WriteLine("        P: \"FrontAxisSign\", \"int\", \"Integer\", \"\", 1");
            w.WriteLine("        P: \"CoordAxis\", \"int\", \"Integer\", \"\", 0");
            w.WriteLine("        P: \"CoordAxisSign\", \"int\", \"Integer\", \"\", 1");
            w.WriteLine("        P: \"OriginalUpAxis\", \"int\", \"Integer\", \"\", 1");
            w.WriteLine("        P: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\", 1");
            w.WriteLine("        P: \"UnitScaleFactor\", \"double\", \"Number\", \"\", 1");
            w.WriteLine("        P: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\", 1");
            w.WriteLine("        P: \"AmbientColor\", \"Color\", \"\", \"A\",0,0,0");
            w.WriteLine("        P: \"DefaultCamera\", \"KString\", \"\", \"\", \"Producer Perspective\"");
            w.WriteLine("    }");
            w.WriteLine("}");
        }

        static void WriteDefinitions(StreamWriter w)
        {
            w.WriteLine("Definitions:  {");
            w.WriteLine("    Version: 100");
            w.WriteLine("    Count: 5");
            w.WriteLine("    ObjectType: \"GlobalSettings\" {");
            w.WriteLine("        Count: 1");
            w.WriteLine("    }");
            w.WriteLine("    ObjectType: \"Model\" {");
            w.WriteLine($"        Count: {_modelCount}");
            w.WriteLine("    }");
            w.WriteLine("    ObjectType: \"NodeAttribute\" {");
            w.WriteLine($"        Count: {_attrCount}");
            w.WriteLine("    }");
            w.WriteLine("    ObjectType: \"Geometry\" {");
            w.WriteLine($"        Count: {_geoCount}");
            w.WriteLine("    }");
            w.WriteLine("    ObjectType: \"Material\" {");
            w.WriteLine($"        Count: {_matCount}");
            w.WriteLine("    }");
            w.WriteLine("}");
        }

        // ---------------- helpers ----------------
        static string San(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            return sb.Length == 0 ? "unnamed" : sb.ToString();
        }

        static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);
    }
}
