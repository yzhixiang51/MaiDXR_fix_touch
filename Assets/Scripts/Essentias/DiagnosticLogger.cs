using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 统一诊断日志系统：同时输出到 Unity Console 和文件。
/// 日志目录由 config.json 的 "DiagnosticLogPath" 控制。
/// 线程安全（支持 TouchThread 等非主线程调用）。
/// </summary>
public static class DiagnosticLogger
{
    private static StreamWriter fileWriter;
    private static string logDir;
    private static string logFilePath;
    private static readonly object writeLock = new object();
    private static bool initialized;
    private static bool enabled = true;

    public static bool IsEnabled
    {
        get => enabled;
        set { enabled = value; if (!value) Flush(); }
    }

    public static string CurrentLogPath => logFilePath;

    public static void Init()
    {
        if (initialized) return;
        initialized = true;

        // 默认关闭日志; 默认路径用相对路径 ./log, 避免绝对路径泄漏到 config.json
        enabled = JsonConfig.GetIntWithDefault("DiagnosticLogEnabled", 0) != 0;
        logDir = JsonConfig.GetStringWithDefault("DiagnosticLogPath", "./log");

        if (!enabled)
        {
            Debug.Log("[DiagnosticLogger] 日志已禁用 (DiagnosticLogEnabled=0)");
            return;
        }

        try
        {
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFilePath = Path.Combine(logDir, $"maiDXR_diag_{timestamp}.log");

            fileWriter = new StreamWriter(logFilePath, false, Encoding.UTF8);
            fileWriter.AutoFlush = false; // 手动 flush，减少 IO

            WriteLine($"=== MaiDXR Diagnostic Log ===");
            WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            WriteLine($"LogDir:  {logDir}");
            WriteLine($"=================================");

            Debug.Log($"[DiagnosticLogger] 日志文件: {logFilePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DiagnosticLogger] 初始化失败: {ex.Message}");
            fileWriter = null;
        }
    }

    public static void Shutdown()
    {
        lock (writeLock)
        {
            if (fileWriter != null)
            {
                WriteLine($"=================================");
                WriteLine($"Shutdown: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                fileWriter.Flush();
                fileWriter.Close();
                fileWriter = null;
            }
        }
    }

    /// <summary>普通信息日志</summary>
    public static void Info(string message)
    {
        if (!enabled) return;
        EnsureInit();
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [INFO] {message}";
        Debug.Log(line);
        WriteLine(line);
    }

    /// <summary>警告日志</summary>
    public static void Warn(string message)
    {
        if (!enabled) return;
        EnsureInit();
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [WARN] {message}";
        Debug.LogWarning(line);
        WriteLine(line);
    }

    /// <summary>错误日志</summary>
    public static void Error(string message)
    {
        if (!enabled) return;
        EnsureInit();
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] {message}";
        Debug.LogError(line);
        WriteLine(line);
    }

    /// <summary>仅文件日志（不刷 Unity Console，避免刷屏）</summary>
    public static void FileOnly(string message)
    {
        if (!enabled) return;
        EnsureInit();
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [TRACE] {message}";
        WriteLine(line);
    }

    private static void EnsureInit()
    {
        if (!initialized) Init();
    }

    private static void WriteLine(string line)
    {
        lock (writeLock)
        {
            if (fileWriter != null)
            {
                try
                {
                    fileWriter.WriteLine(line);
                }
                catch (Exception)
                {
                    // 写入失败时静默丢弃，避免日志系统本身崩溃
                }
            }
        }
    }

    /// <summary>强制 flush 缓冲区到磁盘（建议定期调用，如每秒一次）</summary>
    public static void Flush()
    {
        lock (writeLock)
        {
            try { fileWriter?.Flush(); }
            catch { }
        }
    }
}
