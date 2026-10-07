using UnityEngine;

/// <summary>
/// Forgiving shooting: a throw that is roughly toward the hoop with roughly the right power
/// is corrected onto a clean arc through the rim.
/// </summary>
public static class ShotAssist
{
    public static bool Enabled = true;
    public const float AngleTolerance = 15f;
    public const float SpeedTolerance = 0.25f;
    const float DefaultLaunch = 55f;

    /// <summary>Speed needed to reach <paramref name="target"/> from <paramref name="start"/> at the given launch angle, or -1.</summary>
    public static float RequiredSpeed(Vector3 start, Vector3 target, float launchDegrees)
    {
        Vector3 to = target - start;
        float d = new Vector2(to.x, to.z).magnitude;
        float h = to.y;
        float a = launchDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(a);
        float g = -Physics.gravity.y;
        float speed = 0f, lift = 0f;
        for (int i = 0; i < 4; i++)
        {
            // Fixed-step physics drops the ball about g*dt*T/2 below the ideal parabola; aim that much higher.
            float denom = 2f * cos * cos * (d * Mathf.Tan(a) - (h + lift));
            if (denom <= 0f) return -1f;
            speed = Mathf.Sqrt(g * d * d / denom);
            lift = 0.5f * g * Time.fixedDeltaTime * (d / (speed * cos));
        }
        return speed;
    }

    public static Vector3 Aim(Hoop hoop) => hoop.rimCenter.position + Vector3.up * 0.05f;

    /// <summary>Returns the corrected velocity, or the original one if the throw is too far off to help.</summary>
    public static Vector3 Correct(Vector3 start, Vector3 velocity, Hoop hoop)
    {
        if (!Enabled || !hoop) return velocity;
        Vector3 target = Aim(hoop);
        Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 flatTo = new Vector3(target.x - start.x, 0f, target.z - start.z);
        if (flatVel.magnitude < 0.5f || flatTo.magnitude < 0.5f) return velocity;
        if (Vector3.Angle(flatVel, flatTo) > AngleTolerance) return velocity;

        float launch = Mathf.Atan2(velocity.y, flatVel.magnitude) * Mathf.Rad2Deg;
        if (launch < 45f || launch > 72f) launch = DefaultLaunch;
        float ideal = RequiredSpeed(start, target, launch);
        if (ideal < 0f) return velocity;
        if (Mathf.Abs(velocity.magnitude - ideal) > ideal * SpeedTolerance) return velocity;

        float a = launch * Mathf.Deg2Rad;
        return (flatTo.normalized * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * ideal;
    }
}
