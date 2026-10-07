using System.Collections;
using TMPro;
using UnityEngine;

public class Scoreboard : MonoBehaviour
{
    [Header("Arena scoreboard")]
    public TMP_Text pointsText;
    public TMP_Text timeText;
    public TMP_Text basketsText;
    public TMP_Text shotsText;
    public TMP_Text accuracyText;
    public TMP_Text bestText;
    public TMP_Text statusText;

    [Header("In-view HUD")]
    public TMP_Text hudText;
    public TMP_Text messageText;

    [Header("Score popup")]
    public TMP_Text popupText;

    BasketballGame game;
    Coroutine messageRoutine;

    void Start()
    {
        game = BasketballGame.Instance;
        game.Changed += Refresh;
        game.Scored += OnScored;
        game.StateChanged += OnStateChanged;
        if (messageText) messageText.gameObject.SetActive(false);
        if (popupText) popupText.gameObject.SetActive(false);
        OnStateChanged(game.State);
        Refresh();
    }

    void OnDestroy()
    {
        if (!game) return;
        game.Changed -= Refresh;
        game.Scored -= OnScored;
        game.StateChanged -= OnStateChanged;
    }

    void Refresh()
    {
        int seconds = Mathf.CeilToInt(game.TimeLeft);
        string time = $"{seconds / 60}:{seconds % 60:00}";
        pointsText.text = game.Points.ToString();
        timeText.text = time;
        timeText.color = game.State == RoundState.Playing && seconds <= 10 ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.85f, 0.3f);
        basketsText.text = $"BASKETS  {game.Baskets}";
        shotsText.text = $"SHOTS  {game.Shots}";
        accuracyText.text = $"ACCURACY  {Mathf.RoundToInt(game.Accuracy * 100f)}%";
        bestText.text = $"BEST  {game.Best}";
        if (hudText)
            hudText.text = $"<color=#ffd54a>POINTS {game.Points}</color>   TIME {time}\nBaskets {game.Baskets} / Shots {game.Shots}   <size=75%>Assist {(ShotAssist.Enabled ? "<color=#6f6>ON</color>" : "<color=#f66>OFF</color>")}</size>";
    }

    void OnStateChanged(RoundState state)
    {
        switch (state)
        {
            case RoundState.Waiting:
                statusText.text = "GRAB A BALL TO START";
                break;
            case RoundState.Playing:
                statusText.text = "SHOOT!";
                ShowMessage("GO!");
                break;
            case RoundState.Over:
                statusText.text = "TIME UP - GRAB A BALL TO PLAY AGAIN";
                ShowMessage($"TIME UP!\n<size=60%>{game.Points} points  |  {game.Baskets} baskets</size>");
                break;
        }
        Refresh();
    }

    void OnScored(int points, Vector3 hoopPosition)
    {
        ShowMessage(points == 3 ? "3-POINTER!" : "SWISH! +2");
        if (popupText) StartCoroutine(Popup(points, hoopPosition));
    }

    void ShowMessage(string text)
    {
        if (!messageText) return;
        if (messageRoutine != null) StopCoroutine(messageRoutine);
        messageRoutine = StartCoroutine(Message(text));
    }

    IEnumerator Message(string text)
    {
        messageText.text = text;
        messageText.gameObject.SetActive(true);
        for (float t = 0f; t < 0.2f; t += Time.deltaTime)
        {
            messageText.transform.localScale = Vector3.one * (1f + 0.3f * Mathf.Sin(t / 0.2f * Mathf.PI));
            yield return null;
        }
        messageText.transform.localScale = Vector3.one;
        yield return new WaitForSeconds(1.6f);
        messageText.gameObject.SetActive(false);
    }

    IEnumerator Popup(int points, Vector3 at)
    {
        popupText.text = $"+{points}";
        popupText.gameObject.SetActive(true);
        Transform t = popupText.transform;
        Vector3 start = at + Vector3.up * 0.4f;
        for (float x = 0f; x < 1.2f; x += Time.deltaTime)
        {
            t.position = start + Vector3.up * x * 0.8f;
            if (Camera.main) t.rotation = Quaternion.LookRotation(t.position - Camera.main.transform.position);
            popupText.alpha = 1f - Mathf.Clamp01((x - 0.6f) / 0.6f);
            yield return null;
        }
        popupText.gameObject.SetActive(false);
    }
}
