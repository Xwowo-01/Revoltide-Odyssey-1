using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class AudioManager : MonoBehaviour
{
    [Header("音频源")]
    [SerializeField] private AudioSource bgmSource;

    [Header("全局音量（0~1）")]
    [Range(0, 1)] public float musicVolume = 1.0f;
    [Range(0, 1)] public float sfxVolume = 1.0f;

    [Header("对象池设置")]
    public bool preloadAllResources = true;
    public OnMissingBehavior onMissingBehavior = OnMissingBehavior.LoadFromResources;
    public AudioClip defaultClip;

    public enum OnMissingBehavior
    {
        LoadFromResources,
        ThrowError,
        UseDefault
    }

    // ---------- BGM 相关 ----------
    private Dictionary<string, AudioClip> audioPool = new Dictionary<string, AudioClip>();
    private Tween bgmTween;                      // 当前 BGM 动画（由 DOTween.To 创建）
    private string currentMusicName = null;
    private float bgmRelativeCurrent = 0f;      // 当前相对音量（0~1），不乘 musicVolume
    private float bgmRelativeTarget = 0f;       // 目标相对音量

    // ---------- SFX 相关 ----------
    private class SFXInstance
    {
        public GameObject gameObject;
        public AudioSource audioSource;
        public bool isLoop;
        public string clipName;
        public float relativeVolume;            // 用户传入的原始音量
    }

    private List<SFXInstance> sfxInstances = new List<SFXInstance>();
    private Dictionary<string, float> loopSFXVolumes = new Dictionary<string, float>();

    // ---------- 编辑器辅助 ----------
    private float _lastMusicVolume;
    private float _lastSfxVolume;

    // ========================================================
    // 生命周期
    // ========================================================

    private void Awake()
    {
        _lastMusicVolume = musicVolume;
        _lastSfxVolume = sfxVolume;
    }

    private void OnValidate()
    {
        if (!Mathf.Approximately(musicVolume, _lastMusicVolume))
        {
            UpdateMusicVolume(musicVolume);
            _lastMusicVolume = musicVolume;
        }
        if (!Mathf.Approximately(sfxVolume, _lastSfxVolume))
        {
            UpdateSFXVolume(sfxVolume);
            _lastSfxVolume = sfxVolume;
        }
    }

    // ========================================================
    // 运行时调整全局音量
    // ========================================================

    public void SetMusicVolume(float newVol)
    {
        newVol = Mathf.Clamp01(newVol);
        musicVolume = newVol;
        UpdateMusicVolume(newVol);
        _lastMusicVolume = newVol;
    }

    public void SetSFXVolume(float newVol)
    {
        newVol = Mathf.Clamp01(newVol);
        sfxVolume = newVol;
        UpdateSFXVolume(newVol);
        _lastSfxVolume = newVol;
    }

    // ========================================================
    // 核心方法
    // ========================================================

    public void InitAudio(string[] audioNames)
    {
        audioPool.Clear();
        if (audioNames != null && audioNames.Length > 0)
        {
            foreach (string name in audioNames)
                LoadAudioToPool(name);
        }
        else if (preloadAllResources)
        {
            AudioClip[] allClips = Resources.LoadAll<AudioClip>("Sounds");
            foreach (AudioClip clip in allClips)
            {
                if (!audioPool.ContainsKey(clip.name))
                    audioPool.Add(clip.name, clip);
            }
            Debug.Log($"音频对象池加载完成，共 {audioPool.Count} 个音频");
        }
        else
        {
            Debug.LogWarning("未指定音频列表，且 preloadAllResources 为 false，对象池为空。");
        }

        KillBgmTween();
        bgmSource.Stop();
        bgmSource.clip = null;
        currentMusicName = null;
        bgmRelativeCurrent = 0f;
        bgmRelativeTarget = 0f;
        bgmSource.volume = 0f;
        ClearAllSFX();
    }

    // ---- BGM ----
    public void FadeInMusic(string musicName, float duration, float targetVol)
    {
        KillBgmTween();
        bgmRelativeTarget = Mathf.Clamp01(targetVol);
        AudioClip clip = GetClipFromPool(musicName);
        if (clip == null) return;

        bgmSource.clip = clip;
        bgmSource.volume = 0f;
        bgmRelativeCurrent = 0f;
        currentMusicName = musicName;
        bgmSource.Play();

        bgmTween = DOTween.To(
            () => bgmRelativeCurrent,
            x =>
            {
                bgmRelativeCurrent = x;
                bgmSource.volume = x * musicVolume;
            },
            bgmRelativeTarget,
            duration
        ).OnComplete(() =>
        {
            bgmTween = null;
        });
    }

    public void FadeOutMusic(float duration)
    {
        if (bgmSource.clip == null || bgmSource.volume <= 0.001f)
        {
            Debug.Log("无音乐播放或已静音，跳过淡出");
            return;
        }

        KillBgmTween();
        bgmRelativeTarget = 0f;

        bgmTween = DOTween.To(
            () => bgmRelativeCurrent,
            x =>
            {
                bgmRelativeCurrent = x;
                bgmSource.volume = x * musicVolume;
            },
            0f,
            duration
        ).OnComplete(() =>
        {
            bgmSource.Stop();
            currentMusicName = null;
            bgmRelativeCurrent = 0f;
            bgmRelativeTarget = 0f;
            bgmTween = null;
        });
    }

    public void SwitchMusic(string musicName, float duration, float targetVol)
    {
        KillBgmTween();
        float half = duration * 0.5f;
        bgmRelativeTarget = Mathf.Clamp01(targetVol);

        if (bgmSource.clip == null || bgmSource.volume <= 0.001f)
        {
            FadeInMusic(musicName, duration, targetVol);
            return;
        }

        // 先淡出到 0
        bgmTween = DOTween.To(
            () => bgmRelativeCurrent,
            x =>
            {
                bgmRelativeCurrent = x;
                bgmSource.volume = x * musicVolume;
            },
            0f,
            half
        ).OnComplete(() =>
        {
            // 切换音频
            AudioClip clip = GetClipFromPool(musicName);
            if (clip == null) return;
            bgmSource.clip = clip;
            bgmSource.volume = 0f;
            bgmRelativeCurrent = 0f;
            currentMusicName = musicName;
            bgmSource.Play();

            // 淡入到目标
            bgmTween = DOTween.To(
                () => bgmRelativeCurrent,
                x =>
                {
                    bgmRelativeCurrent = x;
                    bgmSource.volume = x * musicVolume;
                },
                bgmRelativeTarget,
                half
            ).OnComplete(() =>
            {
                bgmTween = null;
            });
        });
    }

    // ---- SFX ----
    public void PlaySFX(string sfxName, string mode, float volume)
    {
        AudioClip clip = GetClipFromPool(sfxName);
        if (clip == null)
        {
            Debug.LogWarning($"音效 '{sfxName}' 未找到，无法播放");
            return;
        }

        float relativeVol = Mathf.Clamp01(volume);
        float finalVol = relativeVol * sfxVolume;
        bool isLoop = (mode == "循环");

        GameObject go = new GameObject($"SFX_{sfxName}_{(isLoop ? "Loop" : "OneShot")}");
        go.transform.SetParent(transform, false);
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = finalVol;
        src.loop = isLoop;
        src.Play();

        SFXInstance inst = new SFXInstance
        {
            gameObject = go,
            audioSource = src,
            isLoop = isLoop,
            clipName = sfxName,
            relativeVolume = relativeVol
        };
        sfxInstances.Add(inst);

        if (isLoop)
        {
            loopSFXVolumes[sfxName] = relativeVol;
        }
    }

    public void StopAllSFX(float fadeDuration = 0.5f)
    {
        if (sfxInstances.Count == 0) return;

        List<SFXInstance> instances = new List<SFXInstance>(sfxInstances);
        foreach (var inst in instances)
        {
            if (inst == null || inst.gameObject == null) continue;
            AudioSource src = inst.audioSource;
            if (src == null) continue;

            if (!inst.isLoop && !src.isPlaying)
            {
                Destroy(inst.gameObject);
                sfxInstances.Remove(inst);
                continue;
            }

            src.DOFade(0f, fadeDuration).OnComplete(() =>
            {
                if (inst.gameObject != null)
                {
                    src.Stop();
                    Destroy(inst.gameObject);
                }
            });
        }

        sfxInstances.Clear();
        loopSFXVolumes.Clear();
    }

    private void ClearAllSFX()
    {
        foreach (var inst in sfxInstances)
        {
            if (inst.gameObject != null)
                Destroy(inst.gameObject);
        }
        sfxInstances.Clear();
        loopSFXVolumes.Clear();
    }

    public void CompleteCurrent()
    {
        KillBgmTween();
    }

    // ========================================================
    // 存档与读档
    // ========================================================

    [System.Serializable]
    public class AudioSaveData
    {
        public string musicName;
        public float musicRelativeVolume;   // 相对音量（0~1）
        public bool isPlaying;
        public List<LoopSFXData> loopSFXList;
    }

    [System.Serializable]
    public class LoopSFXData
    {
        public string sfxName;
        public float volume;   // 相对音量
    }

    public AudioSaveData GetSaveData()
    {
        AudioSaveData data = new AudioSaveData
        {
            musicName = currentMusicName,
            musicRelativeVolume = bgmRelativeCurrent,
            isPlaying = bgmSource.isPlaying,
            loopSFXList = new List<LoopSFXData>()
        };

        foreach (var kvp in loopSFXVolumes)
        {
            data.loopSFXList.Add(new LoopSFXData { sfxName = kvp.Key, volume = kvp.Value });
        }
        return data;
    }

    public void LoadSaveData(AudioSaveData data)
    {
        KillBgmTween();
        ClearAllSFX();

        // 恢复 BGM
        if (!string.IsNullOrEmpty(data.musicName))
        {
            AudioClip clip = GetClipFromPool(data.musicName);
            if (clip != null)
            {
                bgmSource.clip = clip;
                bgmRelativeCurrent = Mathf.Clamp01(data.musicRelativeVolume);
                bgmRelativeTarget = bgmRelativeCurrent;
                bgmSource.volume = bgmRelativeCurrent * musicVolume;
                currentMusicName = data.musicName;
                if (data.isPlaying)
                    bgmSource.Play();
                else
                    bgmSource.Stop();
            }
            else
            {
                bgmSource.clip = null;
                bgmSource.Stop();
                currentMusicName = null;
                bgmRelativeCurrent = 0f;
                bgmRelativeTarget = 0f;
            }
        }
        else
        {
            bgmSource.clip = null;
            bgmSource.Stop();
            currentMusicName = null;
            bgmRelativeCurrent = 0f;
            bgmRelativeTarget = 0f;
        }

        // 恢复循环音效
        if (data.loopSFXList != null)
        {
            foreach (var sfxData in data.loopSFXList)
            {
                PlaySFX(sfxData.sfxName, "循环", sfxData.volume);
            }
        }
    }

    // ========================================================
    // 私有辅助
    // ========================================================

    private void LoadAudioToPool(string name)
    {
        if (audioPool.ContainsKey(name)) return;
        AudioClip clip = Resources.Load<AudioClip>("Sounds/" + name);
        if (clip != null) audioPool.Add(name, clip);
        else Debug.LogWarning($"音频加载失败: {name}");
    }

    private AudioClip GetClipFromPool(string name)
    {
        if (audioPool.TryGetValue(name, out AudioClip clip)) return clip;

        switch (onMissingBehavior)
        {
            case OnMissingBehavior.LoadFromResources:
                LoadAudioToPool(name);
                if (audioPool.TryGetValue(name, out clip)) return clip;
                else goto case OnMissingBehavior.UseDefault;
            case OnMissingBehavior.UseDefault:
                return defaultClip;
            case OnMissingBehavior.ThrowError:
            default:
                Debug.LogError($"对象池中无音频: {name}");
                return null;
        }
    }

    private void KillBgmTween()
    {
        if (bgmTween != null)
        {
            bgmTween.Kill(true);
            bgmTween = null;
        }
    }

    private void OnDestroy()
    {
        ClearAllSFX();
    }

    // ========================================================
    // 全局音量实时更新
    // ========================================================

    private void UpdateMusicVolume(float newMusicVol)
    {
        if (bgmSource == null) return;

        // 直接根据当前相对音量计算绝对音量
        bgmSource.volume = bgmRelativeCurrent * newMusicVol;

        // 如果当前有动画，动画的 OnUpdate 会持续更新，所以无需额外操作
        // 但为了立即响应，我们已设置当前音量，下次动画帧会再次覆盖，但目标值不变，
        // 所以音量会保持在正确值（因为 OnUpdate 使用 x * musicVolume，而 musicVolume 已变）
        // 注意：bgmRelativeCurrent 是动画驱动的，不会因 musicVolume 改变而改变。
    }

    private void UpdateSFXVolume(float newSfxVol)
    {
        foreach (var inst in sfxInstances)
        {
            if (inst.audioSource != null)
            {
                inst.audioSource.volume = inst.relativeVolume * newSfxVol;
            }
        }
    }
}