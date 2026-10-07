using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HoopTrigger : MonoBehaviour
{
    public Hoop hoop;
    public bool isTop;

    void OnTriggerEnter(Collider other)
    {
        Ball ball = other.GetComponent<Ball>();
        if (!ball || !hoop) return;
        if (isTop) hoop.TopEntered(ball);
        else hoop.BottomEntered(ball);
    }
}
