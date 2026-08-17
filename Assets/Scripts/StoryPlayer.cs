using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ExcelDataReader;
using DG.Tweening;
using System.Text;
public class StoryPlayer : MonoBehaviour
{
    [Header("管理器引用（必须在场景中指定）")]
    [SerializeField] private BackgroundManager bgManager;
    [SerializeField] private CharacterManager charManager;
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private EffectManager effectManager;

    [Header("Excel读取设置")]
    public string excelFilePath;
    public string excelSheetName = "Sheet1";

    [Header("调试设置")]
    public bool printCommands = true;
    public bool autoStart = true;

    [Header("点击继续设置")]
    public bool enableMouseClickContinue = true;

    [Header("自动播放")]
    public float autoPlayDelay = 1.0f;

    [Header("快进")]
    public float fastForwardInterval = 0.25f;

    private static readonly HashSet<string> validTypes = new HashSet<string>
    {
        "背景控制", "立绘控制", "对话控制", "延时", "声音控制", "分支控制", "特效控制"
    };

    private static readonly HashSet<string> pauseActions = new HashSet<string>
    {
        "展示文本", "角色说话"
    };

    private List<StoryCommand> commands = new List<StoryCommand>();
    private bool isWaitingForContinue = false;
    private bool isPlaying = false;
    private bool isSkipping = false;
    private int currentCommandIndex = 0;
    private Coroutine playCoroutine = null;
    private bool isLoadResume = false;
    private Dictionary<string, int> labelIndexMap;
    private HashSet<string> conditions;
    private bool isWaitingForChoice = false;
    private string[] currentChoiceOptions;
    private string[] currentChoiceConditions;
    private int currentChoiceCount = 0;
    private bool isAutoPlay = false;
    private Coroutine autoPlayCoroutine = null;
    private bool isFastForward = false;
    private float fastForwardTimer = 0f;
    private float clickCooldown = 0.2f;
    private float lastClickTime = -1f;

    private void Awake()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    void Start()
    {
        if (autoStart)
        {
            StartStory(); // 由 StartStory 内部处理路径选择与检查
        }
    }

    void Update()
    {
        HandleChoiceInput();

        if (Input.GetKeyDown(KeyCode.Alpha1))
            HandleToggleTextBox();

        if (Input.GetKeyDown(KeyCode.Alpha2))
            HandleSkipInput();

        if (Input.GetKeyDown(KeyCode.Alpha3))
            HandleSaveInput();

        if (Input.GetKeyDown(KeyCode.Alpha4))
            HandleLoadInput();

        if (Input.GetKeyDown(KeyCode.Alpha5))
            HandleAutoPlayToggleInput();

        if (Input.GetKeyDown(KeyCode.Alpha6))
            HandleFastForwardToggleInput();

        if (enableMouseClickContinue && Input.GetMouseButtonDown(0))
            HandleMouseClickContinue();

        // ---- demo测试代码：按 ESC 退出程序 ----
        if (Input.GetKeyDown(KeyCode.Escape))
        {
#if UNITY_EDITOR
            // 在编辑器中停止运行
            UnityEditor.EditorApplication.isPlaying = false;
#else
        // 在 Build 后的软件中退出
        Application.Quit();
#endif
            Debug.Log("StoryPlayer: 按 ESC 退出程序");
        }

        HandleAutoPlayLogic();
        HandleFastForwardLogic();
    }

    // ============================================================
    // 按键处理方法
    // ============================================================

    private void HandleChoiceInput()
    {
        if (!isWaitingForChoice) return;

        int choiceIndex = -1;
        if (Input.GetKeyDown(KeyCode.Q)) choiceIndex = 0;
        else if (Input.GetKeyDown(KeyCode.W)) choiceIndex = 1;
        else if (Input.GetKeyDown(KeyCode.E)) choiceIndex = 2;

        if (choiceIndex >= 0 && choiceIndex < currentChoiceCount)
        {
            string condition = currentChoiceConditions[choiceIndex];
            if (!string.IsNullOrEmpty(condition) && condition != "无")
            {
                conditions.Add(condition);
                Debug.Log($"StoryPlayer: 选择选项 {choiceIndex + 1}，达成条件 '{condition}'");
            }
            else
            {
                Debug.Log($"StoryPlayer: 选择选项 {choiceIndex + 1}，无条件");
            }

            dialogueManager.HideOptions();
            ContinueStory();
        }
    }

    private void HandleToggleTextBox()
    {
        if (!isWaitingForContinue && !isWaitingForChoice) return;
        if (dialogueManager == null) return;

        bool isVisible = dialogueManager.gameObject.activeSelf;
        if (isVisible)
        {
            dialogueManager.HideTextBox();
            Debug.Log("StoryPlayer: 文本框已手动隐藏（按 1 恢复）");
        }
        else
        {
            dialogueManager.ShowTextBox();
            Debug.Log("StoryPlayer: 文本框已恢复显示");
        }
    }

    private void HandleSkipInput()
    {
        if (!isPlaying || isSkipping || isWaitingForContinue || isWaitingForChoice) return;
        Skip();
    }

    private void HandleSaveInput()
    {
        if (!isWaitingForContinue && !isWaitingForChoice) return;
        SaveGame();
    }

    private void HandleLoadInput()
    {
        LoadGame();
    }

    private void HandleAutoPlayToggleInput()
    {
        ToggleAutoPlay();
    }

    private void HandleFastForwardToggleInput()
    {
        isFastForward = !isFastForward;
        if (isFastForward)
            fastForwardTimer = 0f;
        Debug.Log($"StoryPlayer: 快进 {(isFastForward ? "开启" : "关闭")}");
    }

    private void HandleMouseClickContinue()
    {
        if (!isPlaying || !isWaitingForContinue || isWaitingForChoice) return;

        bool textBoxVisible = dialogueManager != null && dialogueManager.gameObject.activeSelf;
        if (!textBoxVisible) return;

        if (dialogueManager != null && !dialogueManager.IsTypingComplete) return;

        if (Time.time - lastClickTime < clickCooldown) return;

        lastClickTime = Time.time;
        StopAutoPlayCoroutine();
        ContinueStory();
    }

    private void HandleAutoPlayLogic()
    {
        if (!isAutoPlay || !isPlaying || !isWaitingForContinue || isWaitingForChoice) return;
        if (dialogueManager == null || !dialogueManager.IsTypingComplete) return;
        if (autoPlayCoroutine != null) return;

        autoPlayCoroutine = StartCoroutine(AutoPlayDelay());
    }

    private void HandleFastForwardLogic()
    {
        if (!isFastForward || !isPlaying) return;
        if (isWaitingForChoice) return;

        fastForwardTimer += Time.deltaTime;
        if (fastForwardTimer >= fastForwardInterval)
        {
            fastForwardTimer = 0f;
            if (isWaitingForContinue && !isWaitingForChoice)
            {
                HandleMouseClickContinue();
            }
            if (!isWaitingForContinue && !isWaitingForChoice && !isSkipping)
            {
                HandleSkipInput();
            }
        }
    }

    // ============================================================
    // 自动播放相关
    // ============================================================

    private void ToggleAutoPlay()
    {
        isAutoPlay = !isAutoPlay;
        if (!isAutoPlay)
        {
            StopAutoPlayCoroutine();
        }
        Debug.Log($"StoryPlayer: 自动播放 {(isAutoPlay ? "开启" : "关闭")}");
    }

    private void StopAutoPlayCoroutine()
    {
        if (autoPlayCoroutine != null)
        {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }
    }

    private IEnumerator AutoPlayDelay()
    {
        yield return new WaitForSeconds(autoPlayDelay);
        if (isAutoPlay && isPlaying && isWaitingForContinue && !isWaitingForChoice)
        {
            if (dialogueManager != null && dialogueManager.IsTypingComplete)
            {
                Debug.Log($"StoryPlayer: 自动播放延时结束，继续剧情");
                ContinueStory();
            }
        }
        autoPlayCoroutine = null;
    }

    // ============================================================
    // 公共方法（原有）
    // ============================================================

    public void StartStory(string filePath = null)
    {
        if (isPlaying)
        {
            Debug.LogWarning("剧情正在播放，请勿重复启动");
            return;
        }

        if (bgManager == null || charManager == null || dialogueManager == null || audioManager == null || effectManager == null)
        {
            Debug.LogError("StoryPlayer: 管理器引用未全部赋值");
            return;
        }

        // ---- demo 测试逻辑：确定 Excel 文件路径 ----
        string path;
        if (!string.IsNullOrEmpty(filePath))
        {
            // 如果外部传入了路径，优先使用
            path = filePath;
        }
        else if (Application.isEditor)
        {
            // 编辑器模式下，使用 Inspector 中填写的 excelFilePath
            path = excelFilePath;
        }
        else
        {
            // Build 后，读取 exe 同级目录下的 TestPlot.xlsx
            path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "演出配置.xlsx");
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogError($"StoryPlayer: 文件路径无效: {path}");
            return;
        }

        Debug.Log($"StoryPlayer: 开始解析 Excel 文件: {path}, Sheet: {excelSheetName}");

        if (!ParseExcel(path))
        {
            Debug.LogError("StoryPlayer: 解析Excel失败");
            return;
        }

        if (printCommands) PrintAllCommands();

        Preprocess();
        currentCommandIndex = 0;
        isLoadResume = false;
        PlayFromIndex(0);
    }

    public void ContinueStory()
    {
        if (isWaitingForContinue || isWaitingForChoice)
        {
            StopAutoPlayCoroutine();
            ClearAudioAndEffects();
            isWaitingForContinue = false;
            isWaitingForChoice = false;

            if (isLoadResume)
            {
                isLoadResume = false;
                PlayFromIndex(currentCommandIndex + 1);
            }
        }
    }

    public void Skip()
    {
        if (!isPlaying || isSkipping) return;

        Debug.Log("StoryPlayer: 开始跳过...");
        isSkipping = true;

        bgManager.IsSkipping = true;
        charManager.IsSkipping = true;
        dialogueManager.IsSkipping = true;

        bgManager.CompleteCurrent();
        charManager.CompleteAllCurrent();
        dialogueManager.CompleteCurrent();
        if (audioManager != null) audioManager.CompleteCurrent();
        if (effectManager != null) effectManager.CompleteCurrent();

        StopAutoPlayCoroutine();

        if (isWaitingForContinue)
        {
            isWaitingForContinue = false;
        }
        if (isWaitingForChoice)
        {
            if (currentChoiceCount > 0)
            {
                string condition = currentChoiceConditions[0];
                if (!string.IsNullOrEmpty(condition) && condition != "无")
                {
                    conditions.Add(condition);
                    Debug.Log($"StoryPlayer: Skip 自动选择选项1，达成条件 '{condition}'");
                }
                dialogueManager.HideOptions();
                ContinueStory();
            }
        }
    }

    // ============================================================
    // 存档与读档
    // ============================================================

    [System.Serializable]
    public class SaveData
    {
        public int commandIndex;
        public bool isWaiting;
        public BackgroundManager.BgSaveData bgData;
        public List<CharacterManager.CharSaveData> charData;
        public DialogueManager.DialogueSaveData dialogueData;
        public AudioManager.AudioSaveData audioData;
        public EffectManager.EffectSaveData effectData;
        public List<string> conditionsList;
        public bool isWaitingForChoice;
        public string[] choiceOptions;
        public string[] choiceConditions;
        public int choiceCount;
        public string lightType;
    }

    public void SaveGame()
    {
        if (!isWaitingForContinue && !isWaitingForChoice)
        {
            Debug.LogWarning("存档只能在等待继续或等待选择时进行");
            return;
        }

        SaveData data = new SaveData();
        data.commandIndex = currentCommandIndex;
        data.isWaiting = isWaitingForContinue || isWaitingForChoice;
        data.bgData = bgManager.GetSaveData();
        data.charData = charManager.GetSaveData();
        data.dialogueData = dialogueManager.GetSaveData();
        data.audioData = audioManager.GetSaveData();
        data.effectData = effectManager.GetSaveData();
        data.conditionsList = new List<string>(conditions);
        data.lightType = charManager.CurrentLightType;

        data.isWaitingForChoice = isWaitingForChoice;
        data.choiceOptions = currentChoiceOptions;
        data.choiceConditions = currentChoiceConditions;
        data.choiceCount = currentChoiceCount;

        string json = JsonUtility.ToJson(data, true);
        string path = Application.persistentDataPath + "/save.json";
        File.WriteAllText(path, json);
        Debug.Log($"存档已保存至: {path}");
    }

    public void LoadGame()
    {
        if (commands.Count == 0)
        {
            Debug.LogWarning("尚未加载任何剧情数据，请先开始播放剧情");
            return;
        }

        string path = Application.persistentDataPath + "/save.json";
        if (!File.Exists(path))
        {
            Debug.LogWarning("存档文件不存在");
            return;
        }

        string json = File.ReadAllText(path);
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        if (data == null)
        {
            Debug.LogError("读取存档失败，JSON格式错误");
            return;
        }

        if (playCoroutine != null)
        {
            StopCoroutine(playCoroutine);
            playCoroutine = null;
        }
        isPlaying = false;
        isWaitingForContinue = false;
        isWaitingForChoice = false;
        isSkipping = false;
        isLoadResume = false;
        bgManager.IsSkipping = false;
        charManager.IsSkipping = false;
        dialogueManager.IsSkipping = false;

        isAutoPlay = false;
        StopAutoPlayCoroutine();

        isFastForward = false;
        fastForwardTimer = 0f;

        bgManager.LoadSaveData(data.bgData);
        charManager.LoadSaveData(data.charData);
        if (!string.IsNullOrEmpty(data.lightType))
            charManager.ApplyMaterialProperties(data.lightType);
        dialogueManager.LoadSaveData(data.dialogueData);
        if (audioManager != null && data.audioData != null)
            audioManager.LoadSaveData(data.audioData);
        if (effectManager != null && data.effectData != null)
            effectManager.LoadSaveData(data.effectData);

        if (data.conditionsList != null)
            conditions = new HashSet<string>(data.conditionsList);
        else
            conditions = new HashSet<string>();

        currentCommandIndex = data.commandIndex;
        isWaitingForContinue = data.isWaiting;
        isWaitingForChoice = data.isWaitingForChoice;
        if (isWaitingForChoice)
        {
            currentChoiceOptions = data.choiceOptions;
            currentChoiceConditions = data.choiceConditions;
            currentChoiceCount = data.choiceCount;
            if (currentChoiceOptions != null && currentChoiceOptions.Length > 0)
            {
                dialogueManager.ShowOptions(currentChoiceOptions, dialogueManager.GetSaveData().speaker);
            }
        }

        if (isWaitingForContinue || isWaitingForChoice)
        {
            isLoadResume = true;
            isPlaying = true;
            Debug.Log($"读档完成，已恢复等待状态 (索引 {currentCommandIndex})，点击继续或选择选项");
        }
        else
        {
            PlayFromIndex(currentCommandIndex);
            Debug.Log($"读档完成，从索引 {currentCommandIndex} 继续播放");
        }
    }

    private void PlayFromIndex(int startIndex)
    {
        if (playCoroutine != null)
            StopCoroutine(playCoroutine);
        playCoroutine = StartCoroutine(ExecuteCommands(startIndex));
    }

    // ============================================================
    // 解析Excel
    // ============================================================
    private bool ParseExcel(string filePath)
    {
        try
        {
            commands.Clear();

            var config = new ExcelReaderConfiguration()
            {
                FallbackEncoding = System.Text.Encoding.UTF8
            };

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream, config))
            {
                bool foundTargetSheet = false;
                int rowIndex = 0;

                do
                {
                    if (!string.IsNullOrEmpty(excelSheetName) && reader.Name != excelSheetName)
                    {
                        continue;
                    }

                    foundTargetSheet = true;
                    bool isFirstRow = true;

                    while (reader.Read())
                    {
                        rowIndex++;
                        if (isFirstRow)
                        {
                            isFirstRow = false;
                            continue;
                        }

                        string type = reader.GetValue(0)?.ToString()?.Trim();
                        if (string.IsNullOrEmpty(type)) continue;

                        if (!validTypes.Contains(type))
                        {
                            Debug.Log($"StoryPlayer: 跳过无效指令类型 '{type}' (行号: {rowIndex})");
                            continue;
                        }

                        var cmd = new StoryCommand
                        {
                            Type = type,
                            Delay = ParseFloat(reader.GetValue(1)),
                            ResourceName = reader.GetValue(2)?.ToString()?.Trim() ?? "",
                            Action = reader.GetValue(3)?.ToString()?.Trim() ?? "",
                            Param1 = reader.GetValue(4)?.ToString()?.Trim() ?? "",
                            Param2 = reader.GetValue(5)?.ToString()?.Trim() ?? ""
                        };
                        commands.Add(cmd);
                    }

                    if (foundTargetSheet)
                        break;

                } while (reader.NextResult());

                if (!foundTargetSheet && !string.IsNullOrEmpty(excelSheetName))
                {
                    Debug.LogError($"StoryPlayer: 未找到名为 '{excelSheetName}' 的Sheet，请检查名称");
                    return false;
                }
            }

            Debug.Log($"StoryPlayer: 成功解析 {commands.Count} 条指令 (Sheet: {excelSheetName})");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"StoryPlayer: 解析Excel出错: {e.Message}");
            return false;
        }
    }

    private void PrintAllCommands()
    {
        if (commands.Count == 0)
        {
            Debug.Log("StoryPlayer: 没有解析到任何指令");
            return;
        }

        Debug.Log("========== 解析到的指令列表 ==========");
        for (int i = 0; i < commands.Count; i++)
        {
            var c = commands[i];
            Debug.Log($"指令[{i}] 类型={c.Type} 延时={c.Delay} 资源={c.ResourceName} 动作={c.Action} 参数1={c.Param1} 参数2={c.Param2}");
        }
        Debug.Log($"====================================== 共 {commands.Count} 条");
    }

    private float ParseFloat(object val)
    {
        if (val == null) return 0f;
        string s = val.ToString().Trim();
        if (string.IsNullOrEmpty(s)) return 0f;
        if (float.TryParse(s, out float result)) return result;
        return 0f;
    }

    private Vector2 ParsePosition(string posStr)
    {
        if (string.IsNullOrEmpty(posStr)) return new Vector2(0, -300);
        string[] parts = posStr.Trim().Split(',');
        if (parts.Length == 2)
        {
            if (float.TryParse(parts[0].Trim(), out float x) && float.TryParse(parts[1].Trim(), out float y))
                return new Vector2(x, y);
        }
        return new Vector2(0, -300);
    }

    private Vector3 ParsePosition3D(string posStr)
    {
        if (string.IsNullOrEmpty(posStr)) return Vector3.zero;
        string[] parts = posStr.Trim().Split(',');
        if (parts.Length == 3)
        {
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
        }
        else if (parts.Length == 2)
        {
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y))
                return new Vector3(x, y, 0);
        }
        return Vector3.zero;
    }

    // ============================================================
    // 预处理
    // ============================================================
    private void Preprocess()
    {
        // ---- 背景资源 ----
        HashSet<string> bgSet = new HashSet<string>();
        foreach (var cmd in commands)
        {
            if (cmd.Type == "背景控制" && !string.IsNullOrEmpty(cmd.ResourceName))
            {
                if (cmd.Action == "淡入背景" || cmd.Action == "切换背景" || cmd.Action == "交叉背景")
                    bgSet.Add(cmd.ResourceName);
            }
        }
        string[] bgArray = new List<string>(bgSet).ToArray();
        bgManager.InitBackgrounds(bgArray);
        Debug.Log($"StoryPlayer: 背景初始化完成，共 {bgArray.Length} 张");

        // ---- 角色资源 ----
        Dictionary<string, (string resName, Vector2 pos)> roleDict = new Dictionary<string, (string, Vector2)>();
        foreach (var cmd in commands)
        {
            if (cmd.Type != "立绘控制") continue;
            if (string.IsNullOrEmpty(cmd.ResourceName)) continue;

            string resName = cmd.ResourceName;
            string action = cmd.Action;
            string param1 = cmd.Param1;
            string param2 = cmd.Param2;

            if (action == "创建角色")
            {
                string objName = string.IsNullOrEmpty(param1) ? resName : param1;
                Vector2 pos = ParsePosition(param2);
                roleDict[objName] = (resName, pos);
            }
            else if (action == "淡入角色")
            {
                string objName = resName;
                Vector2 pos = ParsePosition(param2);
                if (!roleDict.ContainsKey(objName))
                    roleDict[objName] = (resName, pos);
                else if (pos != new Vector2(0, -300))
                {
                    var existing = roleDict[objName];
                    roleDict[objName] = (existing.resName, pos);
                }
            }
        }

        int count = roleDict.Count;
        if (count > 0)
        {
            string[] resNames = new string[count];
            string[] objNames = new string[count];
            Vector2[] positions = new Vector2[count];
            int idx = 0;
            foreach (var kv in roleDict)
            {
                objNames[idx] = kv.Key;
                resNames[idx] = kv.Value.resName;
                positions[idx] = kv.Value.pos;
                idx++;
            }
            charManager.InitCharacters(resNames, positions, objNames);
            Debug.Log($"StoryPlayer: 角色初始化完成，共 {count} 个角色");
        }
        else
        {
            Debug.Log("StoryPlayer: 没有需要初始化的角色");
        }

        // ---- 音频资源 ----
        HashSet<string> audioSet = new HashSet<string>();
        foreach (var cmd in commands)
        {
            if (cmd.Type == "声音控制" && !string.IsNullOrEmpty(cmd.ResourceName) && (cmd.ResourceName != "无"))
            {
                audioSet.Add(cmd.ResourceName);
            }
        }
        string[] audioArray = new List<string>(audioSet).ToArray();
        if (audioManager != null)
        {
            audioManager.InitAudio(audioArray);
            Debug.Log($"StoryPlayer: 音频初始化完成，共 {audioArray.Length} 个音频");
        }
        else
        {
            Debug.LogWarning("StoryPlayer: audioManager 未赋值，无法初始化音频池");
        }

        // ---- 特效资源收集 ----
        HashSet<string> effectSet = new HashSet<string>();
        foreach (var cmd in commands)
        {
            if (cmd.Type == "特效控制" && !string.IsNullOrEmpty(cmd.ResourceName) && (cmd.ResourceName != "无"))
            {
                effectSet.Add(cmd.ResourceName);
            }
        }
        string[] effectArray = new List<string>(effectSet).ToArray();
        if (effectManager != null)
        {
            effectManager.InitEffects(effectArray);
            Debug.Log($"StoryPlayer: 特效初始化完成，共 {effectArray.Length} 个特效");
        }
        else
        {
            Debug.LogWarning("StoryPlayer: effectManager 未赋值，无法初始化特效池");
        }

        // ---- 分支控制：建立标记索引 ----
        labelIndexMap = new Dictionary<string, int>();
        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if (cmd.Type == "分支控制" && cmd.Action == "建立标记" && !string.IsNullOrEmpty(cmd.Param1))
            {
                string label = cmd.Param1.Trim();
                if (!labelIndexMap.ContainsKey(label))
                {
                    labelIndexMap.Add(label, i);
                }
                else
                {
                    Debug.LogWarning($"标记 '{label}' 重复定义，将忽略后续定义（索引 {i}）");
                }
            }
        }
        Debug.Log($"StoryPlayer: 共建立 {labelIndexMap.Count} 个标记");

        conditions = new HashSet<string>();
    }

    // ============================================================
    // 执行剧情
    // ============================================================
    private IEnumerator ExecuteCommands(int startIndex = 0)
    {
        isPlaying = true;
        Debug.Log($"StoryPlayer: 开始执行剧情（从索引 {startIndex}）...");

        int i = startIndex;
        while (i < commands.Count)
        {
            currentCommandIndex = i;
            StoryCommand cmd = commands[i];

            if (isSkipping && cmd.Type == "声音控制" && cmd.Action == "播放音效" && cmd.Param1 == "单次")
            {
                Debug.Log($"StoryPlayer: Skip 期间跳过单次音效指令 (索引 {i})");
                i++;
                continue;
            }

            // ---- 分支控制指令 ----
            if (cmd.Type == "分支控制")
            {
                bool branchHandled = false;
                switch (cmd.Action)
                {
                    case "建立标记":
                        branchHandled = true;
                        break;

                    case "达成条件":
                        if (!string.IsNullOrEmpty(cmd.Param1))
                        {
                            conditions.Add(cmd.Param1.Trim());
                            Debug.Log($"StoryPlayer: 达成条件 '{cmd.Param1}'");
                        }
                        branchHandled = true;
                        break;

                    case "无条件跳转":
                        if (!string.IsNullOrEmpty(cmd.ResourceName))
                        {
                            string targetLabel = cmd.ResourceName.Trim();
                            if (labelIndexMap.TryGetValue(targetLabel, out int targetIndex))
                            {
                                Debug.Log($"StoryPlayer: 无条件跳转到 '{targetLabel}' 索引 {targetIndex}");
                                i = targetIndex;
                                continue;
                            }
                            else
                            {
                                Debug.LogError($"StoryPlayer: 未找到标记 '{targetLabel}'");
                            }
                        }
                        branchHandled = true;
                        break;

                    case "有条件跳转":
                        string cond1 = cmd.Param1?.Trim();
                        string cond2 = cmd.Param2?.Trim();

                        bool cond1Met = string.IsNullOrEmpty(cond1) || cond1 == "无" || conditions.Contains(cond1);
                        bool cond2Met = string.IsNullOrEmpty(cond2) || cond2 == "无" || conditions.Contains(cond2);

                        if (cond1Met && cond2Met)
                        {
                            string targetLabel = cmd.ResourceName.Trim();
                            if (labelIndexMap.TryGetValue(targetLabel, out int targetIndex))
                            {
                                Debug.Log($"StoryPlayer: 条件满足，跳转到 '{targetLabel}' 索引 {targetIndex}");
                                i = targetIndex;
                                continue;
                            }
                            else
                            {
                                Debug.LogError($"StoryPlayer: 未找到标记 '{targetLabel}'");
                            }
                        }
                        else
                        {
                            Debug.Log($"StoryPlayer: 条件不满足，继续执行 (条件: {cond1}{(string.IsNullOrEmpty(cond2) ? "" : " & " + cond2)})");
                        }
                        branchHandled = true;
                        break;

                    default:
                        Debug.LogWarning($"未知分支动作: {cmd.Action}");
                        branchHandled = true;
                        break;
                }

                if (branchHandled)
                {
                    i++;
                    continue;
                }
            }

            // ---- 展示分支选项 ----
            if (cmd.Type == "对话控制" && cmd.Action == "展示分支选项")
            {
                int optionCount = 0;
                if (!int.TryParse(cmd.Param1, out optionCount) || optionCount < 1 || optionCount > 3)
                {
                    Debug.LogError($"无效的选项数量: {cmd.Param1}，跳过");
                    i++;
                    continue;
                }

                string speaker = cmd.Param2?.Trim() ?? "#旁白";

                List<string> optionTexts = new List<string>();
                List<string> optionConditions = new List<string>();
                int collected = 0;
                int nextIndex = i + 1;
                while (collected < optionCount && nextIndex < commands.Count)
                {
                    var subCmd = commands[nextIndex];
                    if (subCmd.Type == "对话控制" && subCmd.Action == "选项内容")
                    {
                        string text = subCmd.Param1?.Trim() ?? "选项";
                        string cond = subCmd.Param2?.Trim() ?? "";
                        optionTexts.Add(text);
                        optionConditions.Add(cond);
                        collected++;
                        nextIndex++;
                    }
                    else
                    {
                        Debug.LogWarning($"预期选项内容指令，但遇到 {subCmd.Type}/{subCmd.Action}，跳过剩余");
                        break;
                    }
                }

                if (collected < optionCount)
                {
                    Debug.LogError($"未能收集到足够的选项（需要 {optionCount}，实际 {collected}），跳过");
                    i = nextIndex;
                    continue;
                }

                currentChoiceOptions = optionTexts.ToArray();
                currentChoiceConditions = optionConditions.ToArray();
                currentChoiceCount = optionCount;

                // ★ 如果当前正处于 Skip 状态，重置 Skip 标志，但正常显示选项，让玩家手动选择
                if (isSkipping)
                {
                    isSkipping = false;
                    bgManager.IsSkipping = false;
                    charManager.IsSkipping = false;
                    dialogueManager.IsSkipping = false;
                    Debug.Log("StoryPlayer: 分支选项处重置 Skip 状态，等待玩家手动选择");
                    // 不自动选择，继续正常流程
                }

                dialogueManager.ShowOptions(currentChoiceOptions, speaker);
                isWaitingForChoice = true;
                Debug.Log($"StoryPlayer: 等待玩家选择 (Q/W/E) ...");
                yield return new WaitUntil(() => !isWaitingForChoice);
                i = nextIndex;
                continue;
            }

            // ---- 正常执行指令 ----
            ExecuteCommand(cmd);

            // ---- 对话暂停 ----
            if (cmd.Type == "对话控制" && pauseActions.Contains(cmd.Action))
            {
                if (dialogueManager != null)
                {
                    while (!dialogueManager.IsTypingComplete)
                    {
                        yield return null;
                    }
                }

                if (isSkipping)
                {
                    isSkipping = false;
                    bgManager.IsSkipping = false;
                    charManager.IsSkipping = false;
                    dialogueManager.IsSkipping = false;
                }

                isWaitingForContinue = true;
                Debug.Log($"StoryPlayer: 等待点击继续... (索引 {i})");

                yield return new WaitUntil(() => !isWaitingForContinue);

                i++;
                continue;
            }

            // ---- 延时处理 ----
            float delay = cmd.Delay;
            if (isSkipping)
            {
                yield return null;
            }
            else if (delay > 0)
            {
                float elapsed = 0f;
                while (elapsed < delay)
                {
                    if (isSkipping) break;
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            i++;
        }

        Debug.Log("StoryPlayer: 剧情播放完毕");
        isPlaying = false;
        isSkipping = false;
        bgManager.IsSkipping = false;
        charManager.IsSkipping = false;
        dialogueManager.IsSkipping = false;
        playCoroutine = null;
    }

    private void ExecuteCommand(StoryCommand cmd)
    {
        switch (cmd.Type)
        {
            case "背景控制": ExecuteBackground(cmd); break;
            case "立绘控制": ExecuteCharacter(cmd); break;
            case "对话控制": ExecuteDialogue(cmd); break;
            case "声音控制": ExecuteAudio(cmd); break;
            case "特效控制": ExecuteEffect(cmd); break;
            case "延时": break;
            default: Debug.LogWarning($"未知指令类型: {cmd.Type}"); break;
        }
    }

    private void ExecuteBackground(StoryCommand cmd)
    {
        switch (cmd.Action)
        {
            case "淡入背景":
                if (!string.IsNullOrEmpty(cmd.ResourceName))
                    bgManager.FadeInBackground(cmd.ResourceName, cmd.Param1);
                break;
            case "淡出背景":
                bgManager.FadeOutBackground(cmd.Param1);
                break;
            case "切换背景":
                if (!string.IsNullOrEmpty(cmd.ResourceName))
                {
                    string dir = cmd.Param2?.Trim();
                    if (dir == "左" || dir == "右")
                    {
                        bgManager.HorizontalSwitch(cmd.ResourceName, cmd.Param1, dir);
                    }
                    else
                    {
                        bgManager.SwitchBackground(cmd.ResourceName, cmd.Param1);
                    }
                }
                break;
            case "交叉背景":
                if (!string.IsNullOrEmpty(cmd.ResourceName))
                    bgManager.CloneSwitch(cmd.ResourceName);
                break;
            default:
                Debug.LogWarning($"未知背景动作: {cmd.Action}");
                break;
        }
    }

    private void ExecuteCharacter(StoryCommand cmd)
    {
        string objName = cmd.ResourceName;
        switch (cmd.Action)
        {
            case "创建角色":
                break;
            case "淡入角色":
                charManager.FadeInCharacter(objName, cmd.Param1, ParsePosition(cmd.Param2));
                break;
            case "淡出角色":
                charManager.FadeOutCharacter(objName);
                break;
            case "明暗控制":
                if (float.TryParse(cmd.Param1, out float brightness))
                    charManager.SetBrightness(objName, brightness);
                break;
            case "表情切换":
                charManager.SwitchEmotion(objName, cmd.Param1);
                break;
            case "层级控制":
                charManager.SetCharacterLayer(objName, cmd.Param1);
                break;
            case "尺寸缩放":
                charManager.ScaleCharacter(objName, new Vector3(ParseFloat(cmd.Param1), ParseFloat(cmd.Param1), 1f), ParseFloat(cmd.Param2));
                break;
            case "线性移动":
                charManager.MoveCharacter(objName, ParsePositions(cmd.Param1), ParseFloat(cmd.Param2), Ease.Linear);
                break;
            case "快慢缓动":
                charManager.MoveCharacter(objName, ParsePositions(cmd.Param1), ParseFloat(cmd.Param2), Ease.OutQuad);
                break;
            case "慢快缓动":
                charManager.MoveCharacter(objName, ParsePositions(cmd.Param1), ParseFloat(cmd.Param2), Ease.InQuad);
                break;
            case "慢快慢缓动":
                charManager.MoveCharacter(objName, ParsePositions(cmd.Param1), ParseFloat(cmd.Param2), Ease.InOutQuad);
                break;
            case "更改光照方案":
                charManager.ApplyMaterialProperties(cmd.Param1);
                break;
            default:
                Debug.LogWarning($"未知立绘动作: {cmd.Action}");
                break;
        }
    }
    /// <summary>
    /// 解析多位置字符串（格式："x1,y1;x2,y2;x3,y3"）
    /// </summary>
    private Vector2[] ParsePositions(string posStr)
    {
        if (string.IsNullOrEmpty(posStr)) return new Vector2[] { new Vector2(0, -300) };

        string[] parts = posStr.Split(';');
        List<Vector2> positions = new List<Vector2>();
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            string[] coords = trimmed.Split(',');
            if (coords.Length == 2)
            {
                if (float.TryParse(coords[0].Trim(), out float x) && float.TryParse(coords[1].Trim(), out float y))
                    positions.Add(new Vector2(x, y));
            }
        }
        if (positions.Count == 0)
            return new Vector2[] { new Vector2(0, -300) };
        return positions.ToArray();
    }
    private void ExecuteDialogue(StoryCommand cmd)
    {
        switch (cmd.Action)
        {
            case "对话框淡出": dialogueManager.FadeOut(); break;
            case "对话框淡入": dialogueManager.FadeIn(); break;
            case "对话框清空": dialogueManager.Clear(); break;
            case "展示文本": dialogueManager.ShowText(cmd.Param1, cmd.Param2); break;
            case "角色说话":
                string speaker = cmd.Param2;
                string content = cmd.Param1;
                charManager.SetSpeakingCharacter(speaker);
                dialogueManager.ShowText(content, speaker);
                break;
            default:
                Debug.LogWarning($"未知对话动作: {cmd.Action}");
                break;
        }
    }

    private void ExecuteAudio(StoryCommand cmd)
    {
        if (audioManager == null)
        {
            Debug.LogWarning("AudioManager 未赋值，无法执行声音控制");
            return;
        }
        string action = cmd.Action;
        string resource = cmd.ResourceName;
        switch (action)
        {
            case "淡入音乐":
                float duration = ParseFloat(cmd.Param1);
                float targetVol = ParseFloat(cmd.Param2);
                audioManager.FadeInMusic(resource, duration, targetVol);
                break;
            case "淡出音乐":
                float outDuration = ParseFloat(cmd.Param1);
                audioManager.FadeOutMusic(outDuration);
                break;
            case "切换音乐":
                float switchDuration = ParseFloat(cmd.Param1);
                float switchVol = ParseFloat(cmd.Param2);
                audioManager.SwitchMusic(resource, switchDuration, switchVol);
                break;
            case "播放音效":
                string mode = cmd.Param1;
                float vol = ParseFloat(cmd.Param2);
                audioManager.PlaySFX(resource, mode, vol);
                break;
            default:
                Debug.LogWarning($"未知声音动作: {action}");
                break;
        }
    }

    private void ExecuteEffect(StoryCommand cmd)
    {
        if (effectManager == null)
        {
            Debug.LogWarning("EffectManager 未赋值，无法执行特效控制");
            return;
        }
        string action = cmd.Action;
        string resource = cmd.ResourceName;
        string mode = cmd.Param1;
        switch (action)
        {
            case "播放动图":
                Vector3 position = ParsePosition3D(cmd.Param2);
                effectManager.PlayEffect(resource, mode, position);
                break;
            case "播放预设特效":
                effectManager.PlaySpecialEffect(mode, cmd.Param2);
                break;
            default:
                Debug.LogWarning($"未知特效动作: {action}");
                break;
        }
    }

    // ============================================================
    // 私有辅助方法
    // ============================================================
    private void ClearAudioAndEffects()
    {
        if (audioManager != null)
            audioManager.StopAllSFX(0.5f);
        if (effectManager != null)
            effectManager.StopAllEffects();
            effectManager.StopAllSpecialEffects();
        Debug.Log("StoryPlayer: 已清理音效和特效");
    }

    [System.Serializable]
    private class StoryCommand
    {
        public string Type;
        public float Delay;
        public string ResourceName;
        public string Action;
        public string Param1;
        public string Param2;
    }
}