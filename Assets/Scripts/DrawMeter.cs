using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class DrawMeter : MonoBehaviour
{
    [Header("Capacity (容量)")]
    [Tooltip("最大量（ワールド距離相当）。")]
    [Min(0.01f)]
    [SerializeField] private float maxCapacity = 15f;

    [Header("Regen (回復)")]
    [Tooltip("毎秒の自動回復量。0で回復なし。")]
    [Min(0f)]
    [SerializeField] private float regenPerSecond = 3f;

    [Header("Initial (初期値)")]
    [Tooltip("開始時の現在量。maxCapacityを超える場合はクランプ。")]
    [Min(0f)]
    [SerializeField] private float startAmount = 15f;

    [System.Serializable]
    public class MeterChangedEvent : UnityEngine.Events.UnityEvent<float, float> {}

    [Header("Events")]
    public MeterChangedEvent onChanged = new MeterChangedEvent();

    public float Max => maxCapacity;
    public float Current => current;

    private float current;
    private int   regenPauseCount = 0;   // 0より大きい間は回復を止める

    private void Awake()
    {
        current = Mathf.Clamp(startAmount, 0f, maxCapacity);
        onChanged?.Invoke(current, maxCapacity);
    }

    private void Update()
    {
        // ★回復停止中は再生しない
        if (regenPauseCount <= 0 && regenPerSecond > 0f && current < maxCapacity)
        {
            current = Mathf.Min(maxCapacity, current + regenPerSecond * Time.deltaTime);
            onChanged?.Invoke(current, maxCapacity);
        }
    }

    /// <summary>
    /// 要求量のうち、実際に消費できた量を返す（部分消費可）。
    /// </summary>
    public float ConsumeUpTo(float segLen)
    {
        float take = Mathf.Min(segLen, current);
        if (take > 0f)
        {
            current -= take;
            onChanged?.Invoke(current, maxCapacity);
        }
        return take;
    }

    /// <summary>
    /// 現在の残量を返す。UIやゴースト末尾のクランプに利用。
    /// </summary>
    public float GetAvailable() => Mathf.Max(0f, current); 

    /// <summary>
    /// 強制的に現在量を設定（デバッグ/リワード等）。
    /// </summary>
    public void SetCurrent(float value)
    {
        current = Mathf.Clamp(value, 0f, maxCapacity);
        onChanged?.Invoke(current, maxCapacity);
    }

    /// <summary>
    /// 最大容量・回復量をランタイムで変更したい場合用。
    /// </summary>
    public void Configure(float newMax, float newRegen, bool keepRatio = true)
    {
        float ratio = (maxCapacity > 0f) ? current / maxCapacity : 0f;
        maxCapacity = Mathf.Max(0.01f, newMax);
        regenPerSecond = Mathf.Max(0f, newRegen);
        current = keepRatio ? Mathf.Clamp01(ratio) * maxCapacity : Mathf.Min(current, maxCapacity);
        onChanged?.Invoke(current, maxCapacity);
    }

    // ★回復停止API（入れ子対応）
    public void PauseRegen()  { regenPauseCount++; }
    public void ResumeRegen() { regenPauseCount = Mathf.Max(0, regenPauseCount - 1); }
}
