using UnityEngine;
using UnityEngine.InputSystem;

namespace NFLSim
{
    /// <summary>
    /// The VR quarterback. Owns the snap input (A button, or Space on desktop
    /// for headset-free testing) and exposes head/hands positions to the sim.
    /// Movement is physical — walk the pocket, no artificial locomotion in v0.1.
    /// A gold ring marks where to stand each play.
    /// </summary>
    public class QBController : MonoBehaviour
    {
        [Header("Input (optional — Space/Tab work without these)")]
        public InputActionReference snapAction;    // A button: confirm play / snap
        public InputActionReference cyclePlayAction; // B button: cycle play

        Camera headCam;
        GameObject spotRing;

        public Vector3 HeadPosition => headCam != null ? headCam.transform.position : transform.position;

        public Vector3 HandsPosition
        {
            get
            {
                if (headCam == null) return transform.position + Vector3.up * 1.2f;
                return headCam.transform.position
                    + headCam.transform.forward * 0.45f
                    + Vector3.down * 0.3f;
            }
        }

        void Awake()
        {
            headCam = Camera.main;
            if (headCam == null) headCam = FindFirstObjectByType<Camera>();

            // gold ring marking the QB spot
            spotRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spotRing.name = "QBSpotRing";
            spotRing.transform.localScale = new Vector3(1.4f, 0.03f, 1.4f);
            var col = spotRing.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = new Color(1f, 0.71f, 0.09f, 0.55f) };
            spotRing.GetComponent<Renderer>().material = m;
        }

        public void SetSpot(Vector3 spot)
        {
            transform.position = spot;
            spotRing.transform.position = new Vector3(spot.x, 0.02f, spot.z);
        }

        void OnEnable()
        {
            if (snapAction != null) snapAction.action.performed += OnSnapPerformed;
            if (cyclePlayAction != null) cyclePlayAction.action.performed += OnCyclePerformed;
        }

        void OnDisable()
        {
            if (snapAction != null) snapAction.action.performed -= OnSnapPerformed;
            if (cyclePlayAction != null) cyclePlayAction.action.performed -= OnCyclePerformed;
        }

        void OnSnapPerformed(InputAction.CallbackContext _) => HandleSnapButton();
        void OnCyclePerformed(InputAction.CallbackContext _) => HandleCycleButton();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame) HandleSnapButton();
        }

        void HandleSnapButton()
        {
            var dir = ExperienceDirector.Instance;
            if (dir == null) return;
            if (dir.State == MomentState.Broadcast) dir.TakeTheField();
            else if (dir.State == MomentState.PreSnap) dir.Snap();
        }
    }
}
