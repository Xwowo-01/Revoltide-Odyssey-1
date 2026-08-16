using UnityEngine;
using System.Collections;
using echo17.EndlessBook;

/// <summary>
/// 控制书本状态切换与磨损特效的时序
/// </summary>
public class BookWearSequenceController : MonoBehaviour
{
    [Header("必需引用（可手动拖入，若为空则自动查找）")]
    public EndlessBook book;
    public WearEffectController wearController;
    public CameraMotionController cameraMotionController;

    [Header("音效（可选）")]
    public AudioSource bookOpenSound;               // 打开书音效（非循环）
    public float bookOpenSoundDelay = 0f;           // 打开书音效延迟秒数

    public AudioSource pagesFlippingSound;          // 连续翻页音效（建议勾选 Loop）
    public float pagesFlippingSoundDelay = 0.2f;    // 翻页音效延迟播放秒数
    public float pagesFlippingFadeOutDuration = 0.3f; // 淡出时长（秒）
    public float pagesFlippingEarlyStopOffset = 0.5f; // 提前停止偏移量（秒），使音效比翻页结束提前这么多开始淡出

    [Header("时间配置（秒）")]
    public float initialDelay = 3f;
    public float openMiddleDelay = 2f;
    public float jumpToEndDelay = 2f;

    [Header("Jump to End 配置")]
    public bool jumpToEndAsPage = true;
    public float openMiddleAnimTime = 0.5f;
    public float jumpAnimTime = 0.5f;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            if (book == null) book = FindObjectOfType<EndlessBook>();
            if (wearController == null) wearController = FindObjectOfType<WearEffectController>();
            if (cameraMotionController == null) cameraMotionController = FindObjectOfType<CameraMotionController>();

            if (book == null) Debug.LogError("未找到 EndlessBook 组件！");
            if (wearController == null) Debug.LogError("未找到 WearEffectController 组件！");

            StartCoroutine(SequenceRoutine());
            if (cameraMotionController != null) cameraMotionController.StartMotion();
        }
    }

    private IEnumerator SequenceRoutine()
    {
        // 第一步：初始等待
        yield return new WaitForSeconds(initialDelay);

        // 第二步：打开到中间（播放打开书音效）
        if (book != null)
        {
            if (bookOpenSound != null)
                bookOpenSound.PlayDelayed(bookOpenSoundDelay);

            book.SetState(EndlessBook.StateEnum.OpenMiddle, animationTime: openMiddleAnimTime);
            Debug.Log("执行 OpenMiddle");
        }

        yield return new WaitForSeconds(openMiddleDelay);

        // 第三步：跳转到末尾（播放连续翻页音效 + 淡出控制）
        if (book != null && jumpToEndAsPage)
        {
            int lastPage = book.LastPageNumber;

            // 播放翻页音效（带延迟）
            if (pagesFlippingSound != null)
            {
                pagesFlippingSound.PlayDelayed(pagesFlippingSoundDelay);

                // 计算淡出开始延迟 = 翻页总时长 - 淡出时长 - 提前停止偏移量
                float fadeStartDelay = jumpAnimTime - pagesFlippingFadeOutDuration - pagesFlippingEarlyStopOffset;
                fadeStartDelay = Mathf.Max(0f, fadeStartDelay); // 确保不小于0

                StartCoroutine(FadeOutFlippingSoundAfterDelay(fadeStartDelay));
            }

            // 执行翻页
            book.TurnToPage(lastPage,
                            EndlessBook.PageTurnTimeTypeEnum.TotalTurnTime,
                            jumpAnimTime);
            Debug.Log($"翻到最后一页: {lastPage}");
        }
        else if (book != null)
        {
            // 非翻页模式（直接切换状态）
            book.SetState(EndlessBook.StateEnum.ClosedBack, animationTime: jumpAnimTime);
            Debug.Log("设置状态为 ClosedBack");
        }

        yield return new WaitForSeconds(jumpToEndDelay);

        // 第四步：触发磨损动画
        if (wearController != null)
        {
            wearController.AnimateWearThreshold();
            Debug.Log("调用 AnimateWearThreshold");
        }
        else
        {
            Debug.LogWarning("WearEffectController 缺失，无法触发磨损动画");
        }
    }

    /// <summary>
    /// 在指定延迟后开始淡出翻页音效
    /// </summary>
    private IEnumerator FadeOutFlippingSoundAfterDelay(float delay)
    {
        if (delay > 0)
            yield return new WaitForSeconds(delay);

        if (pagesFlippingSound == null || !pagesFlippingSound.isPlaying)
            yield break;

        float startVolume = pagesFlippingSound.volume;
        float elapsed = 0f;
        float duration = pagesFlippingFadeOutDuration;

        while (elapsed < duration && pagesFlippingSound != null && pagesFlippingSound.isPlaying)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            pagesFlippingSound.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        if (pagesFlippingSound != null)
        {
            pagesFlippingSound.Stop();
            pagesFlippingSound.volume = startVolume;
            Debug.Log("翻页音效淡出完成并已停止");
        }
    }
}