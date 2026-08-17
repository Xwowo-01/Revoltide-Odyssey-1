using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections.Generic;

public class CharacterManager : MonoBehaviour
{
    [Header("淡入淡出时长（秒）")]
    public float fadeDuration = 1.0f;

    [Header("亮度渐变时长（秒）")]
    public float brightnessDuration = 0.5f;

    [Header("角色材质（所有角色共享）")]
    public Material characterMaterial;

    [Header("材质配置列表（按光线类型）")]
    public MaterialPropertiesConfig[] materialConfigs;

    [Header("至高角色层")]
    public GameObject outOfFrameLayer;

    private Dictionary<string, MaterialPropertiesConfig> configDict = new Dictionary<string, MaterialPropertiesConfig>();
    private Dictionary<string, GameObject> characters = new Dictionary<string, GameObject>();
    private Dictionary<string, string> charResourceMap = new Dictionary<string, string>();
    private Dictionary<string, Dictionary<string, Sprite>> emotionCache = new Dictionary<string, Dictionary<string, Sprite>>();
    private Dictionary<string, Vector2> characterPositions = new Dictionary<string, Vector2>();
    private Dictionary<string, Color> targetColors = new Dictionary<string, Color>();
    private Dictionary<string, Vector3> targetScales = new Dictionary<string, Vector3>();
    private Dictionary<string, Tween> scaleTweens = new Dictionary<string, Tween>();
    private Dictionary<string, Tween> positionTweens = new Dictionary<string, Tween>();
    private Dictionary<string, Tween> colorTweens = new Dictionary<string, Tween>();

    public bool IsSkipping { get; set; } = false;
    private string currentLightType = "默认";
    public string CurrentLightType => currentLightType;

    // ================================================================
    // 配置数据结构
    // ================================================================

    [System.Serializable]
    public class MaterialPropertiesConfig
    {
        public string lightType;
        public Color shadowColor = new Color(0, 0, 0, 0.5f);
        [Range(0, 5)] public float shadowStrength = 0.7f;
        [Range(1, 150)] public float shadowRadius = 25f;
        public Color rimColor = new Color(1, 1, 1, 0.5f);
        [Range(0, 5)] public float rimStrength = 0f;
        [Range(1, 150)] public float rimRadius = 25f;
        public Color gradientColor = new Color(0.2f, 0.1294f, 0.149f, 1f);
        [Range(0, 1)] public float gradientBottomAlpha = 1f;
        [Range(0, 1)] public float gradientHeight = 0.6f;
    }

    // ================================================================
    // 公共方法
    // ================================================================

    public void ApplyMaterialProperties(string lightType)
    {
        if (characterMaterial == null)
        {
            Debug.LogWarning("CharacterManager: characterMaterial 未赋值，无法应用材质参数");
            return;
        }

        if (configDict.Count == 0)
        {
            foreach (var cfg in materialConfigs)
            {
                if (!configDict.ContainsKey(cfg.lightType))
                    configDict.Add(cfg.lightType, cfg);
                else
                    Debug.LogWarning($"重复的光线类型配置: {cfg.lightType}，将忽略后面的");
            }
        }

        MaterialPropertiesConfig config = null;
        if (!configDict.TryGetValue(lightType, out config))
        {
            Debug.LogWarning($"未找到光线类型 '{lightType}' 的配置，使用默认配置");
            if (!configDict.TryGetValue("默认", out config))
            {
                Debug.LogError("没有 '默认' 配置，无法应用材质参数");
                return;
            }
        }

        characterMaterial.SetColor("_ShadowColor", config.shadowColor);
        characterMaterial.SetFloat("_ShadowStrength", config.shadowStrength);
        characterMaterial.SetFloat("_ShadowRadius", config.shadowRadius);
        characterMaterial.SetColor("_RimColor", config.rimColor);
        characterMaterial.SetFloat("_RimStrength", config.rimStrength);
        characterMaterial.SetFloat("_RimRadius", config.rimRadius);
        characterMaterial.SetColor("_GradientColor", config.gradientColor);
        characterMaterial.SetFloat("_GradientBottomAlpha", config.gradientBottomAlpha);
        characterMaterial.SetFloat("_GradientHeight", config.gradientHeight);

        currentLightType = lightType;
        Debug.Log($"CharacterManager: 材质已切换为 '{lightType}' 配置");
    }

    public void InitCharacters(string[] resourceNames, Vector2[] positions, string[] objectNames)
    {
        ApplyMaterialProperties("默认");

        int count = resourceNames.Length;
        if (positions == null || positions.Length < count)
        {
            Vector2 defaultPos = new Vector2(0, -300);
            Vector2[] newPos = new Vector2[count];
            for (int i = 0; i < count; i++)
                newPos[i] = defaultPos;
            positions = newPos;
        }
        if (objectNames == null || objectNames.Length < count)
        {
            objectNames = resourceNames;
        }

        for (int i = 0; i < count; i++)
        {
            string resName = resourceNames[i];
            string objName = objectNames[i];
            Vector2 pos = positions[i];
            CreateCharacter(resName, objName, pos.x, pos.y);
        }
        Debug.Log($"角色初始化完成，共 {count} 个角色");
    }

    public void FadeInCharacter(string objectName, string emotion = null, Vector2? position = null)
    {
        if (!characters.ContainsKey(objectName))
        {
            Debug.LogError($"角色 '{objectName}' 不存在，无法淡入");
            return;
        }
        if (position.HasValue)
            SetCharacterPosition(objectName, position.Value.x, position.Value.y);
        if (!string.IsNullOrEmpty(emotion))
            SwitchEmotion(objectName, emotion);
        FadeIn(objectName, fadeDuration);
    }

    public void FadeOutCharacter(string objectName)
    {
        FadeOut(objectName, fadeDuration);
    }

    public void SetBrightness(string objectName, float brightness)
    {
        SetCharacterBrightness(objectName, brightness);
    }

    public void SwitchEmotion(string objectName, string emotion)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在，无法切换表情");
            return;
        }
        if (!charResourceMap.TryGetValue(objectName, out string resourceName))
        {
            Debug.LogError($"角色 '{objectName}' 的资源名未记录");
            return;
        }
        if (!emotionCache.TryGetValue(resourceName, out var cache))
            return;

        string key = (emotion == "默认") ? resourceName : $"{resourceName}_{emotion}";
        if (!cache.TryGetValue(key, out Sprite targetSprite))
        {
            Debug.LogError($"表情 '{emotion}' 不存在，可用：{string.Join(", ", cache.Keys)}");
            return;
        }

        Image img = go.GetComponent<Image>();
        if (img != null) img.sprite = targetSprite;
        Debug.Log($"角色 '{objectName}' 切换到表情 '{emotion}'");
    }

    public void MoveCharacterLinear(string objectName, Vector2 targetPos, float duration)
    {
        MoveCharacter(objectName, new Vector2[] { targetPos }, duration, Ease.Linear);
    }

    public void MoveCharacterOut(string objectName, Vector2 targetPos, float duration)
    {
        MoveCharacter(objectName, new Vector2[] { targetPos }, duration, Ease.OutQuad);
    }

    public void MoveCharacterIn(string objectName, Vector2 targetPos, float duration)
    {
        MoveCharacter(objectName, new Vector2[] { targetPos }, duration, Ease.InQuad);
    }

    public void MoveCharacterInOut(string objectName, Vector2 targetPos, float duration)
    {
        MoveCharacter(objectName, new Vector2[] { targetPos }, duration, Ease.InOutQuad);
    }

    public void SetCharacterLayer(string objectName, string layerType)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在，无法调整层级");
            return;
        }

        if (layerType == "出框")  // 处理出框 / 返回
        {
            if (outOfFrameLayer == null)
            {
                Debug.LogError("出框角色层 (outOfFrameLayer) 未赋值，无法移出");
                return;
            }
            Transform currentParent = go.transform.parent;
            Transform targetParent = outOfFrameLayer.transform;
            if (currentParent == targetParent)
            {
                Debug.Log($"角色 '{objectName}' 已在出框层，无需重复移动");
                return;
            }
            go.transform.SetParent(targetParent, worldPositionStays: true);
            Debug.Log($"角色 '{objectName}' 已移入出框层");
            return;
        }
        else if (layerType == "返回")
        {
            Transform currentParent = go.transform.parent;
            Transform originalParent = transform; // 原始父物体为 CharacterManager 自身
            if (currentParent == originalParent)
            {
                Debug.Log($"角色 '{objectName}' 已在主层，无需重复移动");
                return;
            }
            go.transform.SetParent(originalParent, worldPositionStays: true);
            Debug.Log($"角色 '{objectName}' 已返回主层");
            return;
        }
       
        if (layerType == "置顶") // 置顶 / 置底逻辑
            BringToFront(go);
        else if (layerType == "置底")
            SendToBack(go);
        else
            Debug.LogError($"无效的层级参数：'{layerType}'，请使用 '置顶'、'置底'、'出框' 或 '返回'");
    }

    public void SetSpeakingCharacter(string speakerName)
    {
        if (speakerName == "#旁白")
        {
            foreach (var kvp in characters)
            {
                SetBrightness(kvp.Key, 1f);
            }
            Debug.Log($"旁白说话，所有角色亮度设为100%");
            return;
        }

        if (!characters.ContainsKey(speakerName))
        {
            Debug.LogWarning($"角色 '{speakerName}' 不存在，无法设置说话亮度");
            return;
        }

        GameObject speakerGo = characters[speakerName];
        speakerGo.transform.SetAsLastSibling();

        foreach (var kvp in characters)
        {
            float target = (kvp.Key == speakerName) ? 1.0f : 0.6f;
            SetBrightness(kvp.Key, target);
        }
        Debug.Log($"角色 '{speakerName}' 说话，亮度100%，其他人60%，已置顶");
    }

    public void ScaleCharacter(string objectName, Vector3 targetScale, float duration)
    {
        ScaleCharacter(objectName, targetScale, duration, Ease.Linear);
    }

    public void ScaleCharacter(string objectName, Vector3 targetScale, float duration, Ease ease)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在，无法缩放");
            return;
        }

        Transform trans = go.transform;
        if (trans == null) return;

        KillScaleTween(objectName);

        targetScales[objectName] = targetScale;

        if (IsSkipping)
        {
            trans.localScale = targetScale;
            return;
        }

        Tween tween = trans.DOScale(targetScale, duration).SetEase(ease);
        scaleTweens[objectName] = tween;
        tween.OnComplete(() => {
            if (scaleTweens.TryGetValue(objectName, out Tween stored) && stored == tween)
                scaleTweens.Remove(objectName);
        });
        Debug.Log($"角色 '{objectName}' 缩放到 ({targetScale.x}, {targetScale.y}, {targetScale.z})，时长 {duration}s");
    }

    public void CompleteAllCurrent()
    {
        List<string> posKeys = new List<string>(positionTweens.Keys);
        foreach (string key in posKeys)
        {
            if (positionTweens.TryGetValue(key, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill(true);
            }
        }
        positionTweens.Clear();

        List<string> colKeys = new List<string>(colorTweens.Keys);
        foreach (string key in colKeys)
        {
            if (colorTweens.TryGetValue(key, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill(true);
            }
        }
        colorTweens.Clear();

        List<string> scaleKeys = new List<string>(scaleTweens.Keys);
        foreach (string key in scaleKeys)
        {
            if (scaleTweens.TryGetValue(key, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill(true);
            }
        }
        scaleTweens.Clear();

        foreach (var kvp in characters)
        {
            GameObject go = kvp.Value;
            if (targetColors.TryGetValue(kvp.Key, out Color targetColor))
            {
                Image img = go.GetComponent<Image>();
                if (img != null) img.color = targetColor;
            }
            if (characterPositions.TryGetValue(kvp.Key, out Vector2 pos))
            {
                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = pos;
            }
            if (targetScales.TryGetValue(kvp.Key, out Vector3 scale))
            {
                go.transform.localScale = scale;
            }
        }
    }

    // ================================================================
    // 存档与读档
    // ================================================================

    [System.Serializable]
    public class CharSaveData
    {
        public string objectName;
        public string resourceName;
        public Vector2 position;
        public Color color;
        public string emotion;
        public Vector3 scale;
    }

    public List<CharSaveData> GetSaveData()
    {
        List<CharSaveData> list = new List<CharSaveData>();
        foreach (var kvp in characters)
        {
            string objName = kvp.Key;
            GameObject go = kvp.Value;
            Image img = go.GetComponent<Image>();
            if (img == null) continue;

            CharSaveData data = new CharSaveData();
            data.objectName = objName;
            data.resourceName = charResourceMap[objName];
            data.position = characterPositions[objName];
            data.color = img.color;
            data.scale = go.transform.localScale;

            string emotionKey = "默认";
            if (img.sprite != null)
            {
                string spriteName = img.sprite.name;
                int idx = spriteName.IndexOf('_');
                if (idx > 0)
                    emotionKey = spriteName.Substring(idx + 1);
                else
                    emotionKey = "默认";
            }
            data.emotion = emotionKey;
            list.Add(data);
        }
        return list;
    }

    public void LoadSaveData(List<CharSaveData> list)
    {
        ClearAll();
        foreach (var data in list)
        {
            CreateCharacter(data.resourceName, data.objectName, data.position.x, data.position.y);
            GameObject go = characters[data.objectName];
            Image img = go.GetComponent<Image>();
            if (img != null)
            {
                img.color = data.color;
                targetColors[data.objectName] = data.color;
            }
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = data.position;
            go.transform.localScale = data.scale;
            targetScales[data.objectName] = data.scale;
            if (!string.IsNullOrEmpty(data.emotion))
                SwitchEmotion(data.objectName, data.emotion);
        }
    }

    // ================================================================
    // 私有方法
    // ================================================================

    private void CreateCharacter(string resourceName, string objectName, float posX, float posY)
    {
        if (string.IsNullOrEmpty(resourceName))
        {
            Debug.LogError("角色资源名称不能为空！");
            return;
        }

        if (characters.ContainsKey(objectName))
        {
            GameObject old = characters[objectName];
            DOTween.Kill(old);
            KillPositionTween(objectName);
            KillColorTween(objectName);
            KillScaleTween(objectName);
            DestroyImmediate(old);
            characters.Remove(objectName);
            charResourceMap.Remove(objectName);
            characterPositions.Remove(objectName);
            targetColors.Remove(objectName);
            targetScales.Remove(objectName);
        }

        // ---- 资源加载 ----
        bool fromItems = false;
        Sprite defaultSprite = Resources.Load<Sprite>("Characters/" + resourceName + "/" + resourceName);

        if (defaultSprite == null)
        {
            defaultSprite = Resources.Load<Sprite>("Items/" + resourceName);
            if (defaultSprite == null)
            {
                Debug.LogError($"默认图片不存在：资源 '{resourceName}' 未在 Characters 或 Items 中找到");
                return;
            }
            fromItems = true;
        }
        else
        {
            // 角色资源存在，加载表情缓存（如果尚未加载）
            if (!emotionCache.ContainsKey(resourceName))
                LoadEmotions(resourceName);
        }

        // ---- 创建 GameObject ----
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        Image img = go.AddComponent<Image>();
        img.sprite = defaultSprite;
        img.raycastTarget = false;

        // ★ 根据来源决定是否应用材质
        if (!fromItems)
            img.material = characterMaterial;   // 角色应用材质
        else
            img.material = null;                // 物品不应用材质

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, defaultSprite.rect.width);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, defaultSprite.rect.height);
        rt.anchoredPosition = new Vector2(posX, posY);

        Color initColor = new Color(0, 0, 0, 0);
        img.color = initColor;

        go.transform.localScale = Vector3.one;
        targetScales[objectName] = Vector3.one;

        //Mask mask = go.AddComponent<Mask>();
        //mask.showMaskGraphic = true;

        characters[objectName] = go;
        charResourceMap[objectName] = resourceName;
        characterPositions[objectName] = new Vector2(posX, posY);
        targetColors[objectName] = initColor;

        Debug.Log($"角色 '{objectName}' 创建完成，来源: {(fromItems ? "Items" : "Characters")}");
    }

    private void LoadEmotions(string resourceName)
    {
        string folder = $"Characters/{resourceName}";
        Sprite[] allSprites = Resources.LoadAll<Sprite>(folder);
        if (allSprites == null || allSprites.Length == 0)
        {
            Debug.LogError($"未找到角色资源：{folder}");
            return;
        }

        var dict = new Dictionary<string, Sprite>();
        foreach (var sp in allSprites)
            dict[sp.name] = sp;
        emotionCache[resourceName] = dict;
        Debug.Log($"角色 '{resourceName}' 表情缓存加载完成，共 {dict.Count} 个");
    }

    private void FadeIn(string objectName, float duration)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在");
            return;
        }

        Image img = go.GetComponent<Image>();
        if (img == null) return;

        KillColorTween(objectName);

        Color targetColor = Color.white;
        targetColors[objectName] = targetColor;

        if (IsSkipping)
        {
            img.color = targetColor;
            return;
        }

        img.color = new Color(0, 0, 0, 0);

        float half = duration / 2f;
        Sequence seq = DOTween.Sequence();
        seq.Append(img.DOFade(1, half));
        seq.Append(img.DOColor(Color.white, half));
        colorTweens[objectName] = seq;
        seq.OnComplete(() => {
            if (colorTweens.TryGetValue(objectName, out Tween stored) && stored == seq)
                colorTweens.Remove(objectName);
        });
        seq.Play();
    }

    private void FadeOut(string objectName, float duration)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在");
            return;
        }

        Image img = go.GetComponent<Image>();
        if (img == null) return;

        if (img.color.a < 0.01f)
        {
            Debug.Log($"角色 '{objectName}' 已透明，跳过淡出");
            return;
        }

        KillColorTween(objectName);

        Color targetColor = new Color(0, 0, 0, 0);
        targetColors[objectName] = targetColor;

        if (IsSkipping)
        {
            img.color = targetColor;
            return;
        }

        float half = duration / 2f;
        float currentAlpha = img.color.a;

        Sequence seq = DOTween.Sequence();
        seq.Append(img.DOColor(new Color(0, 0, 0, currentAlpha), half));
        seq.Append(img.DOFade(0, half));
        colorTweens[objectName] = seq;
        seq.OnComplete(() => {
            if (colorTweens.TryGetValue(objectName, out Tween stored) && stored == seq)
                colorTweens.Remove(objectName);
        });
        seq.Play();
    }

    private void SetCharacterPosition(string objectName, float posX, float posY)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在");
            return;
        }
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.anchoredPosition = new Vector2(posX, posY);
        characterPositions[objectName] = new Vector2(posX, posY);
        Debug.Log($"角色 '{objectName}' 位置设置为 ({posX}, {posY})");
    }

    /// <summary>
    /// 统一移动接口（支持单点或路径）
    /// </summary>
    public void MoveCharacter(string objectName, Vector2[] targets, float duration, Ease ease)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在，无法移动");
            return;
        }

        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt == null) return;

        if (targets == null || targets.Length == 0)
        {
            Debug.LogWarning($"移动目标为空");
            return;
        }

        // 杀死旧动画
        KillPositionTween(objectName);

        // 记录最终目标（用于 Skip）
        characterPositions[objectName] = targets[targets.Length - 1];

        if (IsSkipping)
        {
            rt.anchoredPosition = targets[targets.Length - 1];
            return;
        }

        if (targets.Length == 1)
        {
            // 单点移动
            Tween tween = rt.DOAnchorPos(targets[0], duration).SetEase(ease);
            positionTweens[objectName] = tween;
            tween.OnComplete(() => {
                if (positionTweens.TryGetValue(objectName, out Tween stored) && stored == tween)
                    positionTweens.Remove(objectName);
            });
            Debug.Log($"角色 '{objectName}' 移动到 ({targets[0].x}, {targets[0].y})，时长 {duration}s");
        }
        else
        {
            // 多点路径
            Vector3[] path3D = new Vector3[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                path3D[i] = new Vector3(targets[i].x, targets[i].y, 0);

            Tween tween = rt.DOLocalPath(path3D, duration, PathType.Linear, PathMode.TopDown2D)
                .SetEase(ease);
            positionTweens[objectName] = tween;
            tween.OnComplete(() => {
                if (positionTweens.TryGetValue(objectName, out Tween stored) && stored == tween)
                    positionTweens.Remove(objectName);
            });
            Debug.Log($"角色 '{objectName}' 沿路径移动，共 {targets.Length} 个点，时长 {duration}s");
        }
    }

    private void SetCharacterBrightness(string objectName, float brightness)
    {
        if (!characters.TryGetValue(objectName, out GameObject go))
        {
            Debug.LogError($"角色 '{objectName}' 不存在");
            return;
        }

        Image img = go.GetComponent<Image>();
        if (img == null) return;

        KillColorTween(objectName);

        float alpha = img.color.a;
        Color targetColor = Color.Lerp(Color.black, Color.white, Mathf.Clamp01(brightness));
        targetColor.a = alpha;

        targetColors[objectName] = targetColor;

        if (IsSkipping)
        {
            img.color = targetColor;
            return;
        }

        Tween tween = img.DOColor(targetColor, brightnessDuration).SetEase(Ease.Linear);
        colorTweens[objectName] = tween;
        tween.OnComplete(() => {
            if (colorTweens.TryGetValue(objectName, out Tween stored) && stored == tween)
                colorTweens.Remove(objectName);
        });
        Debug.Log($"角色 '{objectName}' 亮度设为 {brightness}");
    }

    private void BringToFront(GameObject go)
    {
        go.transform.SetAsLastSibling();
        Debug.Log($"角色 '{go.name}' 已置顶");
    }

    private void SendToBack(GameObject go)
    {
        go.transform.SetAsFirstSibling();
        Debug.Log($"角色 '{go.name}' 已置底");
    }

    private void KillPositionTween(string objectName)
    {
        if (positionTweens.TryGetValue(objectName, out Tween tween))
        {
            if (tween != null && tween.IsActive())
                tween.Kill(true);
            positionTweens.Remove(objectName);
        }
    }

    private void KillColorTween(string objectName)
    {
        if (colorTweens.TryGetValue(objectName, out Tween tween))
        {
            if (tween != null && tween.IsActive())
                tween.Kill(true);
            colorTweens.Remove(objectName);
        }
    }

    private void KillScaleTween(string objectName)
    {
        if (scaleTweens.TryGetValue(objectName, out Tween tween))
        {
            if (tween != null && tween.IsActive())
                tween.Kill(true);
            scaleTweens.Remove(objectName);
        }
    }

    private void SkipToTarget(string objectName) { }

    private void ClearAll()
    {
        List<string> posKeys = new List<string>(positionTweens.Keys);
        foreach (string key in posKeys)
        {
            if (positionTweens.TryGetValue(key, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill(true);
            }
        }
        positionTweens.Clear();

        List<string> colKeys = new List<string>(colorTweens.Keys);
        foreach (string key in colKeys)
        {
            if (colorTweens.TryGetValue(key, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill(true);
            }
        }
        colorTweens.Clear();

        List<string> scaleKeys = new List<string>(scaleTweens.Keys);
        foreach (string key in scaleKeys)
        {
            if (scaleTweens.TryGetValue(key, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill(true);
            }
        }
        scaleTweens.Clear();

        foreach (var kvp in characters)
        {
            DOTween.Kill(kvp.Value);
            DestroyImmediate(kvp.Value);
        }
        characters.Clear();
        charResourceMap.Clear();
        characterPositions.Clear();
        targetColors.Clear();
        targetScales.Clear();
        Debug.Log("所有角色已清空");
    }
}