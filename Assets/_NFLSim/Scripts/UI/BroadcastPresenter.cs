using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NFLSim
{
    /// <summary>
    /// The broadcast layer: a jumbotron-style scenario card, the signature
    /// wipe transition (broadcast → first-person moment), a minimal in-moment
    /// prompt, and the touchdown splash. Built at runtime — no prefabs needed.
    /// No scoreboard, no play-call board: this is presentation, not a game HUD.
    /// </summary>
    public class BroadcastPresenter : MonoBehaviour
    {
        GameObject broadcastRoot;
        Text cardTitle;
        Text cardSituation;
        Text cardLine;
        Image cardWash;

        GameObject wipeQuad;
        Vector3 wipePos;

        GameObject promptRoot;
        Text promptText;

        GameObject touchdownRoot;
        Text touchdownTitle;
        Text touchdownSub;

        public void BuildRig()
        {
            BuildBroadcastCard();
            BuildWipe();
            BuildPrompt();
            BuildTouchdown();
        }

        // ---------------- broadcast card ----------------
        void BuildBroadcastCard()
        {
            float U = SimConfig.UnitsPerYard;
            var canvas = MakeCanvas("BroadcastCard", new Vector2(1400f, 700f),
                new Vector3(0f, 3.6f, 106f * U), 0.0028f);
            canvas.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // faces the QB
            broadcastRoot = canvas.gameObject;

            cardWash = MakeBar(canvas.transform, "Wash", new Vector2(0f, 300f), new Vector2(1400f, 100f));
            cardTitle = MakeText(canvas.transform, "Title", 110, new Vector2(0f, 140f), new Vector2(1300f, 160f));
            cardSituation = MakeText(canvas.transform, "Situation", 52, new Vector2(0f, 10f), new Vector2(1300f, 90f));
            cardSituation.color = new Color(0.85f, 0.85f, 0.85f);
            cardLine = MakeText(canvas.transform, "Line", 56, new Vector2(0f, -120f), new Vector2(1300f, 120f));
            cardLine.fontStyle = FontStyle.Italic;
            cardLine.color = new Color(1f, 0.9f, 0.6f);
            var hint = MakeText(canvas.transform, "Hint", 40, new Vector2(0f, -260f), new Vector2(1300f, 70f));
            hint.text = "A / Space — take the field";
            hint.color = new Color(0.65f, 0.65f, 0.65f);
        }

        public void ShowBroadcast(RedzoneScenario s)
        {
            if (broadcastRoot == null) return;
            broadcastRoot.SetActive(true);
            Color c = ScenarioColorOf(s);
            cardWash.color = c;
            cardTitle.text = s.title;
            cardTitle.color = c;
            cardSituation.text = s.situation;
            cardLine.text = "\u201C" + s.broadcastLine + "\u201D";
        }

        public void HideBroadcast()
        {
            if (broadcastRoot != null) broadcastRoot.SetActive(false);
        }

        // ---------------- wipe ----------------
        void BuildWipe()
        {
            wipeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wipeQuad.name = "Wipe";
            Destroy(wipeQuad.GetComponent<Collider>());
            wipeQuad.transform.SetParent(transform);
            wipeQuad.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var rend = wipeQuad.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard"));
            wipeQuad.SetActive(false);
        }

        /// <summary>
        /// The signature beat: a broadcast wipe in the scenario color covers
        /// the screen; the world re-dresses behind it (onCovered), then the
        /// wipe clears to reveal the first-person moment.
        /// </summary>
        public void PlayWipe(RedzoneScenario s, Action onCovered)
        {
            float U = SimConfig.UnitsPerYard;
            float qbZ = (s.ballOn - 5f) * U;
            wipePos = new Vector3(0f, 1.6f, qbZ + 0.7f);
            StartCoroutine(WipeRoutine(s, onCovered));
        }

        IEnumerator WipeRoutine(RedzoneScenario s, Action onCovered)
        {
            HideBroadcast();
            HideTouchdown();
            var rend = wipeQuad.GetComponent<Renderer>();
            rend.material.color = ScenarioColorOf(s);
            wipeQuad.SetActive(true);

            Vector3 full = new Vector3(2.6f, 1.8f, 1f);
            float half = SimConfig.WipeDurationS * 0.45f;

            // wipe in: left edge pinned, grows rightward
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / half;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f));
                wipeQuad.transform.localScale = new Vector3(Mathf.Max(full.x * e, 0.001f), full.y, 1f);
                wipeQuad.transform.position = wipePos + new Vector3(-full.x / 2f * (1f - e), 0f, 0f);
                yield return null;
            }

            onCovered?.Invoke(); // the moment is staged behind the cover
            yield return new WaitForSeconds(0.5f);

            // wipe out: right edge pinned, collapses leftward
            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / half;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f));
                wipeQuad.transform.localScale = new Vector3(Mathf.Max(full.x * (1f - e), 0.001f), full.y, 1f);
                wipeQuad.transform.position = wipePos + new Vector3(full.x / 2f * e, 0f, 0f);
                yield return null;
            }
            wipeQuad.SetActive(false);
        }

        // ---------------- in-moment prompt ----------------
        void BuildPrompt()
        {
            float U = SimConfig.UnitsPerYard;
            var canvas = MakeCanvas("MomentPrompt", new Vector2(900f, 120f),
                new Vector3(0f, 2.2f, 100f * U), 0.002f);
            canvas.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            promptRoot = canvas.gameObject;
            promptText = MakeText(canvas.transform, "Prompt", 52, Vector2.zero, new Vector2(880f, 110f));
            promptText.color = new Color(1f, 0.9f, 0.6f);
            promptRoot.SetActive(false);
        }

        public void ShowMomentPrompt(RedzoneScenario s)
        {
            if (promptRoot == null) return;
            promptRoot.SetActive(true);
            string what = s.color == ScenarioColor.Red
                ? "RED — your throw. A / Space to snap."
                : "BLUE — the run is on. A / Space to snap.";
            promptText.text = what;
            promptText.color = ScenarioColorOf(s);
        }

        public void ShowMomentPrompt(string message)
        {
            if (promptRoot == null) return;
            promptRoot.SetActive(true);
            promptText.text = message;
            promptText.color = new Color(1f, 0.9f, 0.6f);
        }

        public void HideMomentPrompt()
        {
            if (promptRoot != null) promptRoot.SetActive(false);
        }

        // ---------------- touchdown ----------------
        void BuildTouchdown()
        {
            float U = SimConfig.UnitsPerYard;
            var canvas = MakeCanvas("Touchdown", new Vector2(1400f, 500f),
                new Vector3(0f, 3.4f, 104f * U), 0.0032f);
            canvas.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            touchdownRoot = canvas.gameObject;
            touchdownTitle = MakeText(canvas.transform, "Title", 170, new Vector2(0f, 80f), new Vector2(1350f, 220f));
            touchdownTitle.text = "TOUCHDOWN";
            touchdownSub = MakeText(canvas.transform, "Sub", 60, new Vector2(0f, -110f), new Vector2(1300f, 100f));
            touchdownSub.color = new Color(0.9f, 0.9f, 0.9f);
            touchdownRoot.SetActive(false);
        }

        public void ShowTouchdown(RedzoneScenario s)
        {
            if (touchdownRoot == null) return;
            HideMomentPrompt();
            Color c = ScenarioColorOf(s);
            touchdownTitle.color = c;
            touchdownSub.text = s.title + " — " + s.situation;
            touchdownRoot.SetActive(true);
        }

        public void HideTouchdown()
        {
            if (touchdownRoot != null) touchdownRoot.SetActive(false);
        }

        // ---------------- helpers ----------------
        static Color ScenarioColorOf(RedzoneScenario s) =>
            s.color == ScenarioColor.Red ? SimConfig.ScenarioRed : SimConfig.ScenarioBlue;

        Canvas MakeCanvas(string name, Vector2 sizePx, Vector3 pos, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = sizePx;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            var bg = new GameObject("BG");
            bg.transform.SetParent(go.transform, false);
            var img = bg.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.72f);
            var brt = img.rectTransform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            return canvas;
        }

        Image MakeBar(Transform parent, string name, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var rt = img.rectTransform;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return img;
        }

        Text MakeText(Transform parent, string name, int size, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            var rt = t.rectTransform;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return t;
        }
    }
}
