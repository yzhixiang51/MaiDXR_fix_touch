using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TouchPanelPoller : MonoBehaviour
{
    private static TouchPanelPoller instance;
    public static TouchPanelPoller Instance
    {
        get
        {
            if (instance == null)
            {
                var existing = FindObjectOfType<TouchPanelPoller>();
                if (existing != null)
                {
                    instance = existing;
                }
                else
                {
                    var go = new GameObject("TouchPanelPoller");
                    instance = go.AddComponent<TouchPanelPoller>();
                }
            }
            return instance;
        }
    }

    public static bool HasInstance => instance != null;

    [Tooltip("Hand colliders used to sample touch panel overlaps.")]
    public List<Collider> HandColliders = new List<Collider>();

    [Tooltip("Polling frequency in Hz for touch panel overlap detection.")]
    public float PollFrequency = 90f;

    private readonly List<TouchPanelManager> panels = new List<TouchPanelManager>();
    private readonly Dictionary<TouchPanelManager, bool> currentStates = new Dictionary<TouchPanelManager, bool>();
    private readonly Dictionary<TouchPanelManager, int> releaseFrameCounters = new Dictionary<TouchPanelManager, int>();
    private readonly Dictionary<Collider, ControllerHapticManager> handHaptics = new Dictionary<Collider, ControllerHapticManager>();
    private readonly HashSet<Collider> previousActiveHandColliders = new HashSet<Collider>();
    private readonly List<PanelTouchState> stateList = new List<PanelTouchState>();
    private float pollTimer;

    [Tooltip("Number of polling frames to keep touch state after overlap is lost. Set 0 to disable buffering.")]
    [Range(0, 5)]
    public int ReleaseBufferFrames = 2;

    // 触摸震动振幅（config: TouchHapticAmplitude，单位百分之一，默认 10→0.1）
    private float touchHapticAmplitude = 0.1f;

    // Buffer for Physics.OverlapSphereNonAlloc (34 panels × 2 hands = plenty)
    private Collider[] overlapBuffer = new Collider[64];

    [Header("Diagnostics")]
    [Tooltip("Log state transitions (touch start/release).")]
    public bool LogStateTransitions = true;
    [Tooltip("Log poll summary once per second.")]
    public bool LogPollSummary = true;

    private int diagnosticFrameCounter;
    private int diagnosticPollCounter;
    private float diagnosticFlushTimer;

    public IReadOnlyList<PanelTouchState> TouchStates => stateList;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
        {
            Destroy(this);
            return;
        }

        DontDestroyOnLoad(gameObject);
        DiagnosticLogger.Init();
    }

    void Start()
    {
        if (HandColliders.Count == 0)
            FindHandColliders();

        // ── 从 config.json 读取配置 ──
        PollFrequency = (float)JsonConfig.GetIntWithDefault("TouchPollFrequency", 90);
        ReleaseBufferFrames = JsonConfig.GetIntWithDefault("TouchReleaseBufferFrames", 2);
        ReleaseBufferFrames = Mathf.Clamp(ReleaseBufferFrames, 0, 5);
        touchHapticAmplitude = (float)JsonConfig.GetIntWithDefault("TouchHapticAmplitude", 10) / 100f;

        DiagnosticLogger.Info($"[TouchPanelPoller] 🖐️ Hands={HandColliders.Count} " +
            $"Poll={PollFrequency}Hz BufFrames={ReleaseBufferFrames} TouchAmp={touchHapticAmplitude:F2}");
    }

    void Update()
    {
        if (PollFrequency <= 0f)
            return;

        pollTimer += Time.deltaTime;
        float interval = 1f / PollFrequency;
        while (pollTimer >= interval)
        {
            pollTimer -= interval;
            PollTouchPanels();
        }

        // 每秒 flush 日志到磁盘
        diagnosticFlushTimer += Time.deltaTime;
        if (diagnosticFlushTimer >= 1.0f)
        {
            diagnosticFlushTimer = 0f;
            DiagnosticLogger.Flush();
        }
    }

    void OnDestroy()
    {
        if (instance == this)
            DiagnosticLogger.Shutdown();
    }

    /// <summary>
    /// 获取手 collider 的有效世界空间半径。
    /// 不从 local radius 推（场景中可能有大缩放导致 local radius 异常大），
    /// 直接从 bounds 计算——bounds 已含 transform scale，永远准确。
    /// </summary>
    private float GetHandRadius(Collider handCollider)
    {
        if (handCollider == null) return 0.04f;
        // 从世界空间包围盒推导：取 extents 的中位数（比 min 稳，比 max 小）
        var e = handCollider.bounds.extents;
        return Mathf.Min(e.x, e.y, e.z);
    }

    public void RegisterPanel(TouchPanelManager panel)
    {
        if (panel == null || panels.Contains(panel))
            return;

        panels.Add(panel);
        currentStates[panel] = panel.IsTouched;
        releaseFrameCounters[panel] = 0;
        UpdateStateList();
    }

    public void UnregisterPanel(TouchPanelManager panel)
    {
        if (panel == null)
            return;

        panels.Remove(panel);
        currentStates.Remove(panel);
        releaseFrameCounters.Remove(panel);
        UpdateStateList();
    }

    // ================================================================
    // 核心检测：Physics.OverlapSphere — 直接查手球体与面板 collider 的重叠
    // 无 margin、无偏移、无 ClosestPoint、无 ComputePenetration
    // ================================================================
    private void PollTouchPanels()
    {
        if (HandColliders.Count == 0) return;
        diagnosticPollCounter++;

        // ── 第一步：用 OverlapSphere 收集每只手触碰的所有面板 ──
        var panelsTouchedByHand = new Dictionary<Collider, HashSet<TouchPanelManager>>();
        int totalHits = 0;

        foreach (var hand in HandColliders)
        {
            if (hand == null || !hand.enabled) continue;
            Vector3 center = hand.bounds.center;
            float radius = GetHandRadius(hand);
            if (radius <= 0f) continue;

            int hitCount = Physics.OverlapSphereNonAlloc(center, radius, overlapBuffer,
                -1, QueryTriggerInteraction.Collide);

            var hitPanels = new HashSet<TouchPanelManager>();
            for (int i = 0; i < hitCount; i++)
            {
                var col = overlapBuffer[i];
                if (col == null) continue;
                var panel = col.GetComponentInParent<TouchPanelManager>();
                if (panel != null && panel.HasAnyCollider())
                {
                    hitPanels.Add(panel);
                    totalHits++;
                }
            }

            if (hitPanels.Count > 0)
                panelsTouchedByHand[hand] = hitPanels;
        }

        // ── 第二步：确定每个面板被哪只手触摸（先到先得） ──
        var panelToHand = new Dictionary<TouchPanelManager, Collider>();
        foreach (var kv in panelsTouchedByHand)
        {
            foreach (var panel in kv.Value)
            {
                if (!panelToHand.ContainsKey(panel))
                    panelToHand[panel] = kv.Key;
            }
        }

        // ── 第三步：状态更新 ──
        var newActiveHandColliders = new HashSet<Collider>();
        int touchedThisPoll = 0;
        int untouchThisPoll = 0;

        foreach (var panel in panels)
        {
            if (panel == null || !panel.HasAnyCollider()) continue;

            bool isTouchedNow = panelToHand.ContainsKey(panel);
            Collider hand = isTouchedNow ? panelToHand[panel] : null;

            bool wasTouched = currentStates.TryGetValue(panel, out var prev) && prev;

            if (isTouchedNow)
            {
                touchedThisPoll++;
                panel.UpdateTouchState(true, hand);
                releaseFrameCounters[panel] = 0;
                currentStates[panel] = true;

                if (hand != null) newActiveHandColliders.Add(hand);

                if (LogStateTransitions && !wasTouched)
                    DiagnosticLogger.Info($"[TouchPanelPoller] ✅ TOUCH START | panel={panel.name} area={panel.Area} " +
                        $"hand={hand?.name} isP1={panel.IsP1} frame={Time.frameCount}");
            }
            else if (wasTouched)
            {
                untouchThisPoll++;
                if (ReleaseBufferFrames > 0)
                {
                    releaseFrameCounters[panel] += 1;
                    if (releaseFrameCounters[panel] >= ReleaseBufferFrames)
                    {
                        panel.UpdateTouchState(false);
                        currentStates[panel] = false;
                        releaseFrameCounters[panel] = 0;
                        if (LogStateTransitions)
                            DiagnosticLogger.Info($"[TouchPanelPoller] ❌ TOUCH RELEASE (buf={ReleaseBufferFrames}) | " +
                                $"panel={panel.name} area={panel.Area} isP1={panel.IsP1} frame={Time.frameCount}");
                    }
                    else
                    {
                        // 缓冲中保持触摸
                        panel.UpdateTouchState(true, panel.LastTouchingHandCollider);
                        if (panel.LastTouchingHandCollider != null)
                            newActiveHandColliders.Add(panel.LastTouchingHandCollider);
                    }
                }
                else
                {
                    panel.UpdateTouchState(false);
                    currentStates[panel] = false;
                    releaseFrameCounters[panel] = 0;
                    if (LogStateTransitions)
                        DiagnosticLogger.Info($"[TouchPanelPoller] ❌ TOUCH RELEASE (no buf) | " +
                            $"panel={panel.name} area={panel.Area} frame={Time.frameCount}");
                }
            }
            else if (!currentStates.ContainsKey(panel))
            {
                panel.UpdateTouchState(false);
                currentStates[panel] = false;
                releaseFrameCounters[panel] = 0;
            }
        }

        // ── 诊断 ──
        if (LogPollSummary)
        {
            diagnosticFrameCounter++;
            DiagnosticLogger.FileOnly($"[TouchPanelPoller] 📊 F#{diagnosticFrameCounter} | " +
                $"frame={Time.frameCount} poll=#{diagnosticPollCounter} | " +
                $"overlapHits={totalHits} touched={touchedThisPoll} untouch={untouchThisPoll} | " +
                $"hands={newActiveHandColliders.Count}");
        }

        UpdateHandHaptics(newActiveHandColliders);
        UpdateStateList();
    }

    private void UpdateStateList()
    {
        stateList.Clear();
        foreach (var kvp in currentStates)
            stateList.Add(new PanelTouchState(kvp.Key, kvp.Value));
    }

    private void FindHandColliders()
    {
        var colliders = FindObjectsOfType<Collider>();
        foreach (var collider in colliders)
        {
            var lowerName = collider.gameObject.name.ToLowerInvariant();
            if (lowerName.Contains("lhand") || lowerName.Contains("rhand") || lowerName.Contains("lefthand") || lowerName.Contains("righthand") || lowerName.Contains("left hand") || lowerName.Contains("right hand"))
            {
                HandColliders.Add(collider);
            }
        }

        if (HandColliders.Count == 0)
            DiagnosticLogger.Warn("[TouchPanelPoller] 未自动发现手 collider，请在 Inspector 中手动指定 HandColliders");
    }

    private void UpdateHandHaptics(HashSet<Collider> newActiveHandColliders)
    {
        foreach (var handCollider in newActiveHandColliders)
        {
            if (!previousActiveHandColliders.Contains(handCollider))
                StartHandHaptic(handCollider);
        }

        foreach (var handCollider in previousActiveHandColliders)
        {
            if (!newActiveHandColliders.Contains(handCollider))
                StopHandHaptic(handCollider);
        }

        previousActiveHandColliders.Clear();
        foreach (var handCollider in newActiveHandColliders)
            previousActiveHandColliders.Add(handCollider);
    }

    private void StartHandHaptic(Collider handCollider)
    {
        var hapticManager = GetHapticManager(handCollider);
        if (hapticManager == null) return;
        hapticManager.touchAmplitude = touchHapticAmplitude;
        hapticManager.StartTouchHaptic();
    }

    private void StopHandHaptic(Collider handCollider)
    {
        var hapticManager = GetHapticManager(handCollider);
        if (hapticManager == null) return;
        hapticManager.StopTouchHaptic();
    }

    private ControllerHapticManager GetHapticManager(Collider handCollider)
    {
        if (handCollider == null)
            return null;

        if (handHaptics.TryGetValue(handCollider, out var manager) && manager != null)
            return manager;

        manager = handCollider.GetComponentInParent<ControllerHapticManager>();
        if (manager == null && handCollider.transform.parent != null)
            manager = handCollider.transform.parent.GetComponentInParent<ControllerHapticManager>();

        handHaptics[handCollider] = manager;
        return manager;
    }

    [Serializable]
    public readonly struct PanelTouchState
    {
        public TouchPanelManager Panel { get; }
        public bool IsTouched { get; }

        public PanelTouchState(TouchPanelManager panel, bool isTouched)
        {
            Panel = panel;
            IsTouched = isTouched;
        }
    }
}
