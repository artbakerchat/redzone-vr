# Unity Build Automation — redzone-vr setup notes

How this repo is wired for Unity DevOps Build Automation (Android/Quest),
and what still has to be clicked in the Unity Dashboard. Nothing here is
configured on the dashboard yet — that part needs a human.

## 1. Meta XR SDK via Package Manager (headless-safe)

Meta's NPM registry (`https://npm.developer.oculus.com`) is declared as a
scoped registry in `Packages/manifest.json` (scope `com.meta.xr`). Verified
2026-10-06: registry metadata **and tarball downloads are anonymous**
(HTTP 200, no auth), so Build Automation builders resolve the SDK with no
credentials and no manual `.unitypackage` import.

Important: **there is no All-in-One meta-package on the NPM registry.**
`com.meta.xr.sdk.all` and `com.meta.xr.sdk.all-in-one` both 404. The
"All-in-One v207" is only distributed via the Unity Asset Store. The
registry carries the individual v207 components, so the manifest declares
the All-in-One equivalent set, pinned:

- `com.meta.xr.sdk.core` 207.0.0 (OVRPlugin; pulls `com.unity.xr.hands` 1.7.2 — hand tracking)
- `com.meta.xr.sdk.interaction` 207.0.0 (Interaction SDK)
- `com.meta.xr.sdk.platform` 207.0.0
- `com.meta.xr.sdk.haptics` 207.0.0
- `com.meta.xr.sdk.audio` 85.0.0
- `com.meta.xr.mrutilitykit` 207.0.0
- `com.unity.xr.openxr` 1.14.0 (Unity registry; the XR loader — Meta
  deprecated the Oculus XR Plugin, OpenXR is the supported path)
- `com.unity.xr.hands` 1.7.2 (explicit; also a transitive dep of core)

`com.meta.xr.sdk.voice` is intentionally omitted — not needed for the
hand-tracking Quest build; add it later if voice features are wanted.

## 2. Invoking the custom build method (the exact mechanism)

Unity Build Automation calls a custom Editor method through the build
configuration's **Advanced settings** on the Unity Dashboard:

- **Pre-Export Method**: set to `NFLSim.Editor.CloudBuildEntry.PreExport`
  (class must live under an `Assets/Editor` folder — ours is at
  `Assets/_NFLSim/Editor/CloudBuildEntry.cs`). UBA invokes this public
  static method in the Unity Editor context **after script compilation and
  before exporting the player**. It may optionally take a
  `UnityEngine.CloudBuild.BuildManifestObject` parameter, but that type
  only exists inside Build Automation — the parameterless form is used
  here so the file also compiles locally.
- **Scenes**: add `Assets/_NFLSim/Scenes/RedzoneExperience.unity` to the
  build configuration's scene list (Advanced settings → Scenes). The scene
  does not exist in the repo (gitignored, generated at build time) — the
  pre-export method generates it before UBA's export runs.

What `PreExport()` does: generates the scene (`SetupNFLSim.BuildScene`),
applies Quest player settings (`SetupNFLSim.ApplyQuestSettings`: IL2CPP,
ARM64, min SDK 32, bundle `com.artbakerchat.redzonevr`), registers the
scene in `EditorBuildSettings`, and enables the **OpenXR loader for
Android** via XR Plug-in Management
(`XRPackageMetadataStore.AssignLoader`), creating the XR settings
container if missing. XR-loader failures only warn — they never block
the APK.

Do **not** put `NFLSim.Editor.CloudBuildEntry.BuildAndroid` in the
Pre-Export Method field: it calls `BuildPipeline.BuildPlayer` itself,
so UBA would build the player twice.

`BuildAndroid()` is the other entry point: a complete
`-executeMethod` build for any CI (scene + settings + XR loader +
`BuildPipeline.BuildPlayer` → `Builds/redzone-vr.apk`):
`Unity -batchmode -quit -projectPath . -executeMethod NFLSim.Editor.CloudBuildEntry.BuildAndroid`

Other dashboard fields for the Android configuration: Unity version
`6000.3.25f1` (or enable "Auto detect Unity version", which reads
`ProjectSettings/ProjectVersion.txt` — committed), builder OS Linux or
Windows, target platform Android.

Reference: Unity docs "Run custom scripts during the build process"
(Build Automation → Advanced build configuration).

## 3. Still manual (local editor, once)

- Add an XR rig to the generated scene (OVRCameraRig or XR Origin from
  the Meta XR SDK) and disable the PreviewCamera.
- Run the Meta XR SDK **Project Setup Tool → Fix All / Apply All** once
  locally so OpenXR feature groups (Meta Quest Support, Hand Tracking
  Subsystem, Meta Hand Tracking Aim) are enabled; commit any resulting
  `ProjectSettings` changes.
- First successful cloud build will validate the hand-written
  `ProjectSettings/ProjectSettings.asset` (minimal; Unity fills defaults
  for omitted fields, and `ApplyQuestSettings` enforces the Android
  settings via API at build time).
