using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 控制 EyeOpenEffect Shader 的 _Progress 实现特殊睁眼序列
/// 序列：等待 → 先快后慢睁到0.3 → 轻眯一下 → 再慢快睁开至完全
/// </summary>
public class EyeOpenController : MonoBehaviour
{
    [Header("材质引用（二选一）")]
    public Material targetMaterial;          // 直接指定材质
    public Image targetImage;                // 或指定 UI Image 自动获取材质

    [Header("开局等待")]
    public float initialDelay = 1f;          // 播放序列前的等待时间（秒）

    [Header("第一阶段：快速睁开到0.3")]
    public float firstTarget = 0.3f;
    public float firstDuration = 0.5f;
    public Ease firstEase = Ease.OutQuad;    // 先快后慢

    [Header("第二阶段：眯眼（轻微闭合）")]
    public float squintTarget = 0.25f;       // 眯眼到的值（略小于0.3）
    public float squintDuration = 0.2f;
    public Ease squintEase = Ease.InOutQuad; // 快速平滑

    [Header("第三阶段：慢快睁开至完全")]
    public float finalTarget = 1.0f;
    public float finalDuration = 0.8f;
    public Ease finalEase = Ease.InQuad;     // 先慢后快

    [Header("自动执行")]
    public bool autoPlayOnStart = true;

    [Header("完成行为")]
    public bool hideOnComplete = false;  // 默认为 false，保留背景

    private Material material;
    private Sequence sequence;

    void Awake()
    {
        // 获取材质引用
        if (targetMaterial == null && targetImage != null)
        {
            material = targetImage.material;
        }
        else if (targetMaterial != null)
        {
            material = targetMaterial;
        }

        if (material == null)
            Debug.LogError("EyeOpenController: 未找到有效材质！请指定 targetMaterial 或 targetImage。");
    }

    public void Start()
    {
        if (autoPlayOnStart && material != null)
        {
            // 确保起始 _Progress = 0
            material.SetFloat("_Progress", 0f);
            PlayOpenSequence();
        }
    }

    /// <summary>
    /// 播放完整的睁眼序列（可外部调用）
    /// </summary>
    public void PlayOpenSequence()
    {
        if (material == null)
        {
            Debug.LogError("材质丢失，无法播放序列");
            return;
        }

        if (sequence != null && sequence.IsActive())
            sequence.Kill();

        sequence = DOTween.Sequence();

        float start = material.GetFloat("_Progress");

        sequence.AppendInterval(initialDelay);
        Tween t1 = material.DOFloat(firstTarget, "_Progress", firstDuration).SetEase(firstEase);
        sequence.Append(t1);
        Tween t2 = material.DOFloat(squintTarget, "_Progress", squintDuration).SetEase(squintEase);
        sequence.Append(t2);
        Tween t3 = material.DOFloat(finalTarget, "_Progress", finalDuration).SetEase(finalEase);
        sequence.Append(t3);

        sequence.OnComplete(() =>
        {
            Debug.Log("睁眼序列完成");
            if (targetImage != null)
            {
                targetImage.enabled = false;   // 原始设计，完成即关闭
            }
        });

        sequence.Play();
    }

    /// <summary>
    /// 立即停止当前序列
    /// </summary>
    public void StopSequence()
    {
        if (sequence != null && sequence.IsActive())
            sequence.Kill();
    }

    /// <summary>
    /// 重置为全黑（Progress = 0）
    /// </summary>
    public void ResetToBlack()
    {
        if (material != null)
            material.SetFloat("_Progress", 0f);
    }

    void OnDestroy()
    {
        if (sequence != null && sequence.IsActive())
            sequence.Kill();
    }
}