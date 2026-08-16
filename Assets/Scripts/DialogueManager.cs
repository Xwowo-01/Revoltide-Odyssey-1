using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class DialogueManager : MonoBehaviour
{
    [Header("名牌")]
    [SerializeField] private Image nameBgImage;
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("内容")]
    [SerializeField] private Image contentBgImage1;
    [SerializeField] private Image contentBgImage2;
    [SerializeField] private TextMeshProUGUI contentText;

    [Header("选项")]
    [SerializeField] private TextMeshProUGUI optionText1;
    [SerializeField] private TextMeshProUGUI optionText2;
    [SerializeField] private TextMeshProUGUI optionText3;

    [Header("参数")]
    [SerializeField] private float fadeDuration = 0.5f;
    [SerializeField] private float typingSpeed = 0.05f;
    [SerializeField] private float fastSpeedMultiplier = 3.0f;
    [SerializeField] private float slowSpeedMultiplier = 0.5f;
    [SerializeField] private float shakeAmount = 5.0f;
    [SerializeField] private float shakeSpeed = 20.0f;

    private Coroutine typingCoroutine;
    private string fullContent = "";
    private float targetAlpha = -1f;
    private Tween currentFadeTween;

    private string plainText = "";
    private List<bool> shakeFlags = new List<bool>();       // 只对应可见字符
    private List<float> speedFactors = new List<float>();   // 只对应可见字符
    private List<int> visibleCharIndices = new List<int>(); // 可见字符在 plainText 中的索引
    private bool isShakingActive = false;

    // 抖动缓存（按可见字符顺序）
    private Vector3[][] initialVertices;
    private Vector3[][] currentOffsets;

    public bool IsTypingComplete => typingCoroutine == null;
    public bool IsSkipping { get; set; } = false;
    public bool IsShowingOptions { get; private set; } = false;

    // ============================================================
    // 公共方法
    // ============================================================

    public void FadeIn()
    {
        KillCurrentFadeTween();

        SetAllAlpha(0);
        targetAlpha = 1f;
        if (IsSkipping)
        {
            SetAllAlpha(targetAlpha);
            return;
        }

        currentFadeTween = DOTween.To(
            () => GetCurrentAlpha(),
            x => SetAllAlpha(x),
            targetAlpha,
            fadeDuration
        ).OnComplete(() => currentFadeTween = null);
    }

    public void FadeOut()
    {
        KillCurrentFadeTween();

        targetAlpha = 0f;
        if (IsSkipping)
        {
            SetAllAlpha(targetAlpha);
            return;
        }

        currentFadeTween = DOTween.To(
            () => GetCurrentAlpha(),
            x => SetAllAlpha(x),
            targetAlpha,
            fadeDuration
        ).OnComplete(() => currentFadeTween = null);
    }

    public void ShowText(string content, string speaker)
    {
        HideOptions();

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        nameText.text = speaker;
        if (string.IsNullOrEmpty(speaker) || speaker == "#旁白")
        {
            nameBgImage.gameObject.SetActive(false);
        }
        else
        {
            nameBgImage.gameObject.SetActive(true);
        }

        fullContent = content;
        ParseText(content, out plainText, out shakeFlags, out speedFactors, out visibleCharIndices);

        contentText.text = plainText;
        contentText.maxVisibleCharacters = 0;

        initialVertices = null;
        currentOffsets = null;

        if (IsSkipping)
        {
            contentText.maxVisibleCharacters = plainText.Length;
            isShakingActive = true;
            CacheInitialVertices();
            return;
        }

        typingCoroutine = StartCoroutine(TypeWriter());
    }

    public void ShowOptions(string[] options, string speaker)
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        contentText.gameObject.SetActive(false);

        nameText.text = speaker;
        if (string.IsNullOrEmpty(speaker) || speaker == "#旁白")
        {
            nameBgImage.gameObject.SetActive(false);
        }
        else
        {
            nameBgImage.gameObject.SetActive(true);
        }

        var texts = new TextMeshProUGUI[] { optionText1, optionText2, optionText3 };
        for (int i = 0; i < texts.Length; i++)
        {
            if (i < options.Length)
            {
                texts[i].gameObject.SetActive(true);
                texts[i].text = options[i];
            }
            else
            {
                texts[i].gameObject.SetActive(false);
            }
        }

        initialVertices = null;
        currentOffsets = null;
        isShakingActive = false;

        if (IsSkipping)
        {
            foreach (var t in texts)
                t.alpha = 1;
            IsShowingOptions = true;
            return;
        }

        foreach (var t in texts)
            t.alpha = 0;
        IsShowingOptions = true;

        foreach (var t in texts)
        {
            if (t.gameObject.activeSelf)
                t.DOFade(1, fadeDuration).SetEase(Ease.OutQuad);
        }
    }

    public void HideOptions()
    {
        optionText1.gameObject.SetActive(false);
        optionText2.gameObject.SetActive(false);
        optionText3.gameObject.SetActive(false);
        contentText.gameObject.SetActive(true);
        IsShowingOptions = false;

        if (!string.IsNullOrEmpty(contentText.text))
        {
            contentText.maxVisibleCharacters = plainText.Length;
            CacheInitialVertices();
        }
    }

    public void HideTextBox()
    {
        gameObject.SetActive(false);
    }

    public void ShowTextBox()
    {
        gameObject.SetActive(true);
    }

    public void CompleteCurrent()
    {
        KillCurrentFadeTween();

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        if (!string.IsNullOrEmpty(plainText))
        {
            contentText.text = plainText;
            contentText.maxVisibleCharacters = plainText.Length;
            isShakingActive = true;
            CacheInitialVertices();
        }

        if (targetAlpha >= 0f)
        {
            SetAllAlpha(targetAlpha);
            targetAlpha = -1f;
        }
    }

    // ============================================================
    // 存档与读档
    // ============================================================

    [System.Serializable]
    public class DialogueSaveData
    {
        public float alpha;
        public string content;
        public string speaker;
        public bool nameVisible;
        public bool textBoxActive;
        public bool isShowingOptions;
        public string[] optionTexts;
    }

    public DialogueSaveData GetSaveData()
    {
        DialogueSaveData data = new DialogueSaveData();
        data.alpha = GetCurrentAlpha();
        data.content = contentText.text;
        data.speaker = nameText.text;
        data.nameVisible = nameBgImage.gameObject.activeSelf;
        data.textBoxActive = gameObject.activeSelf;
        data.isShowingOptions = IsShowingOptions;
        if (IsShowingOptions)
        {
            var texts = new TextMeshProUGUI[] { optionText1, optionText2, optionText3 };
            var list = new System.Collections.Generic.List<string>();
            foreach (var t in texts)
            {
                if (t.gameObject.activeSelf)
                    list.Add(t.text);
            }
            data.optionTexts = list.ToArray();
        }
        return data;
    }

    public void LoadSaveData(DialogueSaveData data)
    {
        KillCurrentFadeTween();
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        nameText.text = data.speaker;
        contentText.text = data.content;
        plainText = data.content;
        shakeFlags.Clear();
        speedFactors.Clear();
        visibleCharIndices.Clear();
        isShakingActive = false;

        ParseText(data.content, out _, out _, out _, out visibleCharIndices);
        contentText.maxVisibleCharacters = plainText.Length;

        nameBgImage.gameObject.SetActive(data.nameVisible);
        gameObject.SetActive(data.textBoxActive);
        SetAllAlpha(data.alpha);
        targetAlpha = data.alpha;

        CacheInitialVertices();

        if (data.isShowingOptions && data.optionTexts != null && data.optionTexts.Length > 0)
        {
            ShowOptions(data.optionTexts, data.speaker);
        }
        else
        {
            HideOptions();
        }
    }

    // ============================================================
    // 私有方法
    // ============================================================

    private void Awake()
    {
        Init();
    }

    private void Init()
    {
        SetAllAlpha(0);
        nameBgImage.gameObject.SetActive(false);
        nameText.text = "";
        contentText.text = "";
        fullContent = "";
        plainText = "";
        targetAlpha = -1f;
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        KillCurrentFadeTween();
        HideOptions();
        isShakingActive = false;
        shakeFlags.Clear();
        speedFactors.Clear();
        visibleCharIndices.Clear();
        initialVertices = null;
        currentOffsets = null;
    }

    private void ParseText(string content, out string plain, out List<bool> shakes, out List<float> speeds, out List<int> visibleIndices)
    {
        shakes = new List<bool>();
        speeds = new List<float>();
        visibleIndices = new List<int>();
        StringBuilder plainBuilder = new StringBuilder();

        bool inShake = false;
        bool inFast = false;
        bool inSlow = false;
        int i = 0;
        while (i < content.Length)
        {
            if (content[i] == '<')
            {
                // 自定义标签
                if (i + 7 <= content.Length && content.Substring(i, 7) == "<shake>")
                {
                    inShake = true;
                    i += 7;
                    continue;
                }
                else if (i + 8 <= content.Length && content.Substring(i, 8) == "</shake>")
                {
                    inShake = false;
                    i += 8;
                    continue;
                }
                else if (i + 6 <= content.Length && content.Substring(i, 6) == "<fast>")
                {
                    inFast = true;
                    i += 6;
                    continue;
                }
                else if (i + 7 <= content.Length && content.Substring(i, 7) == "</fast>")
                {
                    inFast = false;
                    i += 7;
                    continue;
                }
                else if (i + 6 <= content.Length && content.Substring(i, 6) == "<slow>")
                {
                    inSlow = true;
                    i += 6;
                    continue;
                }
                else if (i + 7 <= content.Length && content.Substring(i, 7) == "</slow>")
                {
                    inSlow = false;
                    i += 7;
                    continue;
                }
                else
                {
                    // TMP 标签：保留到 plainText，但不记录到 shakeFlags/speeds 和 visibleIndices
                    int end = content.IndexOf('>', i);
                    if (end != -1)
                    {
                        string tag = content.Substring(i, end - i + 1);
                        plainBuilder.Append(tag);
                        i = end + 1;
                        continue;
                    }
                    else
                    {
                        // 非法 '<'，当作普通字符
                        plainBuilder.Append(content[i]);
                        shakes.Add(inShake);
                        speeds.Add(inFast ? fastSpeedMultiplier : (inSlow ? slowSpeedMultiplier : 1f));
                        visibleIndices.Add(plainBuilder.Length - 1);
                        i++;
                    }
                }
            }
            else
            {
                // 普通字符（可见字符）
                plainBuilder.Append(content[i]);
                shakes.Add(inShake);
                speeds.Add(inFast ? fastSpeedMultiplier : (inSlow ? slowSpeedMultiplier : 1f));
                visibleIndices.Add(plainBuilder.Length - 1);
                i++;
            }
        }

        plain = plainBuilder.ToString();
    }

    private IEnumerator TypeWriter()
    {
        isShakingActive = false;

        for (int idx = 0; idx < visibleCharIndices.Count; idx++)
        {
            int charIndex = visibleCharIndices[idx];
            contentText.maxVisibleCharacters = charIndex + 1;
            float delay = typingSpeed / speedFactors[idx];
            yield return new WaitForSeconds(delay);
        }

        isShakingActive = true;
        CacheInitialVertices();
        typingCoroutine = null;
    }

    // ---- 缓存初始顶点 ----
    private void CacheInitialVertices()
    {
        if (contentText == null || string.IsNullOrEmpty(contentText.text))
        {
            initialVertices = null;
            currentOffsets = null;
            return;
        }

        contentText.ForceMeshUpdate();
        TMP_TextInfo textInfo = contentText.textInfo;
        if (textInfo == null || textInfo.characterCount == 0)
        {
            initialVertices = null;
            currentOffsets = null;
            return;
        }

        int charCount = textInfo.characterCount;
        initialVertices = new Vector3[charCount][];
        currentOffsets = new Vector3[charCount][];

        for (int i = 0; i < charCount; i++)
        {
            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;
            Vector3[] verts = textInfo.meshInfo[materialIndex].vertices;

            Vector3[] initVerts = new Vector3[4];
            for (int v = 0; v < 4; v++)
            {
                initVerts[v] = verts[vertexIndex + v];
            }
            initialVertices[i] = initVerts;
            currentOffsets[i] = new Vector3[4];
            for (int v = 0; v < 4; v++)
                currentOffsets[i][v] = Vector3.zero;
        }
    }

    private bool EnsureInitialVerticesCached()
    {
        if (initialVertices == null || currentOffsets == null)
        {
            CacheInitialVertices();
        }
        if (initialVertices == null || currentOffsets == null)
            return false;

        TMP_TextInfo textInfo = contentText.textInfo;
        if (textInfo == null || textInfo.characterCount != initialVertices.Length)
        {
            CacheInitialVertices();
            if (initialVertices == null)
                return false;
        }
        return true;
    }

    // ---- Update 抖动 ----
    private void Update()
    {
        if (!isShakingActive || shakeFlags == null || shakeFlags.Count == 0) return;
        if (IsSkipping) return;

        if (!EnsureInitialVerticesCached()) return;

        TMP_TextInfo textInfo = contentText.textInfo;
        if (textInfo == null) return;

        int charCount = Mathf.Min(textInfo.characterCount, shakeFlags.Count);
        for (int i = 0; i < charCount; i++)
        {
            if (i >= initialVertices.Length) break;

            // ★ 直接用 i 索引 shakeFlags，因为 shakeFlags 只对应可见字符
            if (!shakeFlags[i]) continue;

            int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
            int vertexIndex = textInfo.characterInfo[i].vertexIndex;
            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

            Vector3 targetOffset = Vector3.zero;
            float offsetX = Mathf.Sin(Time.time * shakeSpeed + i * 1.7f) * shakeAmount;
            float offsetY = Mathf.Cos(Time.time * shakeSpeed * 1.3f + i * 0.9f) * shakeAmount;
            targetOffset = new Vector3(offsetX, offsetY, 0);

            currentOffsets[i][0] = Vector3.Lerp(currentOffsets[i][0], targetOffset, 0.1f);
            Vector3 offset = currentOffsets[i][0];

            Vector3[] initVerts = initialVertices[i];
            for (int v = 0; v < 4; v++)
            {
                vertices[vertexIndex + v] = initVerts[v] + offset;
            }
        }

        contentText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    // ---- 透明度辅助 ----
    private void SetAllAlpha(float alpha)
    {
        SetImageAlpha(nameBgImage, alpha);
        SetTextAlpha(nameText, alpha);
        SetImageAlpha(contentBgImage1, alpha);
        if (contentBgImage2 != null) SetImageAlpha(contentBgImage2, alpha);
        SetTextAlpha(contentText, alpha);
        SetTextAlpha(optionText1, alpha);
        SetTextAlpha(optionText2, alpha);
        SetTextAlpha(optionText3, alpha);
    }

    private void SetImageAlpha(Image img, float alpha)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = Mathf.Clamp01(alpha);
        img.color = c;
    }

    private void SetTextAlpha(TextMeshProUGUI tmp, float alpha)
    {
        if (tmp == null) return;
        tmp.alpha = Mathf.Clamp01(alpha);
    }

    private float GetCurrentAlpha()
    {
        return nameBgImage != null ? nameBgImage.color.a : 0f;
    }

    private void KillCurrentFadeTween()
    {
        if (currentFadeTween != null)
        {
            currentFadeTween.Kill(true);
            currentFadeTween = null;
        }
    }

    public void Clear()
    {
        // 1. 停止正在进行的打字协程
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        // 2. 清空文本内容和名牌
        contentText.text = "";
        nameText.text = "";
        nameBgImage.gameObject.SetActive(false);  // 隐藏名牌背景

        HideOptions();

        isShakingActive = false;
        shakeFlags.Clear();
        speedFactors.Clear();
        visibleCharIndices.Clear();
        initialVertices = null;
        currentOffsets = null;

        contentText.maxVisibleCharacters = 0;
    }
}