using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

public static class BasketballBuilder
{
    const string ScenePath = "Assets/Scenes/VRBasketball.unity";
    const string SamplesRoot = "Assets/Samples/XR Interaction Toolkit/3.6.1";
    const string RigPrefab = SamplesRoot + "/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string SimulatorPrefab = SamplesRoot + "/XR Interaction Simulator/XR Interaction Simulator.prefab";

    // Court geometry (metres). Hoop faces -Z; player starts near the free-throw line.
    const float Baseline = 7.2f, HalfLine = -6.8f, SideLine = 7.5f;
    static readonly Vector3 RimCenter = new Vector3(0f, 3.05f, 5.6f);
    const float RimRadius = 0.2286f;
    static readonly Vector3 PlayerStart = new Vector3(0f, 0f, 0.8f);

    static Material mCourt, mArenaFloor, mWall, mWallStripe, mBleacher, mBall, mRim, mBoard, mBoardLine, mPole, mPad, mNet,
        mHousing, mLightPanel, mRack, mConfetti, textOutline, overlayText;
    static Material[] crowdMats;
    static PhysicsMaterial ballPhysics;

    // ---------------- Entry points ----------------

    [MenuItem("VR Basketball/1. Build Project Content")]
    public static void BuildAll()
    {
        try
        {
            if (TMP_Settings.defaultFontAsset == null) throw new Exception("TMP Essentials missing");
            foreach (string d in new[] { "Assets/Scenes", "Assets/Materials", "Assets/Meshes", "Assets/Prefabs", "Assets/Audio" })
                EnsureFolder(d);
            ConfigureXR(BuildTargetGroup.Android, true);
            ConfigureXR(BuildTargetGroup.Standalone, false);
            ConfigurePlayer();
            CreateMaterials();
            GameObject ballPrefab = CreateBallPrefab();
            BuildScene(ballPrefab);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[VRBasketball] Project content built successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError("[VRBasketball] Build content failed: " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    [MenuItem("VR Basketball/2. Build Quest APK")]
    public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Android/VRBasketball.apk");

    [MenuItem("VR Basketball/3. Build Windows")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/VRBasketball.exe");

    static void Build(BuildTarget target, string relative)
    {
        string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = output,
            target = target,
            options = BuildOptions.None
        });
        BuildSummary s = report.summary;
        Debug.Log($"[VRBasketball] {target} build {s.result}: {s.totalSize / (1024f * 1024f):F1} MB, {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalTime}");
        if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
    }

    [MenuItem("VR Basketball/4. Save Preview Screenshots")]
    public static void Screenshot()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Camera cam = Camera.main;
        cam.transform.SetPositionAndRotation(PlayerStart + new Vector3(0f, 1.7f, 0f), Quaternion.Euler(-6f, 0f, 0f));
        GameObject msg = GameObject.Find("Message");
        if (msg) msg.SetActive(false);
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(dir);
        Capture(cam, Path.Combine(dir, "player_view.png"));
        cam.transform.SetPositionAndRotation(new Vector3(-9f, 7f, -9f), Quaternion.Euler(25f, 35f, 0f));
        Capture(cam, Path.Combine(dir, "court_overview.png"));
        cam.transform.SetPositionAndRotation(new Vector3(1.3f, 3.4f, 3.9f), Quaternion.Euler(10f, -30f, 0f));
        Capture(cam, Path.Combine(dir, "hoop_closeup.png"));
        EditorSceneManager.OpenScene(ScenePath);
        Debug.Log("[VRBasketball] Screenshots saved to " + dir);
    }

    static void Capture(Camera cam, string path)
    {
        RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.aspect = 16f / 9f;
        for (int i = 0; i < 4; i++) cam.Render();
        RenderTexture.active = rt;
        Texture2D img = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
    }

    // ---------------- XR + player ----------------

    static void ConfigureXR(BuildTargetGroup group, bool quest)
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            EnsureFolder("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
        XRGeneralSettings settings = perTarget.SettingsForBuildTarget(group);
        settings.InitManagerOnStart = true;
        XRPackageMetadataStore.AssignLoader(settings.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
        EditorUtility.SetDirty(settings);

        FeatureHelpers.RefreshFeatures(group);
        OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        foreach (var f in openXR.GetFeatures())
        {
            string n = f.GetType().Name;
            if (n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" || (quest && n == "MetaQuestFeature"))
            {
                f.enabled = true;
                EditorUtility.SetDirty(f);
            }
        }
        EditorUtility.SetDirty(openXR);
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.productName = "VR Basketball";
        PlayerSettings.companyName = "College Project";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.college.vrbasketball");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

    }

    // ---------------- Assets ----------------

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    static T SaveAsset<T>(T asset, string path) where T : UnityEngine.Object
    {
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    static Texture2D SaveTexture(string name, int w, int h, Func<int, int, Color> pixel, bool repeat, bool mips = true)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixel(x, y);
        t.SetPixels(px);
        t.Apply();
        string path = $"Assets/Materials/{name}.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        imp.alphaIsTransparency = true;
        imp.maxTextureSize = Math.Max(w, h);
        imp.anisoLevel = 8;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Lit(string name, Color color, float smooth = 0.3f, float metal = 0f, Texture2D tex = null, Color? emission = null)
    {
        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (tex) m.SetTexture("_BaseMap", tex);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
        }
        return SaveAsset(m, $"Assets/Materials/{name}.mat");
    }

    static float Line(float d, float halfWidth, float px) => Mathf.Clamp01((halfWidth - Mathf.Abs(d)) / px + 0.5f);

    static Color CourtPixel(float x, float z, float px)
    {
        // Maple planks running toward the hoop.
        float plank = Mathf.Floor((x + 20f) / 0.12f);
        float seed = Mathf.Sin(plank * 12.9898f) * 43758.5453f;
        float tone = (seed - Mathf.Floor(seed)) * 0.08f;
        float grain = Mathf.PerlinNoise(x * 3f, z * 0.4f + plank * 7f) * 0.07f;
        float seam = Mathf.Abs(((x + 20f) / 0.12f) - plank - 0.5f) > 0.47f ? 0.06f : 0f;
        Color wood = new Color(0.84f - tone - grain - seam, 0.63f - tone - grain - seam, 0.38f - tone * 0.6f - grain - seam);

        bool inBounds = Mathf.Abs(x) <= SideLine && z <= Baseline && z >= HalfLine;
        if (!inBounds) wood = Color.Lerp(wood, new Color(0.12f, 0.22f, 0.48f), 0.85f);

        Vector2 rim = new Vector2(0f, RimCenter.z);
        bool inKey = Mathf.Abs(x) < 2.45f && z > Baseline - 5.8f && z < Baseline;
        Vector2 ft = new Vector2(0f, Baseline - 5.8f);
        bool inFtCircle = Vector2.Distance(new Vector2(x, z), ft) < 1.8f && z < ft.y;
        if (inKey || inFtCircle) wood = Color.Lerp(wood, new Color(0.78f, 0.22f, 0.16f), 0.8f);
        if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, HalfLine)) < 1.8f && z > HalfLine)
            wood = Color.Lerp(wood, new Color(0.12f, 0.22f, 0.48f), 0.75f);

        const float w = 0.025f;
        float line = 0f;
        float zClamp = Mathf.Clamp(z, HalfLine, Baseline);
        float xClamp = Mathf.Clamp(x, -SideLine, SideLine);
        if (Mathf.Abs(z - zClamp) < 0.01f) line = Mathf.Max(line, Line(Mathf.Abs(x) - SideLine, w, px));
        if (Mathf.Abs(x - xClamp) < 0.01f)
        {
            line = Mathf.Max(line, Line(z - Baseline, w, px));
            line = Mathf.Max(line, Line(z - HalfLine, w, px));
        }
        if (z > ft.y - w && z < Baseline) line = Mathf.Max(line, Line(Mathf.Abs(x) - 2.45f, w, px));
        if (Mathf.Abs(x) < 2.45f + w) line = Mathf.Max(line, Line(z - ft.y, w, px));
        if (z < ft.y) line = Mathf.Max(line, Line(Vector2.Distance(new Vector2(x, z), ft) - 1.8f, w, px));

        const float threeR = 6.75f, corner = 6.6f;
        float cornerZ = rim.y - Mathf.Sqrt(threeR * threeR - corner * corner);
        if (z >= cornerZ && z <= Baseline) line = Mathf.Max(line, Line(Mathf.Abs(x) - corner, w, px));
        if (z < cornerZ && Mathf.Abs(x) <= corner) line = Mathf.Max(line, Line(Vector2.Distance(new Vector2(x, z), rim) - threeR, w, px));
        if (z < rim.y) line = Mathf.Max(line, Line(Vector2.Distance(new Vector2(x, z), rim) - 1.25f, w, px));
        if (z > HalfLine) line = Mathf.Max(line, Line(Vector2.Distance(new Vector2(x, z), new Vector2(0f, HalfLine)) - 1.8f, w, px));

        return Color.Lerp(wood, new Color(0.97f, 0.97f, 0.95f), line);
    }

    static void CreateMaterials()
    {
        const int size = 2048;
        const float extent = 16f;
        float px = extent / size;
        Texture2D court = SaveTexture("Court", size, size, (ix, iy) =>
            CourtPixel(-8f + (ix + 0.5f) * px, -8.8f + (iy + 0.5f) * px, px), false);

        Texture2D ballTex = SaveTexture("BallSkin", 512, 256, (ix, iy) =>
        {
            float u = ix / 512f, v = iy / 256f;
            float pebble = Mathf.PerlinNoise(ix * 0.35f, iy * 0.35f) * 0.08f;
            Color skin = new Color(0.86f - pebble, 0.38f - pebble * 0.6f, 0.1f);
            float seam = 0f;
            seam = Mathf.Max(seam, Line(v - 0.5f, 0.006f, 1f / 256f));
            foreach (float m in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                seam = Mathf.Max(seam, Line(u - m, 0.004f, 1f / 512f));
            return Color.Lerp(skin, new Color(0.08f, 0.05f, 0.04f), seam);
        }, true);

        Texture2D netTex = SaveTexture("NetPattern", 128, 128, (ix, iy) =>
        {
            float a = Mathf.Abs(((ix + iy) % 32) - 16f) < 1.6f || Mathf.Abs(((ix - iy + 128) % 32) - 16f) < 1.6f ? 1f : 0f;
            return new Color(1f, 1f, 1f, a);
        }, true);

        Texture2D stripes = SaveTexture("WallBanner", 256, 64, (ix, iy) =>
        {
            bool stripe = iy > 26 && iy < 38;
            return stripe ? new Color(1f, 0.55f, 0.1f) : new Color(0.12f, 0.16f, 0.32f);
        }, true);

        mCourt = Lit("CourtFloor", Color.white, 0.6f, 0f, court);
        mArenaFloor = Lit("ArenaFloor", new Color(0.1f, 0.13f, 0.22f), 0.4f);
        mWall = Lit("ArenaWall", new Color(0.13f, 0.17f, 0.32f), 0.2f);
        mWallStripe = Lit("WallBanner", Color.white, 0.2f, 0f, stripes);
        mWallStripe.SetTextureScale("_BaseMap", new Vector2(8f, 1f));
        mBleacher = Lit("Bleacher", new Color(0.32f, 0.35f, 0.42f), 0.3f);
        mBall = Lit("Basketball", Color.white, 0.35f, 0f, ballTex);
        mRim = Lit("Rim", new Color(1f, 0.38f, 0.05f), 0.6f, 0.8f, null, new Color(1f, 0.3f, 0f) * 0.25f);
        mBoard = Lit("Backboard", new Color(0.96f, 0.97f, 1f), 0.8f);
        mBoardLine = Lit("BackboardLine", new Color(0.85f, 0.12f, 0.12f), 0.5f);
        mPole = Lit("Pole", new Color(0.15f, 0.17f, 0.22f), 0.5f, 0.7f);
        mPad = Lit("Padding", new Color(0.12f, 0.25f, 0.6f), 0.2f);
        mHousing = Lit("ScoreboardHousing", new Color(0.05f, 0.05f, 0.07f), 0.6f, 0.3f);
        mLightPanel = Lit("LightPanel", Color.white, 0.5f, 0f, null, Color.white * 3f);
        mRack = Lit("Rack", new Color(0.2f, 0.2f, 0.24f), 0.6f, 0.8f);

        Material net = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Net" };
        net.SetTexture("_BaseMap", netTex);
        net.SetTextureScale("_BaseMap", new Vector2(3f, 1.5f));
        net.SetColor("_BaseColor", Color.white);
        net.SetFloat("_AlphaClip", 1f);
        net.SetFloat("_Cutoff", 0.5f);
        net.EnableKeyword("_ALPHATEST_ON");
        net.SetFloat("_Cull", 0f);
        net.renderQueue = (int)RenderQueue.AlphaTest;
        mNet = SaveAsset(net, "Assets/Materials/Net.mat");

        mConfetti = SaveAsset(new Material(Shader.Find("Universal Render Pipeline/Particles/Lit")) { name = "Confetti" }, "Assets/Materials/Confetti.mat");

        Color[] crowd = { new Color(0.9f, 0.3f, 0.3f), new Color(0.3f, 0.5f, 0.95f), new Color(0.95f, 0.8f, 0.3f),
            new Color(0.3f, 0.8f, 0.45f), new Color(0.85f, 0.85f, 0.85f), new Color(0.6f, 0.35f, 0.85f) };
        crowdMats = crowd.Select((c, i) => Lit($"Crowd_{i}", c, 0.2f)).ToArray();

        ballPhysics = new PhysicsMaterial("Basketball")
        {
            bounciness = 0.78f,
            dynamicFriction = 0.5f,
            staticFriction = 0.6f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Average
        };
        ballPhysics = SaveAsset(ballPhysics, "Assets/Materials/BasketballPhysics.asset");

        Material outline = new Material(TMP_Settings.defaultFontAsset.material) { name = "TMP_Outline" };
        outline.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        outline.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.7f));
        outline.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
        outline.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
        textOutline = SaveAsset(outline, "Assets/Materials/TMP_Outline.mat");

        Material overlay = new Material(TMP_Settings.defaultFontAsset.material) { name = "TMP_Overlay" };
        overlay.shader = Shader.Find("TextMeshPro/Distance Field Overlay");
        overlay.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        overlay.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
        overlay.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.5f);
        overlay.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
        overlay.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
        overlayText = SaveAsset(overlay, "Assets/Materials/TMP_Overlay.mat");
    }

    // ---------------- Meshes ----------------

    static Mesh Torus(string name, float radius, float tube, int segments, int sides)
    {
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            for (int j = 0; j <= sides; j++)
            {
                float b = j / (float)sides * Mathf.PI * 2f;
                Vector3 normal = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b));
                v.Add(c + normal * tube);
                n.Add(normal);
            }
        }
        for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int a0 = i * (sides + 1) + j, a1 = a0 + 1, b0 = a0 + sides + 1, b1 = b0 + 1;
                tri.AddRange(new[] { a0, a1, b0, a1, b1, b0 });
            }
        Mesh m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return SaveAsset(m, $"Assets/Meshes/{name}.asset");
    }

    static Mesh NetMesh(float top, float bottom, float height, int segments)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(dir * top);
            v.Add(dir * bottom + Vector3.down * height);
            uv.Add(new Vector2(i / (float)segments, 1f));
            uv.Add(new Vector2(i / (float)segments, 0f));
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
        }
        Mesh m = new Mesh { name = "Net" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return SaveAsset(m, "Assets/Meshes/Net.asset");
    }

    static Mesh CourtQuad()
    {
        Mesh m = new Mesh { name = "CourtQuad" };
        m.vertices = new[] { new Vector3(-8f, 0f, -8.8f), new Vector3(8f, 0f, -8.8f), new Vector3(-8f, 0f, 7.2f), new Vector3(8f, 0f, 7.2f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return SaveAsset(m, "Assets/Meshes/CourtQuad.asset");
    }

    // ---------------- Prefabs ----------------

    static GameObject CreateBallPrefab()
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Basketball";
        ball.transform.localScale = Vector3.one * 0.24f;
        ball.GetComponent<Renderer>().sharedMaterial = mBall;
        ball.GetComponent<SphereCollider>().sharedMaterial = ballPhysics;
        Rigidbody rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.62f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        XRGrabInteractable grab = ball.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        grab.throwOnDetach = true;
        grab.throwVelocityScale = 1.3f;
        grab.useDynamicAttach = true;
        ball.AddComponent<Ball>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(ball, "Assets/Prefabs/Basketball.prefab");
        UnityEngine.Object.DestroyImmediate(ball);
        return prefab;
    }

    // ---------------- Scene helpers ----------------

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
        bool collider = true, Vector3? euler = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler ?? Vector3.zero;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Transform Group(string name, Transform parent = null)
    {
        Transform t = new GameObject(name).transform;
        if (parent) t.SetParent(parent, false);
        return t;
    }

    static TMP_Text WorldText(Transform parent, string name, string value, float size, Color color, Vector2 pos, Vector2 box,
        TextAlignmentOptions align = TextAlignmentOptions.Center, Material mat = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = value;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        t.fontSharedMaterial = mat ? mat : textOutline;
        return t;
    }

    static Canvas WorldCanvas(string name, Transform parent, Vector3 localPos, Vector2 size, float scale)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        RectTransform rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.localPosition = localPos;
        rt.localScale = Vector3.one * scale;
        return c;
    }

    // ---------------- Scene ----------------

    static Hoop BuildHoop(Transform env)
    {
        Transform root = Group("Hoop", env);
        Hoop hoop = root.gameObject.AddComponent<Hoop>();

        Prim(PrimitiveType.Cylinder, "Pole", root, new Vector3(0f, 1.95f, 7.9f), new Vector3(0.22f, 1.95f, 0.22f), mPole);
        Prim(PrimitiveType.Cube, "BasePad", root, new Vector3(0f, 0.5f, 8.1f), new Vector3(1.2f, 1f, 1.0f), mPad);
        Prim(PrimitiveType.Cube, "Arm", root, new Vector3(0f, 3.62f, 7.0f), new Vector3(0.14f, 0.14f, 1.9f), mPole);
        Prim(PrimitiveType.Cube, "Backboard", root, new Vector3(0f, 3.425f, 6.0f), new Vector3(1.8f, 1.05f, 0.05f), mBoard);
        float face = 5.97f;
        Prim(PrimitiveType.Cube, "SquareTop", root, new Vector3(0f, 3.5f, face), new Vector3(0.59f, 0.05f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "SquareBottom", root, new Vector3(0f, 3.075f, face), new Vector3(0.59f, 0.05f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "SquareLeft", root, new Vector3(-0.27f, 3.29f, face), new Vector3(0.05f, 0.45f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "SquareRight", root, new Vector3(0.27f, 3.29f, face), new Vector3(0.05f, 0.45f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "BorderTop", root, new Vector3(0f, 3.925f, face), new Vector3(1.8f, 0.05f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "BorderBottom", root, new Vector3(0f, 2.925f, face), new Vector3(1.8f, 0.05f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "BorderLeft", root, new Vector3(-0.875f, 3.425f, face), new Vector3(0.05f, 1.05f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "BorderRight", root, new Vector3(0.875f, 3.425f, face), new Vector3(0.05f, 1.05f, 0.01f), mBoardLine, false);
        Prim(PrimitiveType.Cube, "Bracket", root, new Vector3(0f, 3.03f, 5.9f), new Vector3(0.12f, 0.05f, 0.16f), mRim);

        Transform rimCenter = Group("RimCenter", root);
        rimCenter.localPosition = RimCenter;
        Mesh rimMesh = Torus("Rim", RimRadius, 0.012f, 48, 10);
        MeshObj("Rim", rimCenter, rimMesh, mRim, Vector3.zero);
        Transform rimColliders = Group("RimColliders", rimCenter);
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2f;
            GameObject c = new GameObject("RimSegment");
            c.transform.SetParent(rimColliders, false);
            c.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * RimRadius;
            c.AddComponent<SphereCollider>().radius = 0.018f;
        }
        GameObject netGo = MeshObj("Net", rimCenter, NetMesh(RimRadius, 0.15f, 0.42f, 24), mNet, Vector3.zero);
        netGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        GameObject top = new GameObject("ScoreTriggerTop");
        top.transform.SetParent(rimCenter, false);
        top.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        BoxCollider tb = top.AddComponent<BoxCollider>();
        tb.isTrigger = true;
        tb.size = new Vector3(0.28f, 0.06f, 0.28f);
        HoopTrigger tt = top.AddComponent<HoopTrigger>();
        tt.hoop = hoop;
        tt.isTop = true;

        GameObject bottom = new GameObject("ScoreTriggerBottom");
        bottom.transform.SetParent(rimCenter, false);
        bottom.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        BoxCollider bb = bottom.AddComponent<BoxCollider>();
        bb.isTrigger = true;
        bb.size = new Vector3(0.28f, 0.06f, 0.28f);
        bottom.AddComponent<HoopTrigger>().hoop = hoop;

        GameObject confetti = new GameObject("Confetti");
        confetti.transform.SetParent(rimCenter, false);
        confetti.transform.localPosition = new Vector3(0f, -0.4f, 0f);
        ParticleSystem ps = confetti.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.gravityModifier = 0.6f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.3f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.33f),
                new GradientColorKey(new Color(0.3f, 0.9f, 0.5f), 0.66f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.2f;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
        ParticleSystemRenderer pr = confetti.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Mesh;
        pr.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        pr.sharedMaterial = mConfetti;

        hoop.rimCenter = rimCenter;
        hoop.net = netGo.transform;
        hoop.confetti = ps;
        return hoop;
    }

    static void BuildArena(Transform env)
    {
        GameObject floor = new GameObject("ArenaFloor");
        floor.transform.SetParent(env, false);
        BoxCollider fc = floor.AddComponent<BoxCollider>();
        fc.center = new Vector3(0f, -0.5f, 0f);
        fc.size = new Vector3(40f, 1f, 40f);
        Prim(PrimitiveType.Plane, "ArenaSurface", env, new Vector3(0f, -0.005f, 0f), new Vector3(4f, 1f, 4f), mArenaFloor, false);
        MeshObj("Court", env, CourtQuad(), mCourt, Vector3.zero);

        Prim(PrimitiveType.Cube, "WallBack", env, new Vector3(0f, 3f, 11f), new Vector3(24f, 6f, 0.4f), mWall);
        Prim(PrimitiveType.Cube, "WallFront", env, new Vector3(0f, 3f, -12f), new Vector3(24f, 6f, 0.4f), mWall);
        Prim(PrimitiveType.Cube, "WallLeft", env, new Vector3(-12f, 3f, -0.5f), new Vector3(0.4f, 6f, 23f), mWall);
        Prim(PrimitiveType.Cube, "WallRight", env, new Vector3(12f, 3f, -0.5f), new Vector3(0.4f, 6f, 23f), mWall);
        Prim(PrimitiveType.Cube, "BannerBack", env, new Vector3(0f, 1.4f, 10.78f), new Vector3(24f, 0.8f, 0.02f), mWallStripe, false);
        Prim(PrimitiveType.Cube, "BannerLeft", env, new Vector3(-11.78f, 1.4f, -0.5f), new Vector3(0.02f, 0.8f, 23f), mWallStripe, false);
        Prim(PrimitiveType.Cube, "BannerRight", env, new Vector3(11.78f, 1.4f, -0.5f), new Vector3(0.02f, 0.8f, 23f), mWallStripe, false);

        System.Random rng = new System.Random(11);
        foreach (float side in new[] { -1f, 1f })
        {
            Transform stands = Group(side < 0 ? "BleachersLeft" : "BleachersRight", env);
            for (int row = 0; row < 3; row++)
            {
                float x = side * (9.2f + row * 0.8f);
                Prim(PrimitiveType.Cube, "Step", stands, new Vector3(x, 0.25f + row * 0.5f, -0.5f), new Vector3(0.8f, 0.5f + row * 1f, 18f), mBleacher)
                    .transform.localPosition = new Vector3(x, (0.5f + row * 1f) / 2f, -0.5f);
                for (float z = -8.5f; z <= 7.5f; z += 0.75f)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    float h = 0.4f + (float)rng.NextDouble() * 0.12f;
                    Prim(PrimitiveType.Capsule, "Fan", stands, new Vector3(x, 0.5f + row * 1f + h, z + (float)rng.NextDouble() * 0.2f),
                        new Vector3(0.38f, h, 0.38f), crowdMats[rng.Next(crowdMats.Length)], false);
                }
            }
        }

        for (int i = 0; i < 4; i++)
        {
            float x = i < 2 ? -4f : 4f;
            float z = i % 2 == 0 ? -3f : 4f;
            Prim(PrimitiveType.Cube, "LightPanel", env, new Vector3(x, 9f, z), new Vector3(2.5f, 0.15f, 1.2f), mLightPanel, false);
        }

        GameObject logo = new GameObject("CourtLogo");
        logo.transform.SetParent(env, false);
        logo.transform.position = new Vector3(0f, 0.01f, -5.6f);
        logo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        TextMeshPro t = logo.AddComponent<TextMeshPro>();
        t.text = "VR HOOPS";
        t.fontSize = 9;
        t.fontStyle = FontStyles.Bold | FontStyles.Italic;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(1f, 1f, 1f, 0.85f);
        t.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
    }

    static void BuildRack(Transform env, GameObject ballPrefab, Hoop hoop)
    {
        Transform rack = Group("BallRack", env);
        rack.position = new Vector3(1.1f, 0f, PlayerStart.z - 0.3f);
        Prim(PrimitiveType.Cube, "RailFront", rack, new Vector3(0.55f, 0.85f, -0.075f), new Vector3(1.5f, 0.03f, 0.03f), mRack);
        Prim(PrimitiveType.Cube, "RailBack", rack, new Vector3(0.55f, 0.85f, 0.075f), new Vector3(1.5f, 0.03f, 0.03f), mRack);
        Prim(PrimitiveType.Cube, "EndLeft", rack, new Vector3(-0.21f, 0.92f, 0f), new Vector3(0.03f, 0.18f, 0.2f), mRack);
        Prim(PrimitiveType.Cube, "EndRight", rack, new Vector3(1.31f, 0.92f, 0f), new Vector3(0.03f, 0.18f, 0.2f), mRack);
        foreach (float x in new[] { -0.2f, 1.3f })
            foreach (float z in new[] { -0.08f, 0.08f })
                Prim(PrimitiveType.Cube, "Leg", rack, new Vector3(x, 0.42f, z), new Vector3(0.03f, 0.84f, 0.03f), mRack);

        Transform balls = Group("Balls", env);
        for (int i = 0; i < 5; i++)
        {
            GameObject b = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefab, balls);
            b.transform.position = rack.position + new Vector3(i * 0.28f, 0.85f + 0.015f + 0.12f, 0f);
            b.GetComponent<Ball>().hoop = hoop.rimCenter;
        }
    }

    static Scoreboard BuildScoreboard(Transform env, Camera head)
    {
        Transform board = Group("Scoreboard", env);
        board.position = new Vector3(0f, 7.5f, 9.6f);
        Prim(PrimitiveType.Cube, "Housing", board, new Vector3(0f, 0f, 0.25f), new Vector3(5.4f, 2.9f, 0.45f), mHousing);
        Prim(PrimitiveType.Cube, "Trim", board, new Vector3(0f, 1.47f, 0.02f), new Vector3(5.4f, 0.05f, 0.05f), mRim);
        Prim(PrimitiveType.Cube, "TrimB", board, new Vector3(0f, -1.47f, 0.02f), new Vector3(5.4f, 0.05f, 0.05f), mRim);
        Canvas c = WorldCanvas("ScoreCanvas", board, new Vector3(0f, 0f, -0.01f), new Vector2(1040f, 560f), 0.005f);
        Scoreboard sb = board.gameObject.AddComponent<Scoreboard>();
        Color gold = new Color(1f, 0.85f, 0.3f);
        Color dim = new Color(0.7f, 0.75f, 0.85f);
        WorldText(c.transform, "Header", "VR HOOPS  -  SCORECARD", 46, new Color(1f, 0.55f, 0.15f), new Vector2(0f, 235f), new Vector2(1000f, 60f));
        WorldText(c.transform, "PointsLabel", "POINTS", 40, dim, new Vector2(-250f, 160f), new Vector2(400f, 50f));
        sb.pointsText = WorldText(c.transform, "Points", "0", 200, Color.white, new Vector2(-250f, 30f), new Vector2(450f, 220f));
        WorldText(c.transform, "TimeLabel", "TIME", 40, dim, new Vector2(250f, 160f), new Vector2(400f, 50f));
        sb.timeText = WorldText(c.transform, "Time", "1:00", 140, gold, new Vector2(250f, 50f), new Vector2(450f, 170f));
        sb.basketsText = WorldText(c.transform, "Baskets", "BASKETS  0", 36, Color.white, new Vector2(-385f, -120f), new Vector2(250f, 50f));
        sb.shotsText = WorldText(c.transform, "Shots", "SHOTS  0", 36, Color.white, new Vector2(-150f, -120f), new Vector2(220f, 50f));
        sb.accuracyText = WorldText(c.transform, "Accuracy", "ACCURACY  0%", 36, Color.white, new Vector2(110f, -120f), new Vector2(320f, 50f));
        sb.bestText = WorldText(c.transform, "Best", "BEST  0", 36, gold, new Vector2(390f, -120f), new Vector2(200f, 50f));
        sb.statusText = WorldText(c.transform, "Status", "GRAB A BALL TO START", 44, new Color(0.45f, 1f, 0.55f), new Vector2(0f, -215f), new Vector2(1000f, 60f));

        Canvas hud = WorldCanvas("HUD", head.transform, new Vector3(-0.33f, 0.26f, 1.2f), new Vector2(560f, 120f), 0.001f);
        sb.hudText = WorldText(hud.transform, "HudText", "", 40, Color.white, Vector2.zero, new Vector2(560f, 120f), TextAlignmentOptions.Left, overlayText);
        Canvas msg = WorldCanvas("MessageHUD", head.transform, new Vector3(0f, 0.12f, 1.2f), new Vector2(1000f, 260f), 0.001f);
        sb.messageText = WorldText(msg.transform, "Message", "GO!", 110, gold, Vector2.zero, new Vector2(1000f, 260f), TextAlignmentOptions.Center, overlayText);

        GameObject popup = new GameObject("ScorePopup");
        popup.transform.SetParent(env, false);
        TextMeshPro pt = popup.AddComponent<TextMeshPro>();
        pt.text = "+2";
        pt.fontSize = 5;
        pt.fontStyle = FontStyles.Bold;
        pt.alignment = TextAlignmentOptions.Center;
        pt.color = gold;
        pt.fontSharedMaterial = textOutline;
        sb.popupText = pt;
        return sb;
    }

    static void BuildScene(GameObject ballPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Material sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky" };
        sky.SetFloat("_Exposure", 1.1f);
        sky.SetColor("_SkyTint", new Color(0.4f, 0.55f, 0.9f));
        RenderSettings.skybox = SaveAsset(sky, "Assets/Materials/Sky.mat");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.75f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.2f);

        Light sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(62f, -25f, 0f);
        sun.intensity = 1.25f;
        sun.color = new Color(1f, 0.96f, 0.9f);
        sun.shadows = LightShadows.Soft;

        Transform env = Group("Environment");
        BuildArena(env);
        Hoop hoop = BuildHoop(env);
        BuildRack(env, ballPrefab, hoop);

        GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab));
        rig.transform.position = PlayerStart;
        Camera head = rig.GetComponentInChildren<Camera>(true);
        head.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        GameObject sim = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefab));
        sim.tag = "EditorOnly";

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile = SaveAsset(profile, "Assets/Settings/BasketballVolume.asset");
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.6f);
        bloom.threshold.Override(1.1f);
        Tonemapping tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.Neutral);
        foreach (VolumeComponent comp in new VolumeComponent[] { bloom, tone })
        {
            comp.name = comp.GetType().Name;
            AssetDatabase.AddObjectToAsset(comp, profile);
        }
        Volume volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        AudioFX audio = new GameObject("AudioFX").AddComponent<AudioFX>();
        string a = "Assets/Audio/";
        audio.bounce = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "bounce.wav");
        audio.rim = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "rim.wav");
        audio.swish = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "swish.wav");
        audio.cheer = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "cheer.wav");
        audio.buzzer = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "buzzer.wav");
        audio.whistle = AssetDatabase.LoadAssetAtPath<AudioClip>(a + "whistle.wav");

        BasketballGame game = new GameObject("BasketballGame").AddComponent<BasketballGame>();
        game.audioFx = audio;
        BuildScoreboard(env, head);

        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
