using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class DrawMeterUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private DrawMeter meter;         // 空なら自動検出
    [SerializeField] private Image fillImage;         // Mask の子の Image（type=Filled, Horizontal）
    [SerializeField] private RectTransform maskRect;  // Mask の RectTransform（RectMask2D付き）
    [SerializeField] private RectTransform handle;    // 任意：小さい点のRectTransform
    [SerializeField] private TextMeshProUGUI label;   // 任意：数値表示

    [Header("Look & Feel")]
    [Tooltip("ゲージの色を残量に応じてグラデで変化させる")]
    [SerializeField] private Gradient fillGradient;   // 0(赤)→1(緑)等を設定
    [Tooltip("見た目の追従速度。大きいほど即時、小さいほどヌルっと")]
    [SerializeField, Range(0.1f, 20f)] private float smoothSpeed = 8f;

    [Header("Low Warning")]
    [Tooltip("低残量で明滅させるか")]
    [SerializeField] private bool lowBlink = true;
    [Tooltip("この割合以下で点滅開始（0〜1）")]
    [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.2f;
    [Tooltip("点滅スピード")]
    [SerializeField, Range(0.5f, 8f)] private float blinkSpeed = 3f;
    [Tooltip("点滅時の最小明度倍率（0.4なら 40% まで暗くする）")]
    [SerializeField, Range(0.1f, 1f)] private float blinkMinBrightness = 0.5f;

    // 内部状態
    private float targetT = 1f;   // 目標値（0〜1）
    private float displayT = 1f;  // 表示用のスムーズ値（0〜1）

    private void Reset()
    {
#if UNITY_2023_1_OR_NEWER
        meter = Object.FindFirstObjectByType<DrawMeter>();
#else
        meter = FindObjectOfType<DrawMeter>();
#endif
        if (!fillImage)
        {
            // それっぽい場所を自動探索（"Mask/Fill" 想定）
            var mask = transform.Find("Mask");
            if (mask)
            {
                maskRect = mask as RectTransform;
                var fill = mask.Find("Fill");
                if (fill) fillImage = fill.GetComponent<Image>();
            }
        }
    }

    private void OnEnable()
    {
        if (!meter)
        {
#if UNITY_2023_1_OR_NEWER
            meter = Object.FindFirstObjectByType<DrawMeter>();
#else
            meter = FindObjectOfType<DrawMeter>();
#endif
        }

        if (meter) meter.onChanged.AddListener(OnMeterChanged);

        // 初期反映（メーターが無い場合でも0表示で安全）
        if (meter) OnMeterChanged(meter.Current, meter.Max);
        else       OnMeterChanged(0f, 1f);
    }

    private void OnDisable()
    {
        if (meter) meter.onChanged.RemoveListener(OnMeterChanged);
    }

    private void Update()
    {
        // スムーズ追従
        displayT = Mathf.MoveTowards(displayT, targetT, smoothSpeed * Time.deltaTime);

        // 塗り
        if (fillImage)
        {
            fillImage.fillAmount = Mathf.Clamp01(displayT);

            // 色（グラデ + 低残量点滅）
            var baseColor = (fillGradient != null) ? fillGradient.Evaluate(displayT) : Color.white;
            if (lowBlink && displayT <= lowThreshold)
            {
                float s = Mathf.Lerp(1f, blinkMinBrightness, (Mathf.Sin(Time.time * Mathf.PI * blinkSpeed) + 1f) * 0.5f);
                baseColor *= s;
            }
            fillImage.color = baseColor;
        }

        // ハンドル（任意）
        if (handle && maskRect)
        {
            float w = maskRect.rect.width;
            var pos = handle.anchoredPosition;
            pos.x = Mathf.Clamp01(displayT) * w; // 原点が左端ならこれでOK
            handle.anchoredPosition = pos;
        }

        // ラベル（任意）
        if (label && meter)
        {
            label.text = $"{meter.Current:0.##} / {meter.Max:0.##}";
        }
    }

    private void OnMeterChanged(float current, float max)
    {
        // 0〜1 の正しい正規化。current == 0 で targetT=0 になる（完全0を表示）
        targetT = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;

        // 安全に初期同期（初回のみ一気に合わせたい場合）
        if (!Application.isPlaying)
            displayT = targetT;
    }
}
