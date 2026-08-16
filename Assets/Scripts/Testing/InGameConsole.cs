using UnityEngine;
using TMPro;
using System.Text;
using System;   // 必须引入，用于 StringSplitOptions

public class InGameConsoleTMP : MonoBehaviour
{
    [Header("UI 引用")]
    public TextMeshProUGUI logText;          // 拖拽你的 TMP_Text 组件

    [Header("显示设置")]
    [Tooltip("最多保留多少行日志")]
    public int maxLines = 10;                // 默认只显示最新 10 行

    // 高效字符串拼接
    private StringBuilder stringBuilder = new StringBuilder();

    private void Awake()
    {
        if (logText == null)
        {
            Debug.LogError("InGameConsoleTMP: 未指定 logText 引用！");
            return;
        }

        // 默认隐藏
        logText.gameObject.SetActive(false);

        // 注册日志回调
        Application.logMessageReceived += OnLogReceived;
    }

    private void Update()
    {
        // 按键盘 7 键切换显示/隐藏
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            bool isActive = logText.gameObject.activeSelf;
            logText.gameObject.SetActive(!isActive);
        }
    }

    private void OnLogReceived(string logString, string stackTrace, LogType type)
    {
        // 根据日志类型添加颜色标签
        string colorTag = GetColorTag(type);
        string entry = $"<{colorTag}>[{type}] {logString}</color>\n";

        // 新日志插入到最前面（最新消息在上方）
        stringBuilder.Insert(0, entry);

        // 按换行分割，并忽略末尾可能的空项，精确统计非空行数
        string[] lines = stringBuilder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > maxLines)
        {
            // 只保留最新的 maxLines 行（lines[0] 是最新插入的）
            string newText = string.Join("\n", lines, 0, maxLines);
            // 保持末尾换行，与原始格式一致
            if (!newText.EndsWith("\n"))
                newText += "\n";

            stringBuilder.Clear();
            stringBuilder.Append(newText);
        }

        // 更新 UI 文本
        logText.text = stringBuilder.ToString();
    }

    // 为不同日志类型返回对应的 TMP 颜色标签
    private string GetColorTag(LogType type)
    {
        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
                return "color=#FF4444";   // 红色
            case LogType.Warning:
                return "color=#FFAA00";   // 橙色
            case LogType.Assert:
                return "color=#FF8800";   // 橙色
            default:
                return "color=#FFFFFF";   // 白色（普通日志）
        }
    }

    private void OnDestroy()
    {
        // 取消注册，防止内存泄漏
        Application.logMessageReceived -= OnLogReceived;
    }
}