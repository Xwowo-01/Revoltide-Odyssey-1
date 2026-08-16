using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class EffectManager : MonoBehaviour
{
    [Header("对象池设置")]
    public bool preloadAllResources = true;
    public OnMissingBehavior onMissingBehavior = OnMissingBehavior.LoadFromResources;
    public Sprite defaultSprite;

    [Header("播放设置")]
    public float defaultFrameInterval = 0.1f;
    public RectTransform uiContainer;
    [Tooltip("眨眼特效每轮结束后的停顿范围（秒）")]
    public Vector2 blinkPauseRange = new Vector2(2f, 5f);

    [Header("特殊特效对象引用")]
    public GameObject openEyeEffect;   // 睁眼特效对象
    public GameObject windEffect;     // 刮风特效对象
    public GameObject fireEffect;     // 着火特效对象

    [Header("背景抖动")]
    public Image bgImage;             // 背景层 Image 引用

    public enum OnMissingBehavior
    {
        LoadFromResources,
        ThrowError,
        UseDefault
    }

    // ---------- 私有成员 ----------
    private Dictionary<string, Sprite[]> effectPool = new Dictionary<string, Sprite[]>();
    private List<EffectInstance> effectInstances = new List<EffectInstance>();
    private Dictionary<string, Vector3> loopEffects = new Dictionary<string, Vector3>();

    // ★ 特殊特效状态记录（用于存档）
    private Dictionary<string, bool> specialEffectsState = new Dictionary<string, bool>();

    // ★ 背景抖动
    private Tween bgShakeTween;        // 抖动 Tween
    private Tween bgScaleTween;        // 缩放/位置缓动 Tween
    private Vector3 bgOriginalScale = Vector3.one;
    private Vector3 bgOriginalPos = Vector3.zero;

    // ========================================================
    // 公共方法
    // ========================================================

    public void InitEffects(string[] effectNames)
    {
        effectPool.Clear();
        if (effectNames != null && effectNames.Length > 0)
        {
            foreach (string name in effectNames)
                LoadEffectToPool(name);
        }
        else if (preloadAllResources)
        {
            Debug.LogWarning("未指定特效列表，且 preloadAllResources 为 true，但未实现自动扫描所有文件夹，因此不加载任何特效。");
        }
        else
        {
            Debug.LogWarning("未指定特效列表，且 preloadAllResources 为 false，对象池为空。");
        }

        ClearAllEffects();
        // 初始化特殊特效状态
        specialEffectsState.Clear();
        specialEffectsState["睁眼"] = false;
        specialEffectsState["刮风"] = false;
        specialEffectsState["着火"] = false;
        specialEffectsState["背景抖动"] = false;
        // 确保所有特殊特效初始为关闭
        StopAllSpecialEffects();
        Debug.Log($"EffectManager: 初始化完成，已加载 {effectPool.Count} 个特效");
    }

    /// <summary>
    /// 播放特效（使用默认帧间隔）
    /// </summary>
    public void PlayEffect(string effectName, string mode, Vector3 position)
    {
        PlayEffect(effectName, mode, position, defaultFrameInterval);
    }

    /// <summary>
    /// 播放特效（自定义帧间隔）
    /// </summary>
    public void PlayEffect(string effectName, string mode, Vector3 position, float frameInterval)
    {
        Sprite[] frames = GetEffectFrames(effectName);
        if (frames == null || frames.Length == 0)
        {
            Debug.LogWarning($"EffectManager: 特效 '{effectName}' 未找到或无帧数据");
            return;
        }

        bool isLoop = (mode == "循环");

        GameObject go = new GameObject($"Effect_{effectName}_{(isLoop ? "Loop" : "OneShot")}");

        if (uiContainer != null)
        {
            go.transform.SetParent(uiContainer, worldPositionStays: false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(position.x, position.y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            Sprite firstFrame = frames[0];
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, firstFrame.rect.width);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, firstFrame.rect.height);

            Image img = go.AddComponent<Image>();
            img.sprite = firstFrame;
            img.raycastTarget = false;

            EffectInstance instance = new EffectInstance
            {
                gameObject = go,
                image = img,
                isLoop = isLoop,
                effectName = effectName,
                frameInterval = frameInterval,
                frames = frames,
                currentIndex = 0
            };
            instance.coroutine = StartCoroutine(PlaySequence(instance));
            effectInstances.Add(instance);

            if (isLoop)
                loopEffects[effectName] = position;

            Debug.Log($"EffectManager: 播放特效 '{effectName}' (UI模式)，位置 {position}，帧间隔 {frameInterval}s，尺寸 {firstFrame.rect.width}x{firstFrame.rect.height}");
        }
        else
        {
            go.transform.position = position;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames[0];

            EffectInstance instance = new EffectInstance
            {
                gameObject = go,
                spriteRenderer = sr,
                isLoop = isLoop,
                effectName = effectName,
                frameInterval = frameInterval,
                frames = frames,
                currentIndex = 0
            };
            instance.coroutine = StartCoroutine(PlaySequence(instance));
            effectInstances.Add(instance);

            if (isLoop)
                loopEffects[effectName] = position;

            Debug.Log($"EffectManager: 播放特效 '{effectName}' (World模式)，位置 {position}，帧间隔 {frameInterval}s");
        }
    }

    // ---- 特殊特效控制 ----

    /// <summary>
    /// 公共接口：根据类型播放特殊特效
    /// </summary>
    public void PlaySpecialEffect(string type)
    {
        PlaySpecialEffect(type, ""); // 调用重载，无背景参数
    }
    public void PlaySpecialEffect(string type, string bgName)
    {
        string key = type.Trim();
        if (key == "睁眼")
            StartOpenEye(bgName);
        else if (key == "刮风")
            StartWind();
        else if (key == "着火")
            StartFire();
        else if (key == "背景抖动")
            StartBgShake();
        else
            Debug.LogWarning($"未知的特殊特效类型: {type}");
    }

    /// <summary>
    /// 停止所有特殊特效
    /// </summary>
    public void StopAllSpecialEffects()
    {
        if (openEyeEffect != null && openEyeEffect.activeSelf)
        {
            openEyeEffect.SetActive(false);
            specialEffectsState["睁眼"] = false;
        }
        if (windEffect != null && windEffect.activeSelf)
        {
            windEffect.SetActive(false);
            specialEffectsState["刮风"] = false;
        }
        if (fireEffect != null && fireEffect.activeSelf)
        {
            fireEffect.SetActive(false);
            specialEffectsState["着火"] = false;
        }
        // ★ 停止背景抖动
        StopBgShake();
        specialEffectsState["背景抖动"] = false;

        Debug.Log("EffectManager: 所有特殊特效已关闭");
    }

    // ---- 私有特殊方法 ----
    private void StartOpenEye(string bgName)
    {
        if (openEyeEffect == null)
        {
            Debug.LogWarning("睁眼特效对象未赋值");
            return;
        }

        // 获取 Image 组件（优先使用 controller.targetImage）
        Image img = openEyeEffect.GetComponent<Image>();
        EyeOpenController controller = openEyeEffect.GetComponent<EyeOpenController>();
        if (controller != null && controller.targetImage != null)
            img = controller.targetImage;

        if (img == null)
        {
            Debug.LogWarning("睁眼特效对象上未找到 Image 组件，无法显示背景");
            return;
        }

        // 加载背景 Sprite
        if (!string.IsNullOrEmpty(bgName))
        {
            Sprite bgSprite = Resources.Load<Sprite>("Backgrounds/" + bgName);
            if (bgSprite != null)
            {
                img.sprite = bgSprite;
                img.enabled = true;   // 确保显示
            }
            else
                Debug.LogWarning($"未找到背景图: Backgrounds/{bgName}");
        }

        // 激活对象
        openEyeEffect.SetActive(true);

        // 重置并播放睁眼序列（内部完成时会自动禁用 targetImage）
        if (controller != null)
        {
            controller.ResetToBlack();
            controller.PlayOpenSequence();
        }

        specialEffectsState["睁眼"] = true;
        Debug.Log($"EffectManager: 睁眼特效已开启，背景={bgName}");
    }

    private void StartWind()
    {
        if (windEffect == null)
        {
            Debug.LogWarning("刮风特效对象未赋值");
            return;
        }
        windEffect.SetActive(true);
        specialEffectsState["刮风"] = true;
        Debug.Log("EffectManager: 刮风特效已开启");
    }

    private void StartFire()
    {
        if (fireEffect == null)
        {
            Debug.LogWarning("着火特效对象未赋值");
            return;
        }
        fireEffect.SetActive(true);
        specialEffectsState["着火"] = true;
        Debug.Log("EffectManager: 着火特效已开启");
    }

    // ★ 背景抖动（带0.1秒缓动）
    private void StartBgShake()
    {
        if (bgImage == null)
        {
            Debug.LogWarning("背景 Image 未赋值，无法执行背景抖动");
            return;
        }

        // 如果已有动画，先停止
        if (bgShakeTween != null)
        {
            bgShakeTween.Kill(true);
            bgShakeTween = null;
        }
        if (bgScaleTween != null)
        {
            bgScaleTween.Kill(true);
            bgScaleTween = null;
        }

        // 保存原始缩放和位置
        bgOriginalScale = bgImage.transform.localScale;
        bgOriginalPos = bgImage.transform.localPosition;

        // 平滑放大到 1.05 倍，耗时 0.1 秒
        bgScaleTween = bgImage.transform.DOScale(bgOriginalScale * 1.05f, 0.1f)
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                bgScaleTween = null;
                // 放大完成后开始抖动
                bgShakeTween = bgImage.transform.DOShakePosition(0.5f, new Vector3(30, 0, 0), 20, 90, false, true)
                    .OnComplete(() =>
                    {
                        // 抖动完成，停止并还原
                        StopBgShake();
                        specialEffectsState["背景抖动"] = false;
                    });
            });

        specialEffectsState["背景抖动"] = true;
        Debug.Log("EffectManager: 背景抖动已开启");
    }

    private void StopBgShake()
    {
        // 杀掉抖动和缩放动画
        if (bgShakeTween != null)
        {
            bgShakeTween.Kill(true);
            bgShakeTween = null;
        }
        if (bgScaleTween != null)
        {
            bgScaleTween.Kill(true);
            bgScaleTween = null;
        }
        if (bgImage != null)
        {
            // 平滑还原缩放和位置，耗时 0.1 秒
            bgImage.transform.DOScale(bgOriginalScale, 0.1f).SetEase(Ease.OutQuad);
            bgImage.transform.DOLocalMove(bgOriginalPos, 0.1f).SetEase(Ease.OutQuad);
        }
        specialEffectsState["背景抖动"] = false;
    }

    // ---- 停止所有特效（保留用于普通特效） ----
    public void StopAllEffects()
    {
        if (effectInstances.Count == 0) return;

        List<EffectInstance> instances = new List<EffectInstance>(effectInstances);
        foreach (var inst in instances)
        {
            if (inst == null || inst.gameObject == null) continue;
            if (inst.coroutine != null)
                StopCoroutine(inst.coroutine);
            Destroy(inst.gameObject);
            Debug.Log($"EffectManager: 停止特效 '{inst.effectName}'");
        }
        effectInstances.Clear();
        loopEffects.Clear();
        // 注意：不停止特殊特效，因为它们由 StopAllSpecialEffects 单独控制
    }

    public void CompleteCurrent()
    {
        StopAllEffects();
    }

    // ========================================================
    // 存档与读档
    // ========================================================

    [System.Serializable]
    public class EffectSaveData
    {
        public List<LoopEffectData> loopEffectList;
        public bool openEyeActive;
        public bool windActive;
        public bool fireActive;
        // 背景抖动是瞬时特效，不保存状态，读档时默认为关闭
    }

    [System.Serializable]
    public class LoopEffectData
    {
        public string effectName;
        public float posX;
        public float posY;
        public float posZ;
    }

    public EffectSaveData GetSaveData()
    {
        EffectSaveData data = new EffectSaveData();
        data.loopEffectList = new List<LoopEffectData>();
        foreach (var kvp in loopEffects)
        {
            LoopEffectData effData = new LoopEffectData();
            effData.effectName = kvp.Key;
            effData.posX = kvp.Value.x;
            effData.posY = kvp.Value.y;
            effData.posZ = kvp.Value.z;
            data.loopEffectList.Add(effData);
        }

        // ★ 保存特殊特效状态（不包括背景抖动）
        data.openEyeActive = specialEffectsState.TryGetValue("睁眼", out bool o) && o;
        data.windActive = specialEffectsState.TryGetValue("刮风", out bool w) && w;
        data.fireActive = specialEffectsState.TryGetValue("着火", out bool f) && f;

        return data;
    }

    public void LoadSaveData(EffectSaveData data)
    {
        // 停止所有普通特效
        StopAllEffects();

        // 恢复循环特效（原有逻辑）
        if (data.loopEffectList != null)
        {
            foreach (var effData in data.loopEffectList)
            {
                Vector3 pos = new Vector3(effData.posX, effData.posY, effData.posZ);
                PlayEffect(effData.effectName, "循环", pos, defaultFrameInterval);
            }
        }

        // ★ 恢复特殊特效状态（直接设置激活状态，不触发日志）
        if (openEyeEffect != null)
        {
            openEyeEffect.SetActive(data.openEyeActive);
            specialEffectsState["睁眼"] = data.openEyeActive;
        }
        if (windEffect != null)
        {
            windEffect.SetActive(data.windActive);
            specialEffectsState["刮风"] = data.windActive;
        }
        if (fireEffect != null)
        {
            fireEffect.SetActive(data.fireActive);
            specialEffectsState["着火"] = data.fireActive;
        }
        // 背景抖动不恢复，默认为关闭
        StopBgShake();

        Debug.Log($"EffectManager: 读档恢复特殊特效 (睁眼={data.openEyeActive}, 刮风={data.windActive}, 着火={data.fireActive})");
    }

    // ========================================================
    // 私有辅助方法
    // ========================================================

    private void LoadEffectToPool(string name)
    {
        if (effectPool.ContainsKey(name)) return;
        Sprite[] sprites = Resources.LoadAll<Sprite>("SFX/" + name);
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogWarning($"EffectManager: 特效 '{name}' 加载失败，未找到资源或文件夹为空");
            return;
        }

        // ★ 改用自然数字排序（按 _数字 的大小）
        System.Array.Sort(sprites, (a, b) =>
        {
            int numA = ExtractTrailingNumber(a.name);
            int numB = ExtractTrailingNumber(b.name);
            return numA.CompareTo(numB);
        });

        effectPool.Add(name, sprites);
        Debug.Log($"EffectManager: 特效 '{name}' 加载完成，共 {sprites.Length} 帧");
    }
        
    private int ExtractTrailingNumber(string fileName)// ★ 辅助方法：提取文件名末尾的数字
    {
        int lastUnderscore = fileName.LastIndexOf('_');
        if (lastUnderscore == -1 || lastUnderscore == fileName.Length - 1)
            return 0; // 没有下划线或下划线在末尾，视为0

        string numStr = fileName.Substring(lastUnderscore + 1);
        if (int.TryParse(numStr, out int result))
            return result;
        else
            return 0; 
    }

    private Sprite[] GetEffectFrames(string name)
    {
        if (effectPool.TryGetValue(name, out Sprite[] frames))
            return frames;

        switch (onMissingBehavior)
        {
            case OnMissingBehavior.LoadFromResources:
                LoadEffectToPool(name);
                if (effectPool.TryGetValue(name, out frames)) return frames;
                else goto case OnMissingBehavior.UseDefault;
            case OnMissingBehavior.UseDefault:
                return new Sprite[] { defaultSprite };
            case OnMissingBehavior.ThrowError:
            default:
                Debug.LogError($"EffectManager: 对象池中无特效 '{name}'");
                return null;
        }
    }

    private IEnumerator PlaySequence(EffectInstance instance)
    {
        int totalFrames = instance.frames.Length;
        int index = 0;
        bool isBlinkLoop = instance.isLoop && instance.effectName.EndsWith("眨眼");

        while (true)
        {
            // 1. 更新画面
            if (uiContainer != null && instance.image != null)
            {
                instance.image.sprite = instance.frames[index];
            }
            else if (instance.spriteRenderer != null)
            {
                instance.spriteRenderer.sprite = instance.frames[index];
            }
            else
            {
                break; // 组件丢失，退出
            }

            // 2. 计算该帧的显示时间
            float waitTime = instance.frameInterval;
            if (isBlinkLoop && index == totalFrames - 1)
            {
                float extraPause = Random.Range(blinkPauseRange.x, blinkPauseRange.y);
                waitTime += extraPause;
            }

            // 3. 计算下一帧索引
            int nextIndex = (index + 1) % totalFrames;

            // 4. 非循环且已播完 → 销毁并退出
            if (!instance.isLoop && nextIndex == 0)
            {
                break;
            }

            // 5. 等待该帧的总时间
            yield return new WaitForSeconds(waitTime);

            // 6. 移动到下一帧
            index = nextIndex;
        }

        // 非循环特效的清理
        if (!instance.isLoop)
        {
            effectInstances.Remove(instance);
            if (instance.gameObject != null)
            {
                Destroy(instance.gameObject);
                Debug.Log($"EffectManager: 单次特效 '{instance.effectName}' 播放完毕，已销毁");
            }
        }
    }

    private void ClearAllEffects()
    {
        foreach (var inst in effectInstances)
        {
            if (inst != null && inst.gameObject != null)
                Destroy(inst.gameObject);
        }
        effectInstances.Clear();
        loopEffects.Clear();
        // 不清理特殊特效，由外部控制
    }

    // ========================================================
    // 内部类：特效实例
    // ========================================================
    private class EffectInstance
    {
        public GameObject gameObject;
        public SpriteRenderer spriteRenderer;
        public Image image;
        public bool isLoop;
        public string effectName;
        public float frameInterval;
        public Sprite[] frames;
        public int currentIndex;
        public Coroutine coroutine;
    }

    private void OnDestroy()
    {
        ClearAllEffects();
        // 特殊特效由场景管理，不在此销毁
        // 停止背景抖动
        StopBgShake();
    }
}