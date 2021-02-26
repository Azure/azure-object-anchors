// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System.Collections.Concurrent;
using System.Linq;

using UnityEngine;
using UnityEngine.UI;

public class TextLogger : MonoBehaviour
{
    private static TextLogger Instance;
    private const int MaxMessageCountToShow = 8;

    public Text LoggerText;

    private ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    void Start()
    {
    }

    void OnEnable()
    {
        Application.logMessageReceivedThreaded += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceivedThreaded -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        // Do nothing here, as this handler could be called from a non-UI thread.
    }

    private void ShowMessage()
    {
        LoggerText.text = string.Empty;

        foreach (var item in _messageQueue.Skip(System.Math.Max(0, _messageQueue.Count - MaxMessageCountToShow)))
        {
            LoggerText.text += $"{item}\n";
        }
    }

    /// <summary>
    /// Log message without adding timestamp.
    /// </summary>
    public static void LogRaw(string message)
    {
        while (Instance._messageQueue.Count >= MaxMessageCountToShow)
        {
            string _message;
            Instance._messageQueue.TryDequeue(out _message);
        }

        Instance._messageQueue.Enqueue(message);
        Instance.ShowMessage();
    }

    public static void Log(string message)
    {
        LogRaw($"[{System.DateTime.Now.ToLongTimeString()}] {message}");
    }

    public static string Truncate(string source, int length)
    {
        if (source.Length > length)
        {
            source = source.Substring(0, length);
        }

        return source;
    }

}