using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerHitResponder : MonoBehaviour
{
    [Header("Hit Detection")]
    public LayerMask enemyLayerMask;

    [Header("Knockback")]
    public float knockbackImpulse = 12f;

    [Header("Invincibility / Blink")]
    public float invincibleTime = 0.8f;
    public float blinkInterval = 0.08f;

    [Header("Damage")]
    [Tooltip("被弾1回あたりのゲージ減少量（DrawMeter.Current から引く）")]
    public float damageAmount = 2f;

    [Header("Meter (同じインスタンスをここに割り当て)")]
    [SerializeField] private DrawMeter meter;   // ← これを Inspector で同じ Meter にドラッグ
    [SerializeField] private bool logOnHit = false;

    Rigidbody2D rb;
    SpriteRenderer[] sprites;
    bool invincible;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sprites = GetComponentsInChildren<SpriteRenderer>(true);

        // ★ フォールバック探索（未割り当てでもなるべく見つける）
        if (!meter) meter = GetComponent<DrawMeter>();
        if (!meter) meter = GetComponentInParent<DrawMeter>();
        if (!meter) meter = GetComponentInChildren<DrawMeter>(true);
#if UNITY_2023_1_OR_NEWER
        if (!meter) meter = Object.FindFirstObjectByType<DrawMeter>();
#else
        if (!meter) meter = FindObjectOfType<DrawMeter>();
#endif

        if (!meter)
            Debug.LogWarning("[PlayerHitResponder] DrawMeter が見つかりません。UIは減りません。Inspectorで meter を割り当ててください。", this);
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (!rb.simulated) return;
        if (!IsEnemy(col.collider.gameObject.layer)) return;

        Vector2 dir = AverageNormal(col);
        ApplyHit(dir);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!rb.simulated) return;
        if (!IsEnemy(other.gameObject.layer)) return;

        Vector2 self = (Vector2)transform.position;
        Vector2 cp   = other.ClosestPoint(self);
        Vector2 dir  = self - cp;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
        ApplyHit(dir.normalized);
    }

    bool IsEnemy(int layer) => ((1 << layer) & enemyLayerMask) != 0;

    Vector2 AverageNormal(Collision2D col)
    {
        if (col.contactCount == 0) return Vector2.up;
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < col.contactCount; i++) sum += col.GetContact(i).normal;
        if (sum.sqrMagnitude < 1e-6f) sum = Vector2.up;
        return sum.normalized;
    }

    void ApplyHit(Vector2 dir)
    {
        if (invincible) return;

        GameAudio.Instance?.PlayHit();

        // ノックバック
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(dir * knockbackImpulse, ForceMode2D.Impulse);

        // ゲージ減少（同じ DrawMeter に対して）
        if (meter)
        {
            float before = meter.Current;
            meter.PauseRegen();
            meter.SetCurrent(meter.Current - damageAmount);
            if (logOnHit) Debug.Log($"[PlayerHitResponder] Meter {before:0.##} -> {meter.Current:0.##}", this);
        }

        StartCoroutine(InvincibleBlink());
    }

    IEnumerator InvincibleBlink()
    {
        invincible = true;
        float t = 0f;
        while (t < invincibleTime)
        {
            SetAlpha(0.25f);
            yield return new WaitForSeconds(blinkInterval);
            SetAlpha(1f);
            yield return new WaitForSeconds(blinkInterval);
            t += blinkInterval * 2f;
        }
        SetAlpha(1f);
        invincible = false;
        if (meter) meter.ResumeRegen();
    }

    void SetAlpha(float a)
    {
        foreach (var s in sprites)
        {
            if (!s) continue;
            var c = s.color; c.a = a; s.color = c;
        }
    }
}
