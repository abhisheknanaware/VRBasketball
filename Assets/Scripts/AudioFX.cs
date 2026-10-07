using UnityEngine;

public class AudioFX : MonoBehaviour
{
    public static AudioFX Instance { get; private set; }

    public AudioClip bounce, rim, swish, cheer, buzzer, whistle;
    AudioSource ui;

    void Awake()
    {
        Instance = this;
        ui = gameObject.AddComponent<AudioSource>();
        ui.playOnAwake = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void PlayImpact(bool onRim, Vector3 position, float strength)
    {
        AudioClip clip = onRim ? rim : bounce;
        if (clip) AudioSource.PlayClipAtPoint(clip, position, Mathf.Lerp(0.2f, 1f, strength));
    }

    public void PlaySwish(Vector3 position)
    {
        if (swish) AudioSource.PlayClipAtPoint(swish, position, 1f);
    }

    public void PlayCheer() => Play(cheer, 0.7f);
    public void PlayBuzzer() => Play(buzzer, 0.8f);
    public void PlayWhistle() => Play(whistle, 0.6f);

    void Play(AudioClip clip, float volume)
    {
        if (clip) ui.PlayOneShot(clip, volume);
    }
}
