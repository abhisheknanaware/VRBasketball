using System.Collections;
using UnityEngine;

public class Hoop : MonoBehaviour
{
    [Tooltip("Rim centre; scoring distance is measured from here.")]
    public Transform rimCenter;
    public float threePointRadius = 6.75f;
    public Transform net;
    public ParticleSystem confetti;

    Vector3 netScale;

    void Start()
    {
        if (net) netScale = net.localScale;
    }

    public void TopEntered(Ball ball)
    {
        if (ball.Body.linearVelocity.y < 0f) ball.TopPassTime = Time.time;
    }

    public void BottomEntered(Ball ball)
    {
        if (ball.Body.linearVelocity.y > 0f)
        {
            ball.EnteredFromBelow = true;
            return;
        }
        bool cameThroughTop = Time.time - ball.TopPassTime < 1f;
        if (!cameThroughTop || ball.ScoredThisThrow || ball.EnteredFromBelow || ball.Held) return;
        ball.ScoredThisThrow = true;

        int points = ball.ReleaseDistance >= threePointRadius ? 3 : 2;
        if (AudioFX.Instance) AudioFX.Instance.PlaySwish(rimCenter.position);
        if (confetti) confetti.Play(true);
        if (net) StartCoroutine(Swish());
        if (BasketballGame.Instance) BasketballGame.Instance.RegisterBasket(points, rimCenter.position);
    }

    IEnumerator Swish()
    {
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            float k = Mathf.Sin(t / 0.5f * Mathf.PI) * (1f - t / 0.5f);
            net.localScale = new Vector3(netScale.x * (1f - 0.25f * k), netScale.y * (1f + 0.35f * k), netScale.z * (1f - 0.25f * k));
            yield return null;
        }
        net.localScale = netScale;
    }
}
