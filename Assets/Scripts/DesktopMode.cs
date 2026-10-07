using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// PC controls when no VR headset is running: mouse look, WASD, pick up a ball,
/// hold the left mouse button to charge a throw and release to shoot.
/// </summary>
public class DesktopMode : MonoBehaviour
{
    public float eyeHeight = 1.7f;
    public float moveSpeed = 3f;
    public float lookSensitivity = 0.12f;
    public float pickupRange = 3f;
    public float minThrowSpeed = 3f;
    public float maxThrowSpeed = 13f;
    public float chargeTime = 2f;
    public float loftDegrees = 48f;
    public Vector3 holdOffset = new Vector3(0f, -0.25f, 0.5f);
    public Vector2 courtMin = new Vector2(-7.5f, -6.8f);
    public Vector2 courtMax = new Vector2(7.5f, 6.5f);

    public static bool Active { get; private set; }

    Camera cam;
    Transform rig;
    Hoop hoop;
    Ball held;
    float yaw, pitch;
    float charge;
    float chargeTimer;
    bool charging;

    Image meterFill, sweetSpot;
    RectTransform meterRect;
    TMP_Text help;

    public Ball HeldBall => held;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Active = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (XRSettings.isDeviceActive || FindAnyObjectByType<DesktopMode>() != null) return;
        if (FindAnyObjectByType<Hoop>() == null) return;
        new GameObject("Desktop Mode").AddComponent<DesktopMode>();
    }

    void Start()
    {
        Active = true;
        cam = Camera.main;
        hoop = FindAnyObjectByType<Hoop>();

        GameObject simulator = GameObject.Find("XR Interaction Simulator");
        if (simulator) simulator.SetActive(false);
        TrackedPoseDriver pose = cam.GetComponent<TrackedPoseDriver>();
        if (pose) pose.enabled = false;

        rig = cam.transform.root;
        yaw = rig.eulerAngles.y;
        cam.transform.localRotation = Quaternion.identity;
        Vector3 p = cam.transform.position;
        cam.transform.position = new Vector3(p.x, rig.position.y + eyeHeight, p.z);
        BuildOverlay();
    }

    void OnDestroy()
    {
        Active = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void BuildOverlay()
    {
        Canvas canvas = new GameObject("Desktop Overlay").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Text(canvas.transform, "+", 26, new Vector2(0.5f, 0.5f), Vector2.zero, new Color(1f, 1f, 1f, 0.9f));
        help = Text(canvas.transform,
            "Click to play  |  Mouse: aim at hoop  |  Click / E: pick up  |  Hold left click, release in the GREEN zone  |  WASD: move  |  H: shot assist  |  Esc: release mouse",
            22, new Vector2(0.5f, 0f), new Vector2(0f, 24f), Color.white);

        meterRect = Rect("PowerMeter", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(420f, 26f));
        meterRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        meterFill = Rect("Fill", meterRect, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 20f)).gameObject.AddComponent<Image>();
        meterFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        meterFill.rectTransform.anchoredPosition = new Vector2(3f, 0f);
        sweetSpot = Rect("SweetSpot", meterRect, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 34f)).gameObject.AddComponent<Image>();
        sweetSpot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        sweetSpot.color = new Color(0.3f, 1f, 0.4f, 0.55f);
        sweetSpot.transform.SetSiblingIndex(0);
        Text(meterRect, "POWER", 18, new Vector2(0.5f, 1f), new Vector2(0f, 18f), Color.white);
        meterRect.gameObject.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        RectTransform rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static TMP_Text Text(Transform parent, string value, float size, Vector2 anchor, Vector2 pos, Color color)
    {
        RectTransform rt = Rect("Text", parent, anchor, pos, new Vector2(1600f, 60f));
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = value;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard kb = Keyboard.current;
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            charging = false;
        }

        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (!locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else if (!held) PickUpNearest();
                else
                {
                    charging = true;
                    charge = 0f;
                    chargeTimer = 0f;
                }
            }
            if (charging && held)
            {
                chargeTimer += Time.deltaTime;
                charge = Mathf.PingPong(chargeTimer, chargeTime) / chargeTime;
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    charging = false;
                    Throw(charge);
                }
            }
            if (locked)
            {
                Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
            }
        }
        if (kb != null && kb.eKey.wasPressedThisFrame && !held) PickUpNearest();
        if (kb != null && kb.hKey.wasPressedThisFrame) ShotAssist.Enabled = !ShotAssist.Enabled;

        cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        Move(kb);
        if (held) held.transform.SetPositionAndRotation(cam.transform.TransformPoint(holdOffset), cam.transform.rotation);
        UpdateMeter();
    }

    void Move(Keyboard kb)
    {
        if (kb == null) return;
        Vector2 input = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
        Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
        Vector3 pos = rig.position + (flat * new Vector3(input.x, 0f, input.y)) * moveSpeed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, courtMin.x, courtMax.x);
        pos.z = Mathf.Clamp(pos.z, courtMin.y, courtMax.y);
        rig.position = pos;
    }

    void UpdateMeter()
    {
        if (help) help.alpha = Cursor.lockState == CursorLockMode.Locked ? 0.5f : 1f;
        meterRect.gameObject.SetActive(held);
        if (!held) return;
        float width = meterRect.sizeDelta.x - 6f;
        meterFill.rectTransform.sizeDelta = new Vector2(width * (charging ? charge : 0f), 20f);
        meterFill.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.3f, 0.2f), charging ? charge : 0f);
        float ideal = IdealPower();
        sweetSpot.gameObject.SetActive(ideal >= 0f);
        if (ideal >= 0f)
        {
            float idealSpeed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, ideal);
            float tolerance = ShotAssist.Enabled ? ShotAssist.SpeedTolerance * 0.9f : 0.03f;
            float lo = Mathf.InverseLerp(minThrowSpeed, maxThrowSpeed, idealSpeed * (1f - tolerance));
            float hi = Mathf.InverseLerp(minThrowSpeed, maxThrowSpeed, idealSpeed * (1f + tolerance));
            sweetSpot.rectTransform.anchoredPosition = new Vector2(3f + width * (lo + hi) * 0.5f, 0f);
            sweetSpot.rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, width * (hi - lo)), 34f);
        }
    }

    public Ball PickUpNearest()
    {
        Ball best = FindObjectsByType<Ball>()
            .Where(b => !b.Held)
            .OrderBy(b => Vector3.Distance(b.transform.position, cam.transform.position))
            .FirstOrDefault(b => Vector3.Distance(b.transform.position, cam.transform.position) <= pickupRange);
        if (best) PickUp(best);
        return best;
    }

    public void PickUp(Ball ball)
    {
        held = ball;
        ball.Body.isKinematic = true;
        ball.OnGrabbed();
    }

    /// <summary>Shooting arc: the ball leaves loftDegrees above where you aim, so a shot aimed at the rim drops in steeply.</summary>
    public Vector3 ThrowDirection()
    {
        Vector3 forward = cam.transform.forward;
        Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
        float elevation = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float launch = Mathf.Clamp(elevation + loftDegrees, 10f, 75f) * Mathf.Deg2Rad;
        return flat * Mathf.Cos(launch) + Vector3.up * Mathf.Sin(launch);
    }

    /// <summary>Power (0..1) that sends the ball through the rim for the current aim, or -1 if out of reach.</summary>
    public float IdealPower()
    {
        if (!held || !hoop) return -1f;
        Vector3 start = cam.transform.TransformPoint(holdOffset);
        Vector3 dir = ThrowDirection();
        Vector3 toRim = hoop.rimCenter.position + Vector3.up * 0.05f - start;
        float d = new Vector2(toRim.x, toRim.z).magnitude;
        float h = toRim.y;
        float angle = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f));
        float cos = Mathf.Cos(angle);
        float g = -Physics.gravity.y;
        // Physics integrates in fixed steps, so the ball drops about g*dt*T/2 below the ideal parabola; aim that much higher.
        float speed = 0f, lift = 0f;
        for (int i = 0; i < 4; i++)
        {
            float denom = 2f * cos * cos * (d * Mathf.Tan(angle) - (h + lift));
            if (denom <= 0f) return -1f;
            speed = Mathf.Sqrt(g * d * d / denom);
            float flightTime = d / (speed * cos);
            lift = 0.5f * g * Time.fixedDeltaTime * flightTime;
        }
        float p = Mathf.InverseLerp(minThrowSpeed, maxThrowSpeed, speed);
        return speed > maxThrowSpeed ? -1f : p;
    }

    public void Throw(float power)
    {
        if (!held) return;
        Ball ball = held;
        held = null;
        ball.transform.position = cam.transform.TransformPoint(holdOffset);
        ball.Body.position = ball.transform.position;
        ball.Body.isKinematic = false;
        ball.Body.linearVelocity = ThrowDirection() * Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Mathf.Clamp01(power));
        ball.Body.angularVelocity = -cam.transform.right * 8f;
        ball.OnReleased();
    }

    public void SetAim(float newYaw, float newPitch)
    {
        yaw = newYaw;
        pitch = newPitch;
        cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
