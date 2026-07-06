using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class TouchPanelManager : MonoBehaviour
{
    public bool IsP1 = true;
    public Collider[] PanelColliders { get; private set; }
    public int Area { get; private set; }
    public bool IsTouched { get; private set; }
    public Collider LastTouchingHandCollider { get; set; }

    void Awake()
    {
        PanelColliders = GetComponentsInChildren<Collider>(true);
        if (PanelColliders == null || PanelColliders.Length == 0)
        {
            DiagnosticLogger.Warn($"[TouchPanelManager] '{gameObject.name}' 无 Collider");
        }
        else
        {
            foreach (var collider in PanelColliders)
            {
                if (collider != null)
                    collider.isTrigger = true;
            }
        }

        Area = ParseArea();
        TouchPanelPoller.Instance.RegisterPanel(this);

        // ── 诊断：启动时打印 collider 配置 ──
        DumpDiagnosticInfo();
    }

    private void DumpDiagnosticInfo()
    {
        if (PanelColliders == null || PanelColliders.Length == 0) return;

        DiagnosticLogger.Info($"[TouchPanelManager] 🏷️ REGISTERED | name={gameObject.name} area={Area} isP1={IsP1} " +
            $"colliderCount={PanelColliders.Length}");

        foreach (var c in PanelColliders)
        {
            if (c == null) continue;
            Vector3 ctr = c.bounds.center;
            Vector3 xf = c.transform.position;
            float offset = Vector3.Distance(ctr, xf);
            DiagnosticLogger.Info($"[TouchPanelManager]   └─ collider='{c.name}' type={c.GetType().Name} " +
                $"isTrigger={c.isTrigger} enabled={c.enabled} " +
                $"boundsSize=({c.bounds.size.x:F3},{c.bounds.size.y:F3},{c.bounds.size.z:F3}) " +
                $"boundsCtr=({ctr.x:F3},{ctr.y:F3},{ctr.z:F3}) " +
                $"xfPos=({xf.x:F3},{xf.y:F3},{xf.z:F3}) " +
                $"ctrOffset={offset:F4}m");
        }
    }

    void OnDestroy()
    {
        if (TouchPanelPoller.HasInstance)
            TouchPanelPoller.Instance.UnregisterPanel(this);
    }

    internal void UpdateTouchState(bool touched, Collider touchingHandCollider = null)
    {
        if (touched)
        {
            if (touchingHandCollider != null)
                LastTouchingHandCollider = touchingHandCollider;
        }
        else
        {
            LastTouchingHandCollider = null;
        }

        bool stateChanged = IsTouched != touched;
        IsTouched = touched;

        if (stateChanged)
        {
            SerialManager.ChangeTouch(IsP1, Area, touched);
        }
    }

    public bool HasAnyCollider()
    {
        return PanelColliders != null && PanelColliders.Length > 0;
    }

    private int ParseArea()
    {
        if (System.Enum.TryParse<TouchArea>(gameObject.name, true, out var result))
            return (int)result;

        DiagnosticLogger.Warn($"[TouchPanelManager] 未知分区名 '{gameObject.name}'，默认为 0");
        return 0;
    }

    private enum TouchArea
    {
        A1 = 0, A2 = 1, A3 = 2, A4 = 3, A5 = 4,
        A6 = 8, A7 = 9, A8 = 10, B1 = 11, B2 = 12,
        B3 = 16, B4 = 17, B5 = 18, B6 = 19, B7 = 20,
        B8 = 24, C1 = 25, C2 = 26, D1 = 27, D2 = 28,
        D3 = 32, D4 = 33, D5 = 34, D6 = 35, D7 = 36,
        D8 = 40, E1 = 41, E2 = 42, E3 = 43, E4 = 44,
        E5 = 48, E6 = 49, E7 = 50, E8 = 51,
    }
}
