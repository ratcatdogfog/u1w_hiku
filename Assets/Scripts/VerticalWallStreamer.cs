using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class VerticalWallStreamer : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Transform player;
    [SerializeField] private GameObject leftWallPrefab;
    [SerializeField] private GameObject rightWallPrefab;

    [Header("Placement")]
    [Tooltip("左/右の壁を置くX座標（ワールド）。画面端の位置に揃えてください。")]
    [SerializeField] private float leftX  = -8f;
    [SerializeField] private float rightX = +8f;

    [Tooltip("1セグメント（プレハブ1枚）の“縦の実寸” [ワールド単位]")]
    [Min(0.001f)]
    [SerializeField] private float segmentHeight = 4f;

    [Tooltip("地面（最下段）のY（上面）")]
    [SerializeField] private float groundTopY = 0f;

    [Header("Streaming")]
    [Tooltip("プレイヤーのY + 先読み距離まで生成を前倒しする。")]
    [Min(0f)]
    [SerializeField] private float spawnAheadDistance = 30f;

    [Tooltip("開始時にこの高さまで前もって積む。")]
    [Min(0f)]
    [SerializeField] private float prewarmHeight = 20f;

    [Header("Despawn（破棄制御）")]
    [Tooltip("破棄を有効にするか。OFFなら一切Destroyせず積み上げ続ける。")]
    [SerializeField] private bool enableDespawn = false;

    [Tooltip("有効時：プレイヤーのY - この距離より下のセグメントは破棄")]
    [Min(0f)]
    [SerializeField] private float despawnBehindDistance = 40f;

    [Header("Sorting (任意)")]
    [SerializeField] private string sortingLayerName = "";
    [SerializeField] private int surfaceSortingOrder = 5;
    [SerializeField] private int insideSortingOrder  = 0;

    private readonly Dictionary<int, (GameObject left, GameObject right)> activeByIndex
        = new Dictionary<int, (GameObject, GameObject)>();

    private float nextSpawnTopY;
    private int   bottomIndex;

    private void Reset()
    {
        if (!player)
        {
            var rb = FindObjectOfType<Rigidbody2D>();
            if (rb) player = rb.transform;
        }
    }

    private void Start()
    {
        int startTopIndex = Mathf.FloorToInt((prewarmHeight) / segmentHeight);
        for (int i = 0; i <= startTopIndex; i++)
        {
            SpawnIndexIfNeeded(i);
        }
        nextSpawnTopY = groundTopY + (startTopIndex + 1) * segmentHeight;

        bottomIndex = -1;
    }

    private void Update()
    {
        if (!player) return;

        // 先読み分まで積む
        float targetTopY = player.position.y + spawnAheadDistance;
        while (nextSpawnTopY <= targetTopY)
        {
            int idx = Mathf.RoundToInt((nextSpawnTopY - groundTopY) / segmentHeight);
            SpawnIndexIfNeeded(idx);
            nextSpawnTopY += segmentHeight;
        }

        // ▼ ここをフラグで無効化（破棄しない）
        if (enableDespawn)
        {
            float despawnY = player.position.y - despawnBehindDistance;
            int   keepFromIndex = Mathf.Max(0, Mathf.FloorToInt((despawnY - groundTopY) / segmentHeight) + 1);
            if (keepFromIndex > bottomIndex)
            {
                DespawnBelowIndex(keepFromIndex);
                bottomIndex = keepFromIndex;
            }
        }
    }

    private void SpawnIndexIfNeeded(int index)
    {
        if (index < 0) return;
        if (activeByIndex.ContainsKey(index)) return;

        float yBottom = groundTopY + index * segmentHeight;
        float yCenter = yBottom + segmentHeight * 0.5f;

        var left  = Instantiate(leftWallPrefab,  new Vector3(leftX,  yCenter, 0f), Quaternion.identity, transform);
        var right = Instantiate(rightWallPrefab, new Vector3(rightX, yCenter, 0f), Quaternion.identity, transform);

        SetupSorting(left,  true);
        SetupSorting(right, true);

        activeByIndex[index] = (left, right);
    }

    private void DespawnBelowIndex(int minIndex)
    {
        var toRemove = new List<int>();
        foreach (var kv in activeByIndex)
        {
            if (kv.Key < minIndex)
            {
                if (kv.Value.left)  Destroy(kv.Value.left);
                if (kv.Value.right) Destroy(kv.Value.right);
                toRemove.Add(kv.Key);
            }
        }
        foreach (var k in toRemove) activeByIndex.Remove(k);
    }

    private void SetupSorting(GameObject go, bool isSurfaceAhead)
    {
        if (!go) return;

        var srs = go.GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in srs)
        {
            if (!string.IsNullOrEmpty(sortingLayerName)) sr.sortingLayerName = sortingLayerName;
            sr.sortingOrder = surfaceSortingOrder;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!player) return;

        // 先読みライン
        Gizmos.color = Color.green;
        Gizmos.DrawLine(new Vector3(leftX,  player.position.y + spawnAheadDistance, 0),
                        new Vector3(rightX, player.position.y + spawnAheadDistance, 0));

        // 破棄ライン（有効時のみ）
        if (enableDespawn)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(leftX,  player.position.y - despawnBehindDistance, 0),
                            new Vector3(rightX, player.position.y - despawnBehindDistance, 0));
        }

        // 地面ライン
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(leftX,  groundTopY, 0), new Vector3(rightX, groundTopY, 0));
    }
#endif
}
