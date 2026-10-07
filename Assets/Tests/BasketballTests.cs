using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class BasketballTests
{
    BasketballGame game;
    Hoop hoop;
    Ball[] balls;
    Scoreboard board;

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        LogAssert.ignoreFailingMessages = true;
        ShotAssist.Enabled = true;
        PlayerPrefs.DeleteKey("VRBasketball.BestPoints");
        yield return SceneManager.LoadSceneAsync("VRBasketball");
        yield return new WaitForSeconds(0.3f);
        game = BasketballGame.Instance;
        hoop = Object.FindAnyObjectByType<Hoop>();
        balls = Object.FindObjectsByType<Ball>();
        board = Object.FindAnyObjectByType<Scoreboard>();
    }

    /// <summary>Throws a ball from <paramref name="from"/> so it arrives at <paramref name="target"/> after <paramref name="flightTime"/> seconds.</summary>
    static void Shoot(Ball ball, Vector3 from, Vector3 target, float flightTime)
    {
        ball.OnGrabbed();
        ball.Body.isKinematic = false;
        ball.transform.position = from;
        ball.Body.position = from;
        ball.Body.angularVelocity = Vector3.zero;
        // Compensate for fixed-step integration (the ball drops g*dt*T/2 below the analytic parabola).
        Vector3 aim = target - 0.5f * Physics.gravity * Time.fixedDeltaTime * flightTime;
        ball.Body.linearVelocity = (aim - from - 0.5f * Physics.gravity * flightTime * flightTime) / flightTime;
        ball.OnReleased();
    }

    IEnumerator WaitForBaskets(int count, float timeout = 3f)
    {
        for (float t = 0f; t < timeout && game.Baskets < count; t += Time.deltaTime) yield return null;
    }

    [UnityTest]
    public IEnumerator CourtHoopAndScorecardAreSetUp()
    {
        Assert.AreEqual(5, balls.Length, "Ball rack should hold 5 balls");
        Assert.AreEqual(3.05f, hoop.rimCenter.position.y, 0.01f, "Regulation rim height");
        Assert.AreEqual(RoundState.Waiting, game.State);
        Assert.AreEqual("GRAB A BALL TO START", board.statusText.text);
        Assert.AreEqual("0", board.pointsText.text);
        Assert.IsTrue(balls.All(b => b.transform.position.y > 0.8f), "Balls should rest on the rack");
        yield return null;
    }

    [UnityTest]
    public IEnumerator GrabbingABallStartsTheRound()
    {
        balls[0].OnGrabbed();
        yield return null;
        Assert.AreEqual(RoundState.Playing, game.State);
        Assert.AreEqual("SHOOT!", board.statusText.text);
        Assert.Greater(game.TimeLeft, 59f);
    }

    [UnityTest]
    public IEnumerator FreeThrowSwishScoresTwo()
    {
        Shoot(balls[0], new Vector3(0f, 1.7f, 1.4f), hoop.rimCenter.position + Vector3.up * 0.02f, 1.25f);
        yield return WaitForBaskets(1);
        Assert.AreEqual(1, game.Baskets);
        Assert.AreEqual(2, game.Points);
        Assert.AreEqual(1, game.Shots);
        Assert.AreEqual("2", board.pointsText.text);
        Assert.AreEqual("ACCURACY  100%", board.accuracyText.text);
    }

    [UnityTest]
    public IEnumerator ShotFromBehindTheArcScoresThree()
    {
        Shoot(balls[0], new Vector3(1.0f, 1.8f, -1.6f), hoop.rimCenter.position + Vector3.up * 0.02f, 1.5f);
        yield return WaitForBaskets(1);
        Assert.AreEqual(1, game.Baskets);
        Assert.AreEqual(3, game.Points);
    }

    [UnityTest]
    public IEnumerator MissCountsAsShotButNotBasket()
    {
        Shoot(balls[0], new Vector3(0f, 1.7f, 1.4f), new Vector3(2.5f, 2.0f, 5.0f), 1.0f);
        yield return new WaitForSeconds(2f);
        Assert.AreEqual(0, game.Baskets);
        Assert.AreEqual(0, game.Points);
        Assert.AreEqual(1, game.Shots);
        Assert.AreEqual("ACCURACY  0%", board.accuracyText.text);
    }

    [UnityTest]
    public IEnumerator BallPushedUpThroughNetDoesNotScore()
    {
        Ball ball = balls[0];
        game.StartRound();
        ball.OnGrabbed();
        ball.Body.isKinematic = false;
        Vector3 under = hoop.rimCenter.position + Vector3.down * 0.6f;
        ball.transform.position = under;
        ball.Body.position = under;
        ball.OnReleased();
        ball.Body.linearVelocity = Vector3.up * 4.2f;
        yield return new WaitForSeconds(2f);
        Assert.AreEqual(0, game.Baskets, "A ball going up through the net must not count");
    }

    [UnityTest]
    public IEnumerator RoundEndsWhenTimeRunsOutAndKeepsBest()
    {
        game.roundTime = 2f;
        Shoot(balls[0], new Vector3(0f, 1.7f, 1.4f), hoop.rimCenter.position + Vector3.up * 0.02f, 1.25f);
        yield return new WaitForSeconds(2.4f);
        Assert.AreEqual(RoundState.Over, game.State);
        Assert.AreEqual(2, game.Best);
        StringAssert.Contains("TIME UP", board.statusText.text);

        balls[1].OnGrabbed();
        yield return null;
        Assert.AreEqual(RoundState.Playing, game.State, "Grabbing a ball again starts a new round");
        Assert.AreEqual(0, game.Points);
    }

    [UnityTest]
    public IEnumerator ThrownBallReturnsToRack()
    {
        Ball ball = balls[0];
        Vector3 home = ball.transform.position;
        ball.respawnAfter = 1f;
        Shoot(ball, new Vector3(0f, 1.7f, 1.4f), new Vector3(-4f, 0.5f, 3f), 1.0f);
        yield return new WaitForSeconds(1.6f);
        Assert.Less(Vector3.Distance(ball.transform.position, home), 0.05f, "Ball should be back on the rack");
    }

    [UnityTest]
    public IEnumerator DesktopShotAtSweetSpotScores()
    {
        DesktopMode desktop = Object.FindAnyObjectByType<DesktopMode>();
        Assert.IsNotNull(desktop, "Desktop mode should be active without a headset");
        Assert.IsNotNull(desktop.PickUpNearest(), "Should pick up a ball from the rack");
        Camera cam = Camera.main;
        Vector3 to = hoop.rimCenter.position - cam.transform.position;
        desktop.SetAim(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
        yield return null;
        float power = desktop.IdealPower();
        Assert.GreaterOrEqual(power, 0f, "Hoop should be reachable");
        desktop.Throw(power);
        yield return WaitForBaskets(1);
        Assert.AreEqual(1, game.Baskets, "Shot at the sweet spot should go in");
    }

    [UnityTest]
    public IEnumerator SlightlyOffShotIsGuidedInByAssist()
    {
        Vector3 offTarget = hoop.rimCenter.position + new Vector3(0.3f, 0.1f, -0.35f);
        Shoot(balls[0], new Vector3(0f, 1.7f, 1.4f), offTarget, 1.3f);
        yield return WaitForBaskets(1);
        Assert.AreEqual(1, game.Baskets, "Assist should guide a near-miss into the basket");
    }

    [UnityTest]
    public IEnumerator SameShotMissesWithAssistOff()
    {
        ShotAssist.Enabled = false;
        Vector3 offTarget = hoop.rimCenter.position + new Vector3(0.3f, 0.1f, -0.35f);
        Shoot(balls[0], new Vector3(0f, 1.7f, 1.4f), offTarget, 1.3f);
        yield return new WaitForSeconds(2.5f);
        ShotAssist.Enabled = true;
        Assert.AreEqual(0, game.Baskets, "Without assist the off-target shot should miss");
    }
}
