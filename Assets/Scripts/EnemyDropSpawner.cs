using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class EnemyDropSpawner : MonoBehaviour
{
    [Header("Bounds (壁の内側面にEmptyを置いて割り当て)")]
    public Transform leftInnerEdge;
    public Transform rightInnerEdge;

    [Header("Prefabs")]
    public GameObject enemyPrefab;
    public GameObject warningPrefab;

    [Header("Timing")]
    public float warningDuration = 0.8f;
    public Vector2 spawnIntervalRange = new Vector2(1.0f, 2.0f);
    [Tooltip("ONで時間・高度に応じてスポーン加速。OFFでspawnIntervalRangeを使用")]
    public bool useDifficultySchedule = true;

    [Header("Spawn Padding")]
    [Tooltip("左右端からの安全余白（単位）")]
    public float edgePadding = 0.0f;
    [Tooltip("敵の半幅を余白に加味する")]
    public bool respectEnemyWidth = true;

    [Header("Positions")]
    [Tooltip("Warningを画面上端からどれだけ下げるか")]
    public float warningTopOffset = 0.4f;
    [Tooltip("敵を画面上端よりどれだけ上から出すか")]
    public float spawnTopMargin = 2.0f;

    [Header("Difficulty")]
    [Tooltip("高度参照（未指定なら高度寄与は0）")]
    public Transform player;
    [Tooltip("開始時のスポーン/秒")]
    public float baseSpawnsPerSecond = 0.5f;
    [Tooltip("1分ごとのスポーン/秒の増加量")]
    public float addPerMinute = 0.6f;
    [Tooltip("ワールド単位1あたりのスポーン/秒の増加量")]
    public float addPerUnitHeight = 0.02f;
    [Tooltip("高度寄与の基準Y（地面など）")]
    public float heightBaselineY = 0f;
    [Tooltip("スポーン/秒の上限")]
    public float maxSpawnsPerSecond = 4.0f;
    [Tooltip("待ち時間を指数分布でサンプル（自然な連発が出る）")]
    public bool useExponentialSchedule = true;
    [Range(0f, 0.95f)]
    [Tooltip("指数分布を使わない場合の±ゆらぎ")]
    public float jitter = 0.35f;

    [Header("Edge Detection")]
    [Tooltip("親を割り当てても子レンダラー/コライダの境界から内側Xを推定する")]
    public bool useChildBoundsForEdges = true;

    [Header("Pooling")]
    public int poolSize = 16;

    [Header("Start/Stop")]
    [Tooltip("有効化と同時に自動開始する（タイトル画面などが無い場合用）。通常はOFF推奨。")]
    public bool autoStartOnEnable = false;

    Camera cam;
    Queue<GameObject> enemyPool, warningPool;

    struct WarningItem { public GameObject go; public float x; }
    readonly List<WarningItem> activeWarnings = new List<WarningItem>();

    float _startTime;
    bool _spawning = false;
    Coroutine _spawnRoutine;

    void Awake()
    {
        cam = Camera.main;
        enemyPool = new Queue<GameObject>();
        warningPool = new Queue<GameObject>();
        for (int i = 0; i < poolSize; i++)
        {
            enemyPool.Enqueue(CreatePooled(enemyPrefab));
            warningPool.Enqueue(CreatePooled(warningPrefab));
        }
    }

    void OnEnable()
    {
        if (autoStartOnEnable) StartSpawning();
    }

    void OnDisable()
    {
        StopSpawning(clearWarnings: true);
    }

    GameObject CreatePooled(GameObject prefab)
    {
        var go = Instantiate(prefab);
        go.SetActive(false);
        return go;
    }
    GameObject Pop(Queue<GameObject> q, GameObject prefab)
    {
        var go = q.Count > 0 ? q.Dequeue() : Instantiate(prefab);
        go.SetActive(true);
        return go;
    }
    void Push(Queue<GameObject> q, GameObject go)
    {
        if (!go) return;
        go.SetActive(false);
        q.Enqueue(go);
    }

    void LateUpdate()
    {
        if (!cam) return;
        float yTop = cam.transform.position.y + cam.orthographicSize - warningTopOffset;
        for (int i = 0; i < activeWarnings.Count; i++)
        {
            var w = activeWarnings[i];
            if (!w.go || !w.go.activeInHierarchy) continue;
            var p = w.go.transform.position;
            w.go.transform.position = new Vector3(w.x, yTop, p.z);
        }
    }

    // ====== 公開API ======
    public void StartSpawning()
    {
        if (_spawning) return;
        _spawning = true;
        _startTime = Time.time;
        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        _spawnRoutine = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning(bool clearWarnings = false)
    {
        _spawning = false;
        if (_spawnRoutine != null) { StopCoroutine(_spawnRoutine); _spawnRoutine = null; }

        if (clearWarnings)
        {
            for (int i = activeWarnings.Count - 1; i >= 0; i--)
            {
                var w = activeWarnings[i];
                if (w.go) Push(warningPool, w.go);
                activeWarnings.RemoveAt(i);
            }
        }
    }

    IEnumerator SpawnLoop()
    {
        while (_spawning)
        {
            float wait = useDifficultySchedule ? ComputeNextInterval()
                                               : Random.Range(spawnIntervalRange.x, spawnIntervalRange.y);
            // 停止指示を素早く反映するため、細かく待つ
            float t = 0f;
            while (_spawning && t < wait) { t += Time.deltaTime; yield return null; }
            if (!_spawning) yield break;

            (float xMin, float xMax) = ComputeSpawnXRange();
            if (xMax - xMin < 0.1f)
            {
                Debug.LogWarning($"Spawn range too narrow: {xMin:F2}..{xMax:F2} / Check left/rightInnerEdge & padding.");
                continue;
            }
            float x = Random.Range(xMin, xMax);

            var warn = Pop(warningPool, warningPrefab);
            warn.transform.position = new Vector3(x, 0f, 0f);
            activeWarnings.Add(new WarningItem { go = warn, x = x });

            float t2 = 0f;
            while (_spawning && t2 < warningDuration) { t2 += Time.deltaTime; yield return null; }
            if (!_spawning) yield break;

            float yTop = cam.transform.position.y + cam.orthographicSize + spawnTopMargin;
            var enemy = Pop(enemyPool, enemyPrefab);
            enemy.transform.position = new Vector3(x, yTop, 0f);

            // Warningを消す
            for (int i = activeWarnings.Count - 1; i >= 0; i--)
            {
                if (activeWarnings[i].go == warn)
                {
                    Push(warningPool, warn);
                    activeWarnings.RemoveAt(i);
                    break;
                }
            }

            StartCoroutine(AutoDespawn(enemy, enemyPool));
        }
    }

    (float, float) ComputeSpawnXRange()
    {
        float lx = useChildBoundsForEdges ? GetInnerEdgeX(leftInnerEdge, true)  : leftInnerEdge.position.x;
        float rx = useChildBoundsForEdges ? GetInnerEdgeX(rightInnerEdge, false) : rightInnerEdge.position.x;

        if (lx > rx) { var t = lx; lx = rx; rx = t; }

        float corridorWidth = rx - lx;
        float pad = Mathf.Max(0f, edgePadding);
        if (respectEnemyWidth) pad += EstimateEnemyHalfWidth();

        float maxPad = Mathf.Max(0f, corridorWidth * 0.49f);
        if (pad > maxPad)
        {
            Debug.LogWarning($"[EnemyDropSpawner] Padding too large. width={corridorWidth:F2}, pad={pad:F2} -> clamp {maxPad:F2}");
            pad = maxPad;
        }

        float xMin = lx + pad;
        float xMax = rx - pad;

        if (xMax <= xMin)
        {
            float mid = (lx + rx) * 0.5f;
            xMin = xMax = mid;
        }
        return (xMin, xMax);
    }

    float GetInnerEdgeX(Transform root, bool isLeftWall)
    {
        if (!root) return 0f;

        bool found = false;
        float result = isLeftWall ? float.NegativeInfinity : float.PositiveInfinity;

        var srs = root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in srs)
        {
            var b = sr.bounds;
            if (isLeftWall) result = Mathf.Max(result, b.max.x);
            else            result = Mathf.Min(result, b.min.x);
            found = true;
        }

        if (!found)
        {
            var cols = root.GetComponentsInChildren<Collider2D>(true);
            foreach (var c in cols)
            {
                var b = c.bounds;
                if (isLeftWall) result = Mathf.Max(result, b.max.x);
                else            result = Mathf.Min(result, b.min.x);
                found = true;
            }
        }

        if (!found) result = root.position.x;
        return result;
    }

    float EstimateEnemyHalfWidth()
    {
        if (!enemyPrefab) return 0f;

        var sr = enemyPrefab.GetComponentInChildren<SpriteRenderer>();
        if (sr && sr.sprite)
        {
            float half = sr.sprite.bounds.extents.x * Mathf.Abs(sr.transform.lossyScale.x);
            return Mathf.Max(0f, half);
        }
        var col = enemyPrefab.GetComponentInChildren<Collider2D>();
        if (col) return Mathf.Max(0f, col.bounds.extents.x);
        return 0f;
    }

    IEnumerator AutoDespawn(GameObject go, Queue<GameObject> pool)
    {
        while (go && go.activeInHierarchy)
        {
            float bottom = cam.transform.position.y - cam.orthographicSize - 5f;
            if (go.transform.position.y < bottom)
            {
                Push(pool, go);
                yield break;
            }
            yield return null;
        }
    }

    float ComputeNextInterval()
    {
        float elapsedMin = (Time.time - _startTime) / 60f;
        float height = 0f;
        if (player) height = Mathf.Max(0f, player.position.y - heightBaselineY);

        float rate = baseSpawnsPerSecond
                   + addPerMinute     * elapsedMin
                   + addPerUnitHeight * height;

        rate = Mathf.Clamp(rate, 0.0001f, maxSpawnsPerSecond);
        float meanInterval = 1f / rate;

        if (useExponentialSchedule) return SampleExponential(meanInterval);
        float f = Random.Range(1f - jitter, 1f + jitter);
        return Mathf.Max(0.02f, meanInterval * f);
    }

    float SampleExponential(float mean)
    {
        float u = Mathf.Clamp01(1f - Random.value);
        return -mean * Mathf.Log(u);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!leftInnerEdge || !rightInnerEdge) return;

        float lx = useChildBoundsForEdges ? GetInnerEdgeX(leftInnerEdge, true)  : leftInnerEdge.position.x;
        float rx = useChildBoundsForEdges ? GetInnerEdgeX(rightInnerEdge, false) : rightInnerEdge.position.x;
        if (lx > rx) { var t = lx; lx = rx; rx = t; }

        float pad = Mathf.Max(0f, edgePadding);
        if (respectEnemyWidth && enemyPrefab)
        {
            var sr = enemyPrefab.GetComponentInChildren<SpriteRenderer>();
            if (sr && sr.sprite) pad += sr.sprite.bounds.size.x * Mathf.Abs(sr.transform.localScale.x) * 0.5f;
        }

        float corridorWidth = rx - lx;
        float maxPad = Mathf.Max(0f, corridorWidth * 0.49f);
        if (pad > maxPad) pad = maxPad;

        float xMin = lx + pad, xMax = rx - pad;

        Gizmos.color = Color.red;
        float y = (Camera.main ? Camera.main.transform.position.y : transform.position.y);
        Gizmos.DrawLine(new Vector3(xMin, y, 0), new Vector3(xMax, y, 0));
    }
#endif
}
