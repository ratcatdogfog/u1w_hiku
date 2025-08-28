using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Threading.Tasks;         // ★ await を使う
using Unityroom.Client;   

public class GameFlowController : MonoBehaviour
{
    [Header("Core Refs")]
    [SerializeField] private Transform player;
    [SerializeField] private Rigidbody2D playerRb;
    [SerializeField] private EnemyDropSpawner spawner;  // 敵スポーナー
    [Tooltip("高度スコアの基準Y（地面上面など）")]
    [SerializeField] private float baseLineY = 0f;

    [Header("Timer")]
    [SerializeField] private int timeLimitSeconds = 60;

    [Header("HUD (TMP)")]
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text heightText;

    [Header("Start UI")]
    [Tooltip("開始時のパネル（説明文＋開始ボタンを含む）")]
    [SerializeField] private CanvasGroup startPanel;
    [SerializeField] private TMP_Text topInstructionText; // 上部の一文（任意）
    [SerializeField] private Button startButton;          // 「ゲーム開始」

    [Header("TimeUp Panel")]
    [SerializeField] private CanvasGroup timeUpPanel;     // スコア＋メッセージ＋再プレイボタン
    [SerializeField] private TMP_Text scoreText;          // 中央スコア
    [SerializeField] private TMP_Text messageText;        // "Thank you for playing"
    [SerializeField] private Button playAgainButton;      // 「もう一度遊ぶ」
    [SerializeField] private float panelFadeTime = 0.35f;

    [Header("Ranking")]
    [SerializeField] private TMP_Text rankText;          // ランク表示用（任意）
    [SerializeField] private bool tintScoreWithRank = true; // スコア文字もランク色で染める

    [System.Serializable]
    public struct RankBand
    {
        public float minScore;   // この値以上で採用
        public string label;     // "Beginner" / "Master" など
        public Color color;      // 低: 緑 / 高: 紫 など
    }

    [Tooltip("スコア閾値の低い順に並べてください。最も近い下限(minScore)のバンドを選びます。")]
    [SerializeField] private List<RankBand> rankBands = new List<RankBand>();

    [Header("unityroom")]
    [SerializeField] private int    unityroomScoreboardId = 1;  // ボードNo
    [SerializeField] private string unityroomHmacKey = "";      // HMACキー（unityroomの設定画面で発行）
    [SerializeField] private bool   sendScoreOnTimeUp = true;   // 送信ON/OFF

    private UnityroomClient _urClient;

    // 進行状態
    float timeLeft;
    bool gameStarted;
    bool timeUp;

    // 物理の初期値を保持して正しく復元する
    RigidbodyConstraints2D rbInitialConstraints;
    RigidbodyType2D       rbInitialBodyType;
    float                 rbInitialGravityScale;
    bool                  rbInitialSimulated;
    Vector3               playerStartPos;

    void Awake()
    {

        if (!string.IsNullOrEmpty(unityroomHmacKey))
        {
            _urClient = new UnityroomClient { HmacKey = unityroomHmacKey };
        }
        else
        {
            Debug.LogWarning("[unityroom] HMACキーが未設定です。スコア送信はスキップします。");
        }
    }

    void OnDestroy()
    {
        _urClient?.Dispose();
    }

    void Start()
    {
        if (!player)  { Debug.LogError("[GameFlowController] player未設定"); enabled = false; return; }
        if (!playerRb) playerRb = player.GetComponent<Rigidbody2D>();

        // 初期物理値を保存
        rbInitialConstraints   = playerRb.constraints;
        rbInitialBodyType      = playerRb.bodyType;
        rbInitialGravityScale  = playerRb.gravityScale;
        rbInitialSimulated     = playerRb.simulated;
        playerStartPos         = player.position;

        // 開始前は物理を完全停止（勝手に動かない）
        playerRb.linearVelocity = Vector2.zero;
        playerRb.angularVelocity = 0f;
        playerRb.simulated = false;

        // スポーンは開始まで停止
        if (spawner) spawner.StopSpawning(true);   // ← 事前に完全停止（警告も消す）

        // UI初期化
        timeLeft = Mathf.Max(1, timeLimitSeconds);
        SetCanvasGroup(startPanel, true, 1f);
        if (topInstructionText) topInstructionText.gameObject.SetActive(true);
        SetCanvasGroup(timeUpPanel, false, 0f);

        // ボタン配線（Inspector側の設定と二重になってもOK）
        if (startButton)
        {
            startButton.onClick.RemoveListener(StartGame);
            startButton.onClick.AddListener(StartGame);
        }
        if (playAgainButton)
        {
            playAgainButton.onClick.RemoveListener(Restart);
            playAgainButton.onClick.AddListener(Restart);
        }

        UpdateHUD();

        GameAudio.Instance?.PlayBgm();
    }

    void Update()
    {
        if (!gameStarted || timeUp) return;

        timeLeft -= Time.deltaTime;
        if (timeLeft < 0f) timeLeft = 0f;

        UpdateHUD();

        if (timeLeft <= 0f && !timeUp)
            HandleTimeUp();
    }

    // ====== UI/HUD ======

    void UpdateHUD()
    {
        if (timeText) timeText.text = FormatTime(timeLeft);

        float altitude = Mathf.Max(0f, player.position.y - baseLineY);
        if (heightText) heightText.text = $"{altitude:0.0} m";
    }

    string FormatTime(float t)
    {
        int sec = Mathf.CeilToInt(t);
        int m = sec / 60;
        int s = sec % 60;
        return $"{m:00}:{s:00}";
    }

    static void SetCanvasGroup(CanvasGroup cg, bool visible, float alphaWhenVisible = 1f)
    {
        if (!cg) return;
        cg.alpha = visible ? alphaWhenVisible : 0f;
        cg.gameObject.SetActive(visible);
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    IEnumerator FadeIn(CanvasGroup cg, float t)
    {
        if (!cg) yield break;
        cg.gameObject.SetActive(true);
        cg.interactable = true;
        cg.blocksRaycasts = true;

        cg.alpha = 0f;
        float e = 0f;
        while (e < t)
        {
            e += Time.unscaledDeltaTime;
            cg.alpha = Mathf.InverseLerp(0f, t, e);
            yield return null;
        }
        cg.alpha = 1f;
    }

    IEnumerator PunchScale(RectTransform rt, float scale, float duration)
    {
        if (!rt) yield break;
        Vector3 baseS = Vector3.one;
        float half = duration * 0.5f, t = 0f;
        // up
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(1f, scale, t / half);
            rt.localScale = baseS * k;
            yield return null;
        }
        // down
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(scale, 1f, t / half);
            rt.localScale = baseS * k;
            yield return null;
        }
        rt.localScale = baseS;
    }

    // ====== フロー ======

    public void StartGame()
    {
        if (gameStarted) return;

        GameAudio.Instance?.PlayUiClick(); // クリックSE
        GameAudio.Instance?.PlayBgm(); 

        GameAudio.Instance?.PlayUiClick();

        gameStarted = true;
        timeUp = false;
        timeLeft = Mathf.Max(1, timeLimitSeconds);

        // 開始UIを閉じる
        SetCanvasGroup(startPanel, false, 0f);
        if (topInstructionText) topInstructionText.gameObject.SetActive(false);

        // 物理を復帰（初期状態へ正確に戻す）
        playerRb.simulated    = rbInitialSimulated;   // 多くのケースでtrue
        playerRb.bodyType     = rbInitialBodyType;
        playerRb.gravityScale = rbInitialGravityScale;
        playerRb.constraints  = rbInitialConstraints;

        // スポーン開始
        if (spawner) spawner.StartSpawning();      // ← 明示的に開始
    }

    void HandleTimeUp()
    {
        timeUp = true;

        GameAudio.Instance?.PlayTimeup();

        // スコア確定
        float score = Mathf.Max(0f, player.position.y - baseLineY);

        // ★ unityroom に送信（Fire-and-forget）
        if (sendScoreOnTimeUp && _urClient != null)
            _ = SendUnityroomScoreAsync(score);

        var rank = EvaluateRank(score);
        if (rankText)
        {
            rankText.text  = rank.label;
            rankText.color = rank.color;
            // ランクTextをアニメさせたいなら、既存の PunchScale を流用してもOK
            StartCoroutine(PunchScale(rankText.rectTransform, 1.08f, 0.28f));
        }
        if (tintScoreWithRank && scoreText)
        {
            scoreText.color = rank.color;
        }

        // スポーン停止
        if (spawner) spawner.StopSpawning(true);  

        // プレイヤーの物理は一旦停止（Transformはその場に）
        playerRb.linearVelocity = Vector2.zero;
        playerRb.angularVelocity = 0f;
        playerRb.simulated = false;

        // 中央パネル（再プレイボタンでのみ復帰）
        if (scoreText)   scoreText.text = $"{score:0.0} m";
        if (messageText) messageText.text = "Thank you for playing!";
        StartCoroutine(FadeIn(timeUpPanel, panelFadeTime));
        if (scoreText) StartCoroutine(PunchScale(scoreText.rectTransform, 1.15f, 0.35f));
    }

    public void Restart()
    {
        GameAudio.Instance?.PlayUiClick();
        // 「もう一度遊ぶ」→ シーン再読込で確実に初期化
        var active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex);
    }

    RankBand EvaluateRank(float score)
    {
        if (rankBands == null || rankBands.Count == 0)
        {
            // バックアップ：下限0の Beginner 緑
            return new RankBand { minScore = 0f, label = "Beginner", color = new Color(0.25f,0.9f,0.4f) };
        }

        // minScore が小さい順に並んでいる前提。最大の minScore <= score を採用
        RankBand best = rankBands[0];
        for (int i = 0; i < rankBands.Count; i++)
        {
            if (score >= rankBands[i].minScore) best = rankBands[i];
            else break;
        }
        return best;
    }

    // ★ 送信本体（await で1回だけ送る）
    private async Task SendUnityroomScoreAsync(float score)
    {
        try
        {
            var res = await _urClient.Scoreboards.SendAsync(new()
            {
                ScoreboardId = unityroomScoreboardId,
                Score = score,      // 降順/昇順はunityroom側のボード設定に従う
            });
            if (res.ScoreUpdated)
                Debug.Log($"[unityroom] Score updated: {score}");
            else
                Debug.Log($"[unityroom] Score sent but not updated: {score}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[unityroom] Send failed: {ex.Message}");
        }
    }
}
