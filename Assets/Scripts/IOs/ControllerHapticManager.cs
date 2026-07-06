using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 控制器震动管理 — 两种模式：
/// 1. 按键碰撞：OnTriggerEnter 触发单次强震（amplitude=1.0，原版行为）
/// 2. 触摸面板：StartTouchHaptic/StopTouchHaptic 持续弱震（amplitude 可配置，默认 0.3）
/// </summary>
public class ControllerHapticManager : MonoBehaviour
{
    public XRNode Hand;
    InputDevice device;

    // 按键震动参数（单次强震）
    public float buttonAmplitude = 1.0f;
    public float buttonDuration = 0.1f;

    // 触摸震动参数（持续弱震）
    public float touchAmplitude = 0.3f;
    public float touchFrequency = 30f;

    public bool ContinuousHaptic { get; private set; }
    private float hapticTimer;

    private void Awake()
    {
        device = InputDevices.GetDeviceAtXRNode(Hand);
    }

    private void Update()
    {
        if (!ContinuousHaptic) return;

        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(Hand);
        if (!device.isValid) return;

        hapticTimer += Time.deltaTime;
        float interval = 1f / Mathf.Max(1f, touchFrequency);
        while (hapticTimer >= interval)
        {
            device.SendHapticImpulse(0, touchAmplitude, 0.02f);
            hapticTimer -= interval;
        }
    }

    // ── 按键碰撞：原版 OnTriggerEnter 单次强震 ──
    private void OnTriggerEnter(Collider other)
    {
        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(Hand);
        if (device.isValid)
            device.SendHapticImpulse(0, buttonAmplitude, buttonDuration);
    }

    private void OnTriggerExit(Collider other)
    {
        // 原版按键释放时停止，但不影响触摸震动
        if (!ContinuousHaptic)
        {
            if (!device.isValid)
                device = InputDevices.GetDeviceAtXRNode(Hand);
            if (device.isValid)
                device.StopHaptics();
        }
    }

    // ── 触摸面板：持续弱震 ──
    public void StartTouchHaptic()
    {
        if (ContinuousHaptic) return;
        ContinuousHaptic = true;
        hapticTimer = 0f;
    }

    public void StopTouchHaptic()
    {
        ContinuousHaptic = false;
        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(Hand);
        if (device.isValid)
            device.StopHaptics();
    }
}
