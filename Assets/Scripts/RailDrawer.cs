using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
[RequireComponent(typeof(EdgeCollider2D))]
[RequireComponent(typeof(SurfaceEffector2D))]
public class RailDrawer : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────────────────────

    [Header("Drawing (描画設定)")]
    [Tooltip("新しい点を追加する最小距離。小さすぎると点が過密になり不安定になります。")]
    [Min(0.001f)]
    [SerializeField] private float minPointDist = 0.10f;

    [Tooltip("折れ線を等間隔に並べ替えるか。高速時の接触安定・見た目のガタつき低減に有効。")]
    [SerializeField] private bool resampleUniform = true;

    [Tooltip("等間隔再サンプルの間隔（ワールド距離）。0.15〜0.30 くらいが目安。")]
    [Min(0.001f)]
    [SerializeField] private float resampleSpacing = 0.20f;

    [Header("Launch (発進調整)")]
    [Tooltip("レール着弾直後の最低初速（前方へ付与）。逆向き成分を消した後、最低でもこの速度に揃えます。")]
    [Min(0f)]
    [SerializeField] private float minLaunchSpeed = 5f;

    [Header("Resources (メーター)")]
    [Tooltip("線を描くための残量・回復を管理するメーター。未設定なら無制限扱い。")]
    [SerializeField] private DrawMeter meter;

    [Header("On-Rail Boost (接触ブースト)")]
    [Tooltip("レールに接触し続けた時間に応じて押し出し速度に掛ける倍率の上限。1=ブーストなし。")]
    [Min(1f)]
    [SerializeField] private float maxBoostMultiplier = 3f;

    [Tooltip("1秒あたりのブースト上昇率（on-rail中）。")]
    [Min(0f)]
    [SerializeField] private float boostPerSecond = 1.0f;

    [Tooltip("レールから離れてもこの秒数以内はブーストを維持（グレース）。")]
    [Min(0f)]
    [SerializeField] private float offRailGraceSeconds = 0.15f;

    [Tooltip("グレース経過後、非接触中に1秒あたりどれだけブーストを減らすか。")]
    [Min(0f)]
    [SerializeField] private float offRailDecayPerSecond = 2.0f;

    // ─────────────────────────────────────────────────────────────────────────────
    // Runtime refs / state
    // ─────────────────────────────────────────────────────────────────────────────

    private Camera cam;
    private LineRenderer line;
    private EdgeCollider2D edge;
    private SurfaceEffector2D eff;

    private readonly List<Vector2> fixedPoints = new();
    private bool drawing;

    // Effector向き制御
    private float effBaseSpeedAbs;
    private int   currentSign = +1;
    private bool  onRail = false;

    // 接触ブースト
    private float onRailTime = 0f;
    private float lastExitTime = -999f;

    // ─────────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        cam = Camera.main;

        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 0;

        edge = GetComponent<EdgeCollider2D>();
        edge.usedByEffector = true;

        eff = GetComponent<SurfaceEffector2D>();
        eff.enabled = false;

        effBaseSpeedAbs = Mathf.Abs(eff.speed);
        currentSign     = (eff.speed >= 0f) ? +1 : -1;

#if UNITY_2023_1_OR_NEWER
        if (!meter) meter = Object.FindFirstObjectByType<DrawMeter>();
#else
        if (!meter) meter = FindObjectOfType<DrawMeter>();
#endif
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0)) BeginDrawing();
        if (Input.GetMouseButton(0) && drawing)     UpdateDrawing();
        if (Input.GetMouseButtonUp(0) && drawing)   EndDrawing();

        // オフレール減衰
        if (!onRail && onRailTime > 0f)
        {
            // グレース中は保持
            if (Time.time - lastExitTime > offRailGraceSeconds)
            {
                onRailTime = Mathf.Max(0f, onRailTime - offRailDecayPerSecond * Time.deltaTime);
            }
            ApplyEffectorSpeed(); // 次回着弾に向けて常に最新化
        }
    }

    private void FixedUpdate()
    {
        if (onRail)
        {
            onRailTime += Time.fixedDeltaTime;
            ApplyEffectorSpeed();
        }
    }

    private void OnDisable()
    {
        if (drawing) GameAudio.Instance?.StopDrawLoop(immediate: true, playEndShot: false);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Drawing flow
    // ─────────────────────────────────────────────────────────────────────────────

    private void BeginDrawing()
    {
        drawing = true;
        fixedPoints.Clear();
        line.positionCount = 0;

        if (meter) meter.PauseRegen(); // 描画中は回復停止

        fixedPoints.Add(MouseWorld());
        RebuildLineAndEdge(withGhostTail: true);

        GameAudio.Instance?.StartDrawLoop(playStartShot: true);
    }

    private void UpdateDrawing()
    {
        // 残量0なら即終了（ゴーストも表示しない）
        if (meter && meter.GetAvailable() <= 0f)
        {
            ForceEndDrawing();
            return;
        }

        Vector2 w = MouseWorld();
        Vector2 last = fixedPoints[^1];
        float segLen = Vector2.Distance(last, w);

        if (segLen >= minPointDist)
        {
            if (!meter)
            {
                fixedPoints.Add(w);
                if (fixedPoints.Count >= 2 && !eff.enabled) eff.enabled = true;
                RebuildLineAndEdge(withGhostTail: true);
                return;
            }

            float got = meter.ConsumeUpTo(segLen); // 0〜segLen
            if (got > 0f)
            {
                Vector2 dir = (w - last).sqrMagnitude > 1e-12f ? (w - last).normalized : Vector2.right;
                Vector2 next = last + dir * got;
                fixedPoints.Add(next);

                if (fixedPoints.Count >= 2 && !eff.enabled) eff.enabled = true;
                RebuildLineAndEdge(withGhostTail: true);
                return;
            }

            // ここに来るのは極小数値の誤差だけ。安全側で終了。
            ForceEndDrawing();
            return;
        }

        // ここまでで確定追加なし：ゴーストだけ更新（ただし残量0なら出さない）
        RebuildLineAndEdge(withGhostTail: true);
    }

    private void EndDrawing()
    {
        GameAudio.Instance?.StopDrawLoop();

        drawing = false;
        if (meter) meter.ResumeRegen();

        if (fixedPoints.Count >= 2)
        {
            var final = resampleUniform ? Resample(fixedPoints, resampleSpacing)
                                        : new List<Vector2>(fixedPoints);
            ApplyToLine(final);
            ApplyToEdge(final);
            eff.enabled = true;
        }
        else
        {
            line.positionCount = 0;
            edge.SetPoints(new List<Vector2>());
            eff.enabled = false;
        }
    }

    private void ForceEndDrawing()
    {
        GameAudio.Instance?.StopDrawLoop();

        drawing = false;
        if (meter) meter.ResumeRegen();

        if (fixedPoints.Count >= 2)
        {
            var final = resampleUniform ? Resample(fixedPoints, resampleSpacing)
                                        : new List<Vector2>(fixedPoints);
            ApplyToLine(final);
            ApplyToEdge(final);
            eff.enabled = true;
        }
        else
        {
            line.positionCount = 0;
            edge.SetPoints(new List<Vector2>());
            eff.enabled = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Build visual/physics rails
    // ─────────────────────────────────────────────────────────────────────────────

    private void RebuildLineAndEdge(bool withGhostTail, Vector2? ghostOverride = null)
    {
        if (fixedPoints.Count == 0) return;

        var dyn = new List<Vector2>(fixedPoints);

        if (withGhostTail)
        {
            // 残量0ならゴーストを出さない
            if (!(meter && meter.GetAvailable() <= 0f))
            {
                Vector2 last = fixedPoints[^1];
                Vector2 ghost = ghostOverride ?? MouseWorld();

                if (meter)
                {
                    float remain = meter.GetAvailable(); // 追加可能距離（現在値）
                    Vector2 dir = ghost - last;
                    if (dir.sqrMagnitude > 1e-6f)
                    {
                        float d = dir.magnitude;
                        if (d > remain) ghost = last + dir / d * remain;
                    }
                    else ghost = last;
                }

                if (dyn.Count == 1 || (dyn[^1] - ghost).sqrMagnitude > 1e-6f)
                    dyn.Add(ghost);
            }
        }

        if (resampleUniform && dyn.Count >= 2)
            dyn = Resample(dyn, resampleSpacing);

        ApplyToLine(dyn);
        ApplyToEdge(dyn);

        if (dyn.Count >= 2) eff.enabled = true;
    }

    /// <summary>LineRendererにワールド座標の点列を反映。</summary>
    private void ApplyToLine(List<Vector2> worldPts)
    {
        line.positionCount = worldPts.Count;
        for (int i = 0; i < worldPts.Count; i++) line.SetPosition(i, worldPts[i]);
    }

    /// <summary>EdgeCollider2Dへローカル座標で頂点を反映。</summary>
    private void ApplyToEdge(List<Vector2> worldPts)
    {
        var local = new List<Vector2>(worldPts.Count);
        for (int i = 0; i < worldPts.Count; i++)
            local.Add(transform.InverseTransformPoint(worldPts[i]));
        edge.SetPoints(local);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Effector / Collision
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!eff.enabled || col.contactCount == 0) return;

        var contact = col.GetContact(0);
        Vector2 want = SegmentDirAtWorldPoint(contact.point);

        Vector2 n  = contact.normal;
        Vector2 t0 = new Vector2(-n.y, n.x).normalized;
        float dot  = Vector2.Dot(t0, want);
        currentSign = (dot >= 0f) ? +1 : -1;

        onRail = true;

        var rb = col.rigidbody;
        if (rb != null)
        {
            Vector2 v = rb.linearVelocity;
            float along = Vector2.Dot(v, want);
            if (along < 0f) v -= want * along;
            if (v.magnitude < minLaunchSpeed) v = want * minLaunchSpeed;
            rb.linearVelocity = v;
        }

        ApplyEffectorSpeed();
    }

    private void OnCollisionStay2D(Collision2D col)
    {
        if (!eff.enabled) return;
        onRail = true; // Stayが来ている間は接触継続
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        onRail = false;
        lastExitTime = Time.time; // ここからグレース計測開始
        ApplyEffectorSpeed();     // 現状のブーストで一度更新
    }

    private void ApplyEffectorSpeed()
    {
        float mult = 1f + onRailTime * boostPerSecond;
        mult = Mathf.Min(mult, maxBoostMultiplier);
        eff.speed = currentSign * effBaseSpeedAbs * mult;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Utility
    // ─────────────────────────────────────────────────────────────────────────────

    private Vector2 MouseWorld()
    {
        Vector3 w = cam ? cam.ScreenToWorldPoint(Input.mousePosition) : (Vector3)Input.mousePosition;
        w.z = 0f;
        return w;
    }

    private Vector2 SegmentDirAtWorldPoint(Vector2 p)
    {
        int c = edge.pointCount;
        if (c < 2) return Vector2.right;

        var pts = edge.points;
        float best = float.MaxValue;
        Vector2 bestA = default, bestB = default;

        for (int i = 0; i < c - 1; i++)
        {
            Vector2 a = transform.TransformPoint(pts[i]);
            Vector2 b = transform.TransformPoint(pts[i + 1]);
            Vector2 proj = ProjectPointOnSegment(p, a, b);
            float d2 = (p - proj).sqrMagnitude;
            if (d2 < best) { best = d2; bestA = a; bestB = b; }
        }

        Vector2 dir = bestB - bestA;
        return (dir.sqrMagnitude > 1e-6f) ? dir.normalized : Vector2.right;
    }

    private static Vector2 ProjectPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return a + ab * t;
    }

    private static List<Vector2> Resample(List<Vector2> src, float spacing)
    {
        if (src == null || src.Count == 0) return new List<Vector2>();
        List<Vector2> outPts = new() { src[0] };
        float acc = 0f;

        for (int i = 1; i < src.Count; i++)
        {
            Vector2 a = src[i - 1], b = src[i];
            float seg = Vector2.Distance(a, b);
            if (seg <= 1e-6f) continue;

            while (acc + seg >= spacing)
            {
                float t = (spacing - acc) / seg;
                Vector2 np = Vector2.Lerp(a, b, t);
                outPts.Add(np);
                a = np;
                seg = Vector2.Distance(a, b);
                acc = 0f;
            }
            acc += seg;
        }

        if ((outPts[^1] - src[^1]).sqrMagnitude > 1e-6f) outPts.Add(src[^1]);
        return outPts;
    }
}
