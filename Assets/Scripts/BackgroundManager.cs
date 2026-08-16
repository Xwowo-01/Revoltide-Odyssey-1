using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

[System.Serializable]
public class CommandData
{
    public int NodeId;
    public int Step;
    public string CmdType;
    public string TargetFile;
    public string TargetId;
    public string Action;
    public string Params;
    public float Delay;
}
public class BackgroundManager : MonoBehaviour
{
    [Header("淡入淡出时长（秒）")]
    public float defaultFadeInDuration = 1.0f;
    public float defaultFadeOutDuration = 0.5f;

    [Header("CG切换时长（秒）")]
    public float cgTransitionDuration = 1.5f;

    [Header("底色图片（需在场景中指定）")]
    [SerializeField] private Image blackBgImage;
    [SerializeField] private Image whiteBgImage;
    [SerializeField] private Image mainBgImage;

    [Header("对象池设置")]
    public bool preloadAllResources = true;
    public OnMissingBehavior onMissingBehavior = OnMissingBehavior.LoadFromResources;
    public Sprite defaultSprite;

    public enum OnMissingBehavior
    {
        LoadFromResources,
        ThrowError,
        UseDefault
    }

    private Dictionary<string, Sprite> backgroundPool = new Dictionary<string, Sprite>();
    private DG.Tweening.Tween currentTween;
    private DG.Tweening.Tween cgTween;
    private DG.Tweening.Tween horizontalTween;   // 水平转场专用

    private GameObject cgCloneObject;

    public bool IsSkipping { get; set; } = false;

    private Color targetMainBgColor = Color.clear;
    private string currentBgName = null;

    // ========================================================
    // 公共方法
    // ========================================================

    public void InitBackgrounds(string[] bgNames)
    {
        backgroundPool.Clear();
        if (bgNames != null && bgNames.Length > 0)
        {
            foreach (string name in bgNames)
                LoadBgToPool(name);
        }
        else if (preloadAllResources)
        {
            Sprite[] allSprites = Resources.LoadAll<Sprite>("Backgrounds");
            foreach (Sprite sp in allSprites)
            {
                if (!backgroundPool.ContainsKey(sp.name))
                    backgroundPool.Add(sp.name, sp);
            }
            Debug.Log($"背景对象池加载完成，共 {backgroundPool.Count} 张图片");
        }
        else
        {
            Debug.LogWarning("未指定背景列表，且 preloadAllResources 为 false，对象池为空。");
        }

        blackBgImage.gameObject.SetActive(true);
        blackBgImage.color = Color.black;
        whiteBgImage.gameObject.SetActive(false);
        mainBgImage.color = new Color(1, 1, 1, 0);
        mainBgImage.sprite = null;
        targetMainBgColor = new Color(1, 1, 1, 0);
        currentBgName = null;

        KillAllTweens();
        if (cgCloneObject != null)
        {
            DestroyImmediate(cgCloneObject);
            cgCloneObject = null;
        }

        ResetTransitionShaderParams();
    }

    public void FadeInBackground(string bgName, string colorType)
    {
        bool useBlack = (colorType != "白");
        if (useBlack)
            FadeInFromBlack(bgName);
        else
            FadeInFromWhite(bgName);
    }

    public void FadeOutBackground(string colorType)
    {
        bool useBlack = (colorType != "白");
        if (useBlack)
            FadeOutToBlack();
        else
            FadeOutToWhite();
    }

    public void SwitchBackground(string bgName, string colorType)
    {
        bool useBlack = (colorType != "白");
        if (useBlack)
            SwitchWithBlack(bgName);
        else
            SwitchWithWhite(bgName);
    }

    public void HorizontalFadeOut(string colorType, string direction)
    {
        bool useBlack = (colorType != "白");
        HorizontalFadeOut(useBlack, direction);
    }

    public void HorizontalFadeIn(string bgName, string colorType, string direction)
    {
        bool useBlack = (colorType != "白");
        HorizontalFadeIn(bgName, useBlack, direction);
    }

    public void HorizontalSwitch(string bgName, string colorType, string direction)
    {
        bool useBlack = (colorType != "白");
        StartCoroutine(HorizontalSwitchSequence(bgName, useBlack, direction));
    }

    public void CloneSwitch(string bgName)
    {
        KillHorizontalTween();
        if (cgTween != null || cgCloneObject != null)
            ForceCompleteCG();

        Sprite newSprite = GetSpriteFromPool(bgName);
        if (newSprite == null)
        {
            Debug.LogError($"CG切换失败：背景图 '{bgName}' 未找到");
            return;
        }

        cgCloneObject = Instantiate(mainBgImage.gameObject, mainBgImage.transform.parent);
        cgCloneObject.transform.SetSiblingIndex(mainBgImage.transform.GetSiblingIndex() - 1);

        mainBgImage.color = new Color(1, 1, 1, 0);
        targetMainBgColor = Color.white;
        currentBgName = bgName;

        mainBgImage.sprite = newSprite;

        cgTween = mainBgImage.DOFade(1, cgTransitionDuration)
            .OnComplete(() =>
            {
                if (cgCloneObject != null)
                {
                    DestroyImmediate(cgCloneObject);
                    cgCloneObject = null;
                }
                cgTween = null;
                targetMainBgColor = Color.white;
            });

        Debug.Log($"CG切换开始：{bgName}，时长 {cgTransitionDuration}s");
    }

    public void CompleteCurrent()
    {
        CompleteHorizontalTween();

        if (cgTween != null || cgCloneObject != null)
            ForceCompleteCG();

        if (currentTween != null)
        {
            currentTween.Kill(true);
            currentTween = null;
        }
        if (targetMainBgColor != Color.clear)
        {
            mainBgImage.color = targetMainBgColor;
        }
    }

    // ========================================================
    // 存档与读档
    // ========================================================

    [System.Serializable]
    public class BgSaveData
    {
        public string bgName;
        public Color color;
        public bool whiteActive;
    }

    public BgSaveData GetSaveData()
    {
        BgSaveData data = new BgSaveData();
        data.bgName = currentBgName;
        data.color = mainBgImage.color;
        data.whiteActive = whiteBgImage.gameObject.activeSelf;
        return data;
    }

    public void LoadSaveData(BgSaveData data)
    {
        KillAllTweens();
        if (cgCloneObject != null)
        {
            DestroyImmediate(cgCloneObject);
            cgCloneObject = null;
        }

        if (!string.IsNullOrEmpty(data.bgName))
        {
            Sprite sp = GetSpriteFromPool(data.bgName);
            if (sp != null)
            {
                mainBgImage.sprite = sp;
                currentBgName = data.bgName;
            }
            else
            {
                mainBgImage.sprite = null;
                currentBgName = null;
            }
        }
        else
        {
            mainBgImage.sprite = null;
            currentBgName = null;
        }
        mainBgImage.color = data.color;
        whiteBgImage.gameObject.SetActive(data.whiteActive);
        blackBgImage.gameObject.SetActive(true);
        blackBgImage.color = Color.black;
        targetMainBgColor = data.color;

        ResetTransitionShaderParams();
    }

    // ========================================================
    // 私有方法
    // ========================================================

    private void LoadBgToPool(string name)
    {
        if (backgroundPool.ContainsKey(name)) return;
        Sprite sp = Resources.Load<Sprite>("Backgrounds/" + name);
        if (sp != null) backgroundPool.Add(name, sp);
        else Debug.LogWarning($"背景图片加载失败: {name}");
    }

    private Sprite GetSpriteFromPool(string name)
    {
        if (backgroundPool.TryGetValue(name, out Sprite sp)) return sp;

        switch (onMissingBehavior)
        {
            case OnMissingBehavior.LoadFromResources:
                LoadBgToPool(name);
                if (backgroundPool.TryGetValue(name, out sp)) return sp;
                else goto case OnMissingBehavior.UseDefault;
            case OnMissingBehavior.UseDefault:
                return defaultSprite;
            case OnMissingBehavior.ThrowError:
            default:
                Debug.LogError($"对象池中无背景图片: {name}");
                return null;
        }
    }

    private void FadeInFromBlack(string bgName, float? duration = null)
    {
        float d = duration ?? defaultFadeInDuration;
        KillCurrentTween();

        whiteBgImage.gameObject.SetActive(false);
        mainBgImage.color = new Color(1, 1, 1, 0);
        Sprite sp = GetSpriteFromPool(bgName);
        if (sp == null) return;
        mainBgImage.sprite = sp;
        currentBgName = bgName;

        targetMainBgColor = Color.white;

        if (IsSkipping)
        {
            mainBgImage.color = targetMainBgColor;
            return;
        }

        currentTween = mainBgImage.DOFade(1, d).OnComplete(() => currentTween = null);
    }

    private void FadeInFromWhite(string bgName, float? duration = null)
    {
        float d = duration ?? defaultFadeInDuration;
        KillCurrentTween();

        whiteBgImage.gameObject.SetActive(true);
        mainBgImage.color = new Color(1, 1, 1, 0);
        Sprite sp = GetSpriteFromPool(bgName);
        if (sp == null) return;
        mainBgImage.sprite = sp;
        currentBgName = bgName;

        targetMainBgColor = Color.white;

        if (IsSkipping)
        {
            mainBgImage.color = targetMainBgColor;
            return;
        }

        currentTween = mainBgImage.DOFade(1, d).OnComplete(() => currentTween = null);
    }

    private void FadeOutToBlack(float? duration = null)
    {
        float d = duration ?? defaultFadeOutDuration;
        KillCurrentTween();

        whiteBgImage.gameObject.SetActive(false);
        mainBgImage.color = new Color(1, 1, 1, 1);

        targetMainBgColor = new Color(0, 0, 0, 0);

        if (IsSkipping)
        {
            mainBgImage.color = targetMainBgColor;
            currentBgName = null;
            return;
        }

        currentTween = mainBgImage.DOFade(0, d).OnComplete(() => { currentTween = null; currentBgName = null; });
    }

    private void FadeOutToWhite(float? duration = null)
    {
        float d = duration ?? defaultFadeOutDuration;
        KillCurrentTween();

        whiteBgImage.gameObject.SetActive(true);
        mainBgImage.color = new Color(1, 1, 1, 1);

        targetMainBgColor = new Color(0, 0, 0, 0);

        if (IsSkipping)
        {
            mainBgImage.color = targetMainBgColor;
            currentBgName = null;
            return;
        }

        currentTween = mainBgImage.DOFade(0, d).OnComplete(() => { currentTween = null; currentBgName = null; });
    }

    private void SwitchWithBlack(string bgName, float? fadeOutDuration = null, float? fadeInDuration = null)
    {
        float outDur = fadeOutDuration ?? defaultFadeOutDuration;
        float inDur = fadeInDuration ?? defaultFadeInDuration;
        StartCoroutine(SwitchSequence(bgName, true, outDur, inDur));
    }

    private void SwitchWithWhite(string bgName, float? fadeOutDuration = null, float? fadeInDuration = null)
    {
        float outDur = fadeOutDuration ?? defaultFadeOutDuration;
        float inDur = fadeInDuration ?? defaultFadeInDuration;
        StartCoroutine(SwitchSequence(bgName, false, outDur, inDur));
    }

    private IEnumerator SwitchSequence(string bgName, bool useBlack, float fadeOutDur, float fadeInDur)
    {
        bool needFadeOut = (mainBgImage.sprite != null && mainBgImage.color.a > 0.01f);

        if (needFadeOut)
        {
            if (useBlack)
                FadeOutToBlack(fadeOutDur);
            else
                FadeOutToWhite(fadeOutDur);

            if (!IsSkipping)
                yield return new WaitUntil(() => currentTween == null);
            else
                yield return null;
        }
        else
        {
            if (useBlack)
                whiteBgImage.gameObject.SetActive(false);
            else
                whiteBgImage.gameObject.SetActive(true);
            mainBgImage.color = new Color(1, 1, 1, 0);
        }

        if (useBlack)
            FadeInFromBlack(bgName, fadeInDur);
        else
            FadeInFromWhite(bgName, fadeInDur);
    }

    // ---- 水平转场实现 (已修改，加入 IsSkipping 检查) ----
    private void HorizontalFadeOut(bool useBlack, string direction)
    {
        KillAllTweens();

        if (useBlack)
            whiteBgImage.gameObject.SetActive(false);
        else
            whiteBgImage.gameObject.SetActive(true);

        // ★ 修改开始：如果正在跳过，直接清理并返回
        if (IsSkipping)
        {
            mainBgImage.sprite = null;
            mainBgImage.color = Color.clear;
            currentBgName = null;
            targetMainBgColor = Color.clear;
            ResetTransitionShaderParams();
            return;
        }

        if (mainBgImage.sprite == null || mainBgImage.color.a < 0.01f)
        {
            // 已经透明，直接完成
            mainBgImage.sprite = null;
            mainBgImage.color = Color.clear;
            currentBgName = null;
            ResetTransitionShaderParams();
            targetMainBgColor = Color.clear;
            return;
        }

        Material mat = mainBgImage.material;
        if (mat == null || !mat.HasProperty("_Fade") || !mat.HasProperty("_EdgeWidth"))
        {
            Debug.LogWarning("主背景材质不支持 _Fade / _EdgeWidth，无法执行水平淡出");
            return;
        }

        float startFade = 0f;
        float endFade = (direction == "左") ? -1f : 1f;
        float edgeWidth = 0.25f;

        mat.SetFloat("_Fade", startFade);
        mat.SetFloat("_EdgeWidth", edgeWidth);

        targetMainBgColor = new Color(0, 0, 0, 0);

        horizontalTween = DOTween.To(() => startFade, x => mat.SetFloat("_Fade", x), endFade, defaultFadeOutDuration)
            .SetEase(Ease.InOutQuad)
            .OnComplete(() =>
            {
                mainBgImage.sprite = null;
                mainBgImage.color = Color.clear;
                currentBgName = null;
                ResetTransitionShaderParams();
                horizontalTween = null;
            });
    }

    private void HorizontalFadeIn(string bgName, bool useBlack, string direction)
    {
        KillAllTweens();

        if (useBlack)
            whiteBgImage.gameObject.SetActive(false);
        else
            whiteBgImage.gameObject.SetActive(true);

        // ★ 修改开始：如果正在跳过，直接设置最终状态并返回
        if (IsSkipping)
        {
            Sprite sp = GetSpriteFromPool(bgName);
            if (sp != null)
            {
                mainBgImage.sprite = sp;
                mainBgImage.color = Color.white;
                currentBgName = bgName;
                targetMainBgColor = Color.white;
            }
            ResetTransitionShaderParams();
            return;
        }

        Sprite sprite = GetSpriteFromPool(bgName);
        if (sprite == null) return;
        mainBgImage.sprite = sprite;
        mainBgImage.color = Color.white;
        currentBgName = bgName;
        targetMainBgColor = Color.white;

        Material mat = mainBgImage.material;
        if (mat == null || !mat.HasProperty("_Fade") || !mat.HasProperty("_EdgeWidth"))
        {
            Debug.LogWarning("主背景材质不支持 _Fade / _EdgeWidth，无法执行水平淡入，降级为普通淡入");
            if (useBlack)
                FadeInFromBlack(bgName);
            else
                FadeInFromWhite(bgName);
            return;
        }

        float startFade = (direction == "左") ? 1f : -1f;
        float endFade = 0f;
        float edgeWidth = 0.25f;

        mat.SetFloat("_Fade", startFade);
        mat.SetFloat("_EdgeWidth", edgeWidth);

        horizontalTween = DOTween.To(() => startFade, x => mat.SetFloat("_Fade", x), endFade, defaultFadeInDuration)
            .SetEase(Ease.InOutQuad)
            .OnComplete(() =>
            {
                if (mat.HasProperty("_EdgeWidth"))
                    mat.SetFloat("_EdgeWidth", 0f);
                targetMainBgColor = Color.white;
                horizontalTween = null;
            });
    }

    private IEnumerator HorizontalSwitchSequence(string bgName, bool useBlack, string direction)
    {
        HorizontalFadeOut(useBlack, direction);
        yield return new WaitUntil(() => horizontalTween == null);
        HorizontalFadeIn(bgName, useBlack, direction);
        yield return new WaitUntil(() => horizontalTween == null);
    }

    // ---- 辅助方法 ----
    private void KillAllTweens()
    {
        KillCurrentTween();
        KillHorizontalTween();
        if (cgTween != null)
        {
            cgTween.Kill(true);
            cgTween = null;
        }
    }

    private void KillCurrentTween()
    {
        if (currentTween != null)
        {
            currentTween.Kill(true);
            currentTween = null;
        }
    }

    private void KillHorizontalTween()
    {
        if (horizontalTween != null)
        {
            horizontalTween.Kill(true);
            horizontalTween = null;
        }
    }

    private void CompleteHorizontalTween()
    {
        if (horizontalTween != null)
        {
            horizontalTween.Kill(true);
            horizontalTween = null;
        }
        if (targetMainBgColor.a < 0.01f)
        {
            mainBgImage.sprite = null;
            mainBgImage.color = Color.clear;
            currentBgName = null;
            ResetTransitionShaderParams();
        }
        else
        {
            if (mainBgImage.sprite != null)
            {
                mainBgImage.color = Color.white;
                if (mainBgImage.material != null)
                {
                    if (mainBgImage.material.HasProperty("_Fade"))
                        mainBgImage.material.SetFloat("_Fade", 0f);
                    if (mainBgImage.material.HasProperty("_EdgeWidth"))
                        mainBgImage.material.SetFloat("_EdgeWidth", 0f);
                }
            }
        }
        targetMainBgColor = mainBgImage.color;
    }

    private void ForceCompleteCG()
    {
        if (cgTween != null)
        {
            cgTween.Kill(true);
            cgTween = null;
        }
        if (cgCloneObject != null)
        {
            DestroyImmediate(cgCloneObject);
            cgCloneObject = null;
        }
        mainBgImage.color = Color.white;
        targetMainBgColor = Color.white;
        Debug.Log("CG切换被强制完成");
    }

    private void ResetTransitionShaderParams()
    {
        if (mainBgImage == null || mainBgImage.material == null)
            return;

        if (mainBgImage.material.HasProperty("_Fade"))
            mainBgImage.material.SetFloat("_Fade", 0f);
        if (mainBgImage.material.HasProperty("_EdgeWidth"))
            mainBgImage.material.SetFloat("_EdgeWidth", 0f);
    }

    private void Skip()
    {
        // 设置跳过标志（已在调用前设置，但为了保险再设一次）
        IsSkipping = true;
        KillAllTweens();
        ForceCompleteCG();
        // 水平转场可能残留材质参数，重置一下
        ResetTransitionShaderParams();
        // 清理状态（与普通转场一致）
        mainBgImage.sprite = null;
        mainBgImage.color = Color.clear;
        currentBgName = null;
        targetMainBgColor = Color.clear;
        Debug.Log("Skip: 所有动画已强制完成");
    }
}