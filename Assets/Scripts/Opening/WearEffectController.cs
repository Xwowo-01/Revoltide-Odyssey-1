using UnityEngine;
using DG.Tweening;      // 需要导入 DOTween 命名空间
using UnityEngine.UI;   // 如果需要处理 UI Image

/// <summary>
/// 控制 AdvancedImageEffect Shader 的磨损阈值动画
/// </summary>
public class WearEffectController : MonoBehaviour
{
    [Header("材质引用（自动获取，也可手动拖入）")]
    [Tooltip("如果为空，会自动从 Renderer 或 Image 组件获取")]
    public Material targetMaterial;

    [Header("动画参数")]
    [Tooltip("目标磨损阈值（最终值）")]
    public float targetWearThreshold = 0.35f;

    [Tooltip("动画持续时间（秒）")]
    public float duration = 1.5f;

    [Tooltip("缓动曲线（先快后慢）")]
    public Ease easeType = Ease.OutCubic;   // OutCubic 或 OutQuad 都是先快后慢

    private void Awake()
    {
        // 如果未手动指定材质，则自动获取
        if (targetMaterial == null)
        {
            // 尝试从 Renderer 获取
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                targetMaterial = renderer.material; // 自动创建材质实例，避免修改共享材质
            }
            else
            {
                // 尝试从 UI Image 获取
                Image image = GetComponent<Image>();
                if (image != null)
                {
                    targetMaterial = image.material;
                }
                else
                {
                    Debug.LogError("WearEffectController: 未找到 Renderer 或 Image 组件，且未手动指定材质！");
                }
            }
        }

        // 功能1：初始化时强制将 _WearThreshold 设为 1
        if (targetMaterial != null)
        {
            targetMaterial.SetFloat("_WearThreshold", 1f);
            Debug.Log("WearThreshold 初始化为 1");
        }
    }


    /// <summary>
    /// 异步动画方法：将 _WearThreshold 从当前值平滑变化到 targetWearThreshold (0.35)
    /// 使用 DOTween，先快后慢 (Ease.OutCubic)
    /// </summary>
    public void AnimateWearThreshold()
    {
        if (targetMaterial == null)
        {
            Debug.LogError("材质丢失，无法执行动画！");
            return;
        }

        // 获取当前阈值
        float current = targetMaterial.GetFloat("_WearThreshold");

        // 终止可能正在运行的动画（避免重叠）
        DOTween.Kill(targetMaterial);

        // 使用 DOFloat 进行浮点动画，设定缓动曲线
        targetMaterial.DOFloat(targetWearThreshold, "_WearThreshold", duration)
            .SetEase(easeType)
            .OnStart(() => Debug.Log("开始磨损动画"))
            .OnComplete(() => Debug.Log("磨损动画完成，阈值 = " + targetWearThreshold));
    }

    private void OnDestroy()
    {
        // 清理 DOTween 动画，防止内存泄漏
        if (targetMaterial != null)
        {
            DOTween.Kill(targetMaterial);
        }
    }
}