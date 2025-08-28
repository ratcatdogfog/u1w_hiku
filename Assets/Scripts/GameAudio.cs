using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Clips")]
    public AudioClip bgmClip;
    public AudioClip hitClip;       // 敵に当たった時
    public AudioClip uiClickClip;   // ボタン押下
    public AudioClip timeupClip;    // スコア表示時

    [Space]
    [Tooltip("描き始めワンショット（鉛筆を当てる音など）")]
    public AudioClip drawStartClip;
    [Tooltip("描いている間のループSE（サーッという擦過音など）")]
    public AudioClip drawLoopClip;
    [Tooltip("描き終わりワンショット（鉛筆離す音など）")]
    public AudioClip drawEndClip;

    [Header("Volumes")]
    [Range(0f,1f)] public float bgmVolume = 0.65f;
    [Range(0f,1f)] public float sfxVolume = 0.9f;
    [Tooltip("描画ループSEに掛ける乗数（全体SE音量に対して）")]
    [Range(0f,1f)] public float drawLoopVolumeMul = 0.8f;

    [Header("Options")]
    public bool playBgmOnStart = true;
    public bool loopBgm = true;

    AudioSource bgmSource;
    AudioSource sfxSource;
    AudioSource drawLoopSource;   // ← 追加：描画ループ用

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.loop = loopBgm;
        bgmSource.volume = bgmVolume;
        bgmSource.spatialBlend = 0f;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.volume = sfxVolume;
        sfxSource.spatialBlend = 0f;

        drawLoopSource = gameObject.AddComponent<AudioSource>();
        drawLoopSource.playOnAwake = false;
        drawLoopSource.loop = true;
        drawLoopSource.volume = sfxVolume * drawLoopVolumeMul;
        drawLoopSource.spatialBlend = 0f;
    }

    void Start()
    {
        if (playBgmOnStart) PlayBgm();
    }

    // ====== BGM ======
    public void PlayBgm(AudioClip clip = null, float? volume = null)
    {
        if (clip) bgmClip = clip;
        if (volume.HasValue) { bgmVolume = Mathf.Clamp01(volume.Value); bgmSource.volume = bgmVolume; }
        if (!bgmSource.isPlaying || bgmSource.clip != bgmClip)
        {
            bgmSource.clip = bgmClip;
            if (bgmSource.clip) { bgmSource.loop = loopBgm; bgmSource.Play(); }
        }
    }

    public void StopBgm(bool immediate = true, float fadeTime = 0.4f)
    {
        if (immediate || fadeTime <= 0f) { bgmSource.Stop(); return; }
        StartCoroutine(FadeOutBgm(fadeTime));
    }
    IEnumerator FadeOutBgm(float t)
    {
        float start = bgmSource.volume, e = 0f;
        while (e < t) { e += Time.unscaledDeltaTime; bgmSource.volume = Mathf.Lerp(start, 0f, e/t); yield return null; }
        bgmSource.Stop(); bgmSource.volume = bgmVolume;
    }

    // ====== SFX ======
    public void PlaySfx(AudioClip clip, float volMul = 1f)
    {
        if (!clip) return;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volMul) * sfxVolume);
    }

    public void PlayHit()     => PlaySfx(hitClip);
    public void PlayUiClick() => PlaySfx(uiClickClip);
    public void PlayTimeup()  => PlaySfx(timeupClip);

    // ====== Draw SFX（ここを RailDrawer が呼ぶ）======
    public void StartDrawLoop(bool playStartShot = true, float fadeIn = 0.03f)
    {
        if (playStartShot && drawStartClip) PlaySfx(drawStartClip);
        if (!drawLoopClip) return;

        if (drawLoopSource.clip != drawLoopClip) drawLoopSource.clip = drawLoopClip;
        drawLoopSource.volume = sfxVolume * drawLoopVolumeMul;

        if (!drawLoopSource.isPlaying)
        {
            drawLoopSource.Play();
            if (fadeIn > 0f) StartCoroutine(FadeInSource(drawLoopSource, fadeIn, drawLoopSource.volume));
        }
    }

    public void StopDrawLoop(bool immediate = false, float fadeOut = 0.06f, bool playEndShot = true)
    {
        if (playEndShot && drawEndClip) PlaySfx(drawEndClip);

        if (!drawLoopSource.isPlaying) return;

        if (immediate || fadeOut <= 0f) { drawLoopSource.Stop(); return; }
        StartCoroutine(FadeOutSource(drawLoopSource, fadeOut));
    }

    IEnumerator FadeInSource(AudioSource src, float t, float targetVol)
    {
        float e = 0f; src.volume = 0f;
        while (e < t)
        {
            e += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(0f, targetVol, e / t);
            yield return null;
        }
        src.volume = targetVol;
    }

    IEnumerator FadeOutSource(AudioSource src, float t)
    {
        float e = 0f, start = src.volume;
        while (e < t)
        {
            e += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(start, 0f, e / t);
            yield return null;
        }
        src.Stop();
        src.volume = sfxVolume * drawLoopVolumeMul; // 次回のために戻す
    }
}
