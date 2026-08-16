using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine.UI;
using UnityEngine.SceneManagement;   // 场景管理
using UnityEngine.Events;            // UnityEvent

/// <summary>
/// 基于节点列表的镜头运动控制，每个节点包含延迟、位置、旋转、缓动类型
/// 支持初始位置配置、呼吸晃动、最后一个节点同步淡出，淡出后自动切换场景
/// </summary>
public class CameraMotionController : MonoBehaviour
{
    [Header("目标对象（默认为主相机）")]
    public Transform target;

    [Header("初始位置配置（应用在 Start 或 Awake）")]
    public Vector3 initialPosition = new Vector3(0, 1, -5);
    public Vector3 initialRotation = Vector3.zero;

    [Header("呼吸晃动（在 autoStart = false 时生效）")]
    public bool enableBreath = true;
    [Tooltip("位置晃动幅度（单位：米）")]
    public float breathPositionAmplitude = 0.02f;
    [Tooltip("旋转晃动幅度（单位：度）")]
    public float breathRotationAmplitude = 0.5f;
    [Tooltip("晃动速度（周期/秒）")]
    public float breathSpeed = 0.5f;

    [Header("淡出与场景切换")]
    public Image fadeImage;                // 全屏黑色 Image
    public Ease fadeOutEase = Ease.InOutQuad;
    public string sceneToLoad = "";        // 目标场景名称（留空则不加载）
    public bool loadSceneAfterFade = true; // 是否加载场景
    public UnityEvent onFadeOutComplete;   // 淡出完成事件（先触发再加载场景）

    [Header("节点列表（按顺序执行）")]
    public List<MotionNode> nodes = new List<MotionNode>();

    [Header("全局控制")]
    public bool autoStart = true;
    public bool loop = false;

    private Sequence sequence;
    private bool isBreathing = false;
    private float breathTimer = 0f;
    private Vector3 breathBasePos;
    private Quaternion breathBaseRot;
    private bool hasFadedOut = false;      // 防止重复淡出（若循环则每次都会执行，但场景加载只执行一次）

    private void Awake()
    {
        // 自动查找 fadeImage
        if (fadeImage == null)
        {
            fadeImage = GameObject.Find("FadeImage")?.GetComponent<Image>();
            if (fadeImage == null)
                Debug.LogWarning("未指定 fadeImage，淡出功能将不可用");
        }

        // 初始时保证 fadeImage 完全透明
        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 0f;
            fadeImage.color = c;
        }
    }

    private void Start()
    {
        if (target == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
                target = mainCam.transform;
            else
                Debug.LogWarning("未找到 MainCamera，请手动指定 Target");
        }

        if (target != null)
        {
            target.position = initialPosition;
            target.rotation = Quaternion.Euler(initialRotation);
            breathBasePos = initialPosition;
            breathBaseRot = Quaternion.Euler(initialRotation);
        }

        if (autoStart)
        {
            StartMotion();
        }
        else
        {
            if (enableBreath && target != null)
                StartBreath();
        }
    }

    private void Update()
    {
        if (isBreathing && target != null)
        {
            breathTimer += Time.deltaTime * breathSpeed * Mathf.PI * 2f;

            float offsetX = Mathf.Sin(breathTimer) * breathPositionAmplitude;
            float offsetY = Mathf.Sin(breathTimer * 0.7f + 0.5f) * breathPositionAmplitude;
            float offsetZ = Mathf.Sin(breathTimer * 0.5f + 1.2f) * breathPositionAmplitude * 0.5f;

            Vector3 posOffset = new Vector3(offsetX, offsetY, offsetZ);
            target.position = breathBasePos + posOffset;

            float rotX = Mathf.Sin(breathTimer * 0.8f) * breathRotationAmplitude;
            float rotZ = Mathf.Sin(breathTimer * 0.6f + 0.8f) * breathRotationAmplitude;
            Quaternion rotOffset = Quaternion.Euler(rotX, 0, rotZ);
            target.rotation = breathBaseRot * rotOffset;
        }
    }

    /// <summary>
    /// 启动运动（外部调用时，自动停止呼吸并执行节点序列）
    /// </summary>
    public void StartMotion()
    {
        StopBreath();

        if (sequence != null && sequence.IsActive())
            sequence.Kill();

        if (target == null || nodes.Count == 0)
        {
            Debug.LogWarning("缺少目标或节点列表为空，无法执行镜头运动");
            return;
        }

        // 重置淡出标记（若循环则每次重新开始）
        hasFadedOut = false;

        sequence = DOTween.Sequence();

        float currentDelay = 0f;
        int nodeCount = nodes.Count;

        for (int i = 0; i < nodeCount; i++)
        {
            var node = nodes[i];
            bool isLastNode = (i == nodeCount - 1);

            float startTime = currentDelay + node.startDelay;

            // 位置和旋转
            Tween posTween = target.DOMove(node.position, node.duration);
            Tween rotTween = target.DORotate(node.rotation, node.duration);
            posTween.SetEase(node.easeType);
            rotTween.SetEase(node.easeType);

            sequence.Insert(startTime, posTween);
            sequence.Insert(startTime, rotTween);

            // 如果是最后一个节点，添加淡出
            if (isLastNode && fadeImage != null)
            {
                // 确保淡出前 Alpha 为 0
                Color c = fadeImage.color;
                c.a = 0f;
                fadeImage.color = c;

                Tween fadeTween = fadeImage.DOFade(1f, node.duration)
                                           .SetEase(fadeOutEase);

                // 淡出完成回调
                fadeTween.OnComplete(() =>
                {
                    // 触发事件（即使在场景加载前，事件也会执行）
                    onFadeOutComplete?.Invoke();

                    // 场景切换（如果启用且未加载过）
                    if (loadSceneAfterFade && !string.IsNullOrEmpty(sceneToLoad) && !hasFadedOut)
                    {
                        hasFadedOut = true;
                        Debug.Log($"加载场景: {sceneToLoad}");
                        SceneManager.LoadScene(sceneToLoad);
                    }
                    else if (!string.IsNullOrEmpty(sceneToLoad) && hasFadedOut)
                    {
                        Debug.LogWarning("场景已加载过，忽略重复请求");
                    }
                });

                sequence.Insert(startTime, fadeTween);
                Debug.Log("最后一个节点开始，淡出同步执行");
            }

            currentDelay = startTime + node.duration;
        }

        if (loop)
            sequence.SetLoops(-1, LoopType.Restart);

        sequence.Play();
    }

    public void StopMotion()
    {
        if (sequence != null && sequence.IsActive())
            sequence.Kill();
    }

    public void ResumeBreath()
    {
        if (enableBreath && target != null && !isBreathing)
        {
            breathBasePos = target.position;
            breathBaseRot = target.rotation;
            breathTimer = 0f;
            StartBreath();
        }
    }

    private void StartBreath()
    {
        isBreathing = true;
        breathTimer = 0f;
    }

    private void StopBreath()
    {
        isBreathing = false;
    }

    [System.Serializable]
    public class MotionNode
    {
        [Header("节点参数")]
        [Tooltip("从序列开始或上一节点结束后，再延迟多少秒开始本节点")]
        public float startDelay = 0f;

        [Tooltip("运动持续时间（秒）")]
        public float duration = 1f;

        [Tooltip("目标位置（世界坐标）")]
        public Vector3 position = Vector3.zero;

        [Tooltip("目标旋转（欧拉角）")]
        public Vector3 rotation = Vector3.zero;

        [Tooltip("缓动曲线类型（DOTween Ease）")]
        public Ease easeType = Ease.InOutQuad;
    }
}