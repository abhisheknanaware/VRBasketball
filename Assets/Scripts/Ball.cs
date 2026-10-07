using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
public class Ball : MonoBehaviour
{
    public Transform hoop;
    public float respawnAfter = 5f;
    public float killHeight = -3f;

    Rigidbody body;
    Vector3 homePosition;
    Quaternion homeRotation;
    float releasedAt = -1f;
    Hoop hoopComponent;

    public bool Held { get; private set; }
    public bool InFlight => releasedAt >= 0f;
    public float ReleaseDistance { get; private set; }
    public bool ScoredThisThrow { get; set; }
    public float TopPassTime { get; set; } = -10f;
    public bool EnteredFromBelow { get; set; }
    public Rigidbody Body => body;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        homePosition = transform.position;
        homeRotation = transform.rotation;
        hoopComponent = FindAnyObjectByType<Hoop>();

        XRGrabInteractable grab = GetComponent<XRGrabInteractable>();
        if (grab)
        {
            grab.selectEntered.AddListener(_ => OnGrabbed());
            grab.selectExited.AddListener(_ => OnReleased());
        }
    }

    public void OnGrabbed()
    {
        Held = true;
        releasedAt = -1f;
        if (BasketballGame.Instance) BasketballGame.Instance.OnBallGrabbed();
    }

    public void OnReleased()
    {
        Held = false;
        releasedAt = Time.time;
        ScoredThisThrow = false;
        EnteredFromBelow = false;
        TopPassTime = -10f;
        if (hoop)
        {
            Vector3 d = transform.position - hoop.position;
            d.y = 0f;
            ReleaseDistance = d.magnitude;
        }
        if (BasketballGame.Instance) BasketballGame.Instance.RegisterShot();
        StartCoroutine(AssistAfterRelease(transform.position));
    }

    // The XR grab applies its throw velocity a physics step after release, so correct it once that has happened.
    IEnumerator AssistAfterRelease(Vector3 releasePoint)
    {
        yield return new WaitForFixedUpdate();
        if (Held || body.isKinematic) yield break;
        body.linearVelocity = ShotAssist.Correct(transform.position, body.linearVelocity, hoopComponent);
    }

    void Update()
    {
        if (Held) return;
        if (!InFlight && (transform.position - homePosition).sqrMagnitude > 0.25f) releasedAt = Time.time;
        if (transform.position.y < killHeight || (InFlight && Time.time - releasedAt > respawnAfter)) Respawn();
    }

    public void Respawn()
    {
        releasedAt = -1f;
        Held = false;
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(homePosition, homeRotation);
        body.position = homePosition;
        body.rotation = homeRotation;
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (speed < 0.6f || !AudioFX.Instance) return;
        bool rim = c.collider.GetComponentInParent<Hoop>() != null;
        AudioFX.Instance.PlayImpact(rim, transform.position, Mathf.Clamp01(speed / 8f));
    }
}
