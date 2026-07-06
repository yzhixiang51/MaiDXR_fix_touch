using UnityEngine;
using System.IO.Ports;
using System;
using System.Collections;
using System.Threading;
public class SerialManager : MonoBehaviour
{
    private static SerialManager instance;
    static SerialPort p1Serial = new SerialPort ("COM5", 9600);
    static SerialPort p2Serial = new SerialPort ("COM6", 9600);
    static byte[] settingPacket = new byte[6] {40, 0, 0, 0, 0, 41};
    static byte[] touchData = new byte[9] {40, 0, 0, 0, 0, 0, 0, 0, 41};
    static byte[] touchData2 = new byte[9] {40, 0, 0, 0, 0, 0, 0, 0, 41};
    public static bool startUp = false;
    private Thread touchThread;
    private volatile bool threadRunning;

    // ── 诊断 ──
    private static int writeCountP1;
    private static int writeCountP2;
    private static float statsTimer;

    void Start()
    {
        instance = this;
        try
        {
            DiagnosticLogger.Info("[SerialManager] 尝试打开 COM5/COM6 串口");
            p1Serial.Open();
            p2Serial.Open();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Error($"[SerialManager] 串口打开失败: {ex.Message}");
        }
        threadRunning = true;
        touchThread = new Thread(TouchThread);
        touchThread.Start();
        DiagnosticLogger.Info("[SerialManager] 串口线程已启动");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            startUp = !startUp;
    }

    /// <summary>
    /// 串口 I/O 线程 — 参考 maimai-android-touch-panel-rs 的架构：
    /// 1. 读取握手数据（6字节包）
    /// 2. 持续写入当前触摸状态（9字节包）
    /// 游戏期望的是持续的状态流，不是变化事件！
    /// </summary>
    private void TouchThread()
    {
        while (threadRunning)
        {
            // ── 读握手 ──
            if (p1Serial.IsOpen) ReadData(p1Serial);
            if (p2Serial.IsOpen) ReadData(p2Serial);

            // ── 持续写当前状态（有无变化都写）──
            if (startUp && p1Serial.IsOpen)
            {
                try { p1Serial.Write(touchData, 0, 9); writeCountP1++; }
                catch (Exception) { }
            }
            if (startUp && p2Serial.IsOpen)
            {
                try { p2Serial.Write(touchData2, 0, 9); writeCountP2++; }
                catch (Exception) { }
            }

            // ── 每秒统计 ──
            float now = Time.realtimeSinceStartup;
            if (now - statsTimer >= 1.0f)
            {
                DiagnosticLogger.FileOnly($"[SerialManager] 📡 writes/sec P1={writeCountP1} P2={writeCountP2}");
                writeCountP1 = 0;
                writeCountP2 = 0;
                statsTimer = now;
            }

            // 1ms 休眠 → 理论最大 ~1000 Hz，实际受串口速度限制 ~100 Hz
            Thread.Sleep(1);
        }
    }

    private void OnDestroy()
    {
        threadRunning = false;
        if (touchThread != null && touchThread.IsAlive)
        {
            touchThread.Join(500);
            if (touchThread.IsAlive) touchThread.Abort();
        }
        try { p1Serial.Close(); } catch { }
        try { p2Serial.Close(); } catch { }
    }

    private void ReadData(SerialPort Serial)
    {
        if (Serial.BytesToRead >= 6)
        {
            try
            {
                byte[] buf = new byte[6];
                Serial.Read(buf, 0, 6);
                TouchSetUp(Serial, buf);
            }
            catch (Exception) { }
        }
    }
    private void TouchSetUp(SerialPort Serial, byte[] data)
    {
        switch (data[3])
        {
            case 76: // 'L'
            case 69: // 'E'
                startUp = false;
                DiagnosticLogger.Info($"[SerialManager] 游戏断开连接 code={(char)data[3]}");
                break;
            case 114: // 'r'
            case 107: // 'k'
                for (int i = 1; i < 5; i++)
                    settingPacket[i] = data[i];
                try { Serial.Write(settingPacket, 0, settingPacket.Length); }
                catch (Exception) { }
                DiagnosticLogger.Info($"[SerialManager] 握手设置回显 code={(char)data[3]}");
                break;
            case 65: // 'A'
                startUp = true;
                DiagnosticLogger.Info($"[SerialManager] ✅ 游戏已连接，开始持续发送触摸数据");
                break;
        }
    }

    public static void ChangeTouch(bool isP1, int Area, bool State)
    {
        if (isP1)
            ByteArrayExt.SetBit(touchData, Area + 8, State);
        else
            ByteArrayExt.SetBit(touchData2, Area + 8, State);
    }
}

public static class ByteArrayExt
{
    public static byte[] SetBit(this byte[] self, int index, bool value)
    {
        var bitArray = new BitArray(self);
        bitArray.Set(index, value);
        bitArray.CopyTo(self, 0);
        return self;
    }
}