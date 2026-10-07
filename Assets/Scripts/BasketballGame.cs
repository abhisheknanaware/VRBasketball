using System;
using UnityEngine;

public enum RoundState { Waiting, Playing, Over }

public class BasketballGame : MonoBehaviour
{
    public static BasketballGame Instance { get; private set; }
    const string BestKey = "VRBasketball.BestPoints";

    public float roundTime = 60f;
    public AudioFX audioFx;

    public RoundState State { get; private set; } = RoundState.Waiting;
    public int Points { get; private set; }
    public int Baskets { get; private set; }
    public int Shots { get; private set; }
    public int Best { get; private set; }
    public float TimeLeft { get; private set; }
    public float Accuracy => Shots == 0 ? 0f : (float)Baskets / Shots;

    public event Action Changed;
    public event Action<int, Vector3> Scored;
    public event Action<RoundState> StateChanged;

    void Awake()
    {
        Instance = this;
        Time.fixedDeltaTime = 1f / 90f;
        Best = PlayerPrefs.GetInt(BestKey, 0);
        TimeLeft = roundTime;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (State != RoundState.Playing) return;
        TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
        Changed?.Invoke();
        if (TimeLeft <= 0f) EndRound();
    }

    public void StartRound()
    {
        Points = Baskets = Shots = 0;
        TimeLeft = roundTime;
        SetState(RoundState.Playing);
        if (audioFx) audioFx.PlayWhistle();
        Changed?.Invoke();
    }

    public void OnBallGrabbed()
    {
        if (State != RoundState.Playing) StartRound();
    }

    public void RegisterShot()
    {
        if (State != RoundState.Playing) return;
        Shots++;
        Changed?.Invoke();
    }

    public void RegisterBasket(int points, Vector3 hoopPosition)
    {
        if (State != RoundState.Playing) return;
        Baskets++;
        Points += points;
        if (audioFx) audioFx.PlayCheer();
        Scored?.Invoke(points, hoopPosition);
        Changed?.Invoke();
    }

    void EndRound()
    {
        if (Points > Best)
        {
            Best = Points;
            PlayerPrefs.SetInt(BestKey, Best);
            PlayerPrefs.Save();
        }
        SetState(RoundState.Over);
        if (audioFx) audioFx.PlayBuzzer();
        Changed?.Invoke();
    }

    void SetState(RoundState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
