using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class AudioManager : MonoBehaviour
{
    [Header("音频源")]
    [SerializeField] private AudioSource bgmSource;   // BGM播放器

    [Header("全局音量（0~1）")]
    public float musicVolume = 1.0f;
    public float sfxVolume = 1.0f;

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

    // ---------- 私有成员 ----------
    private Dictionary<string, AudioClip> audioPool = new Dictionary<string, AudioClip>();
    private Tween currentTween;                 // BGM 渐变动画
    private string currentMusicName = null;
    private float currentVolume = 0f;
    private float targetVolume = 0f;

    // ---- 音效管理 ----
    private class SFXInstance
    {
        public GameObject gameObject;
        public AudioSource audioSource;
        public bool isLoop;
        public string clipName;
        public float volume;
    }

    private List<SFXInstance> sfxInstances = new List<SFXInstance>(); // 用于存档的循环音效列表（名称->音量）
    private Dictionary<string, float> loopSFXVolumes = new Dictionary<string, float>();

    // ========================================================
    // 公共方法
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
        StopAllCoroutines(); // 重置BGM状态
        KillCurrentTween();
        bgmSource.Stop();
        bgmSource.clip = null;
        currentMusicName = null;
        currentVolume = 0f;
        targetVolume = 0f;
        bgmSource.volume = 0f;        
        ClearAllSFX();// 清理所有音效
    }

    // ---- BGM 方法 ----
    public void FadeInMusic(string musicName, float duration, float targetVol)
    {
        KillCurrentTween();
        float vol = Mathf.Clamp01(targetVol) * musicVolume;
        AudioClip clip = GetClipFromPool(musicName);
        if (clip == null) return;
        bgmSource.clip = clip;
        bgmSource.volume = 0f;
        currentVolume = 0f;
        targetVolume = vol;
        currentMusicName = musicName;
        bgmSource.Play();
        currentTween = bgmSource.DOFade(vol, duration)
            .OnUpdate(() => { currentVolume = bgmSource.volume; })
            .OnComplete(() => { currentTween = null; });
    }

    public void FadeOutMusic(float duration)
    {
        if (bgmSource.clip == null || bgmSource.volume <= 0.001f)
        {
            Debug.Log("无音乐播放或已静音，跳过淡出");
            return;
        }
        KillCurrentTween();
        targetVolume = 0f;
        currentTween = bgmSource.DOFade(0f, duration)
            .OnUpdate(() => { currentVolume = bgmSource.volume; })
            .OnComplete(() =>
            {
                bgmSource.Stop();
                currentMusicName = null;
                currentVolume = 0f;
                currentTween = null;
            });
    }

    public void SwitchMusic(string musicName, float duration, float targetVol)
    {
        KillCurrentTween();
        float half = duration * 0.5f;
        float vol = Mathf.Clamp01(targetVol) * musicVolume;
        if (bgmSource.clip == null || bgmSource.volume <= 0.001f)
        {
            FadeInMusic(musicName, duration, targetVol);
            return;
        }
        targetVolume = 0f;
        Tween fadeOut = bgmSource.DOFade(0f, half)
            .OnUpdate(() => { currentVolume = bgmSource.volume; })
            .OnComplete(() =>
            {
                AudioClip clip = GetClipFromPool(musicName);
                if (clip == null) return;
                bgmSource.clip = clip;
                bgmSource.volume = 0f;
                currentVolume = 0f;
                currentMusicName = musicName;
                bgmSource.Play();

                Tween fadeIn = bgmSource.DOFade(vol, half)
                    .OnUpdate(() => { currentVolume = bgmSource.volume; })
                    .OnComplete(() =>
                    {
                        targetVolume = vol;
                        currentTween = null;
                    });

                currentTween = fadeIn;
            });

        currentTween = fadeOut;
        targetVolume = vol;
    }
       
    public void PlaySFX(string sfxName, string mode, float volume) // ---- 音效方法 ----
    {
        AudioClip clip = GetClipFromPool(sfxName);
        if (clip == null)
        {
            Debug.LogWarning($"音效 '{sfxName}' 未找到，无法播放");
            return;
        }
        float finalVol = Mathf.Clamp01(volume) * sfxVolume;
        bool isLoop = (mode == "循环");        
        GameObject go = new GameObject($"SFX_{sfxName}_{(isLoop ? "Loop" : "OneShot")}");// 创建音效对象
        go.transform.SetParent(transform, false);
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = finalVol;
        src.loop = isLoop;
        src.Play();
        SFXInstance instance = new SFXInstance();
        instance.gameObject = go;
        instance.audioSource = src;
        instance.isLoop = isLoop;
        instance.clipName = sfxName;
        instance.volume = finalVol;
        sfxInstances.Add(instance);       
        if (isLoop) // 如果循环，记录到存档字典
        {
            loopSFXVolumes[sfxName] = finalVol;
        }
    }

    /// <summary>
    /// 停止所有音效（淡出后删除），供继续剧情时调用
    /// </summary>
    public void StopAllSFX(float fadeDuration = 0.5f)
    {
        if (sfxInstances.Count == 0) return;       
        List<SFXInstance> instances = new List<SFXInstance>(sfxInstances); // 复制列表，因为会在回调中修改
        foreach (var inst in instances)        {
            if (inst == null || inst.gameObject == null) continue;
            AudioSource src = inst.audioSource;
            if (src == null) continue;                        
            if (!inst.isLoop && !src.isPlaying)// 如果音效已经停止播放（单次已播完），直接销毁
            {
                Destroy(inst.gameObject);
                sfxInstances.Remove(inst);
                continue;
            }           
            src.DOFade(0f, fadeDuration).OnComplete(() => // 否则淡出
            {
                if (inst.gameObject != null)
                {
                    src.Stop();
                    Destroy(inst.gameObject);
                }
            });
        }       
        sfxInstances.Clear(); // 清空列表（因为上面的回调会异步删除，但我们先从列表中移除，避免重复操作）       
        loopSFXVolumes.Clear(); // 清空循环音效记录
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

    /// <summary>
    /// 立即完成当前BGM动画（仅用于紧急清理，不应用于Skip）
    /// </summary>
    public void CompleteCurrent()
    {
        if (currentTween != null)
        {
            currentTween.Kill(true);
            currentTween = null;
        } // 不修改音量，保留当前播放状态
    }

    // ========================================================
    // 存档与读档
    // ========================================================

    [System.Serializable]
    public class AudioSaveData
    {
        public string musicName;
        public float volume;
        public bool isPlaying;
        public List<LoopSFXData> loopSFXList; // 循环音效列表
    }

    [System.Serializable]
    public class LoopSFXData
    {
        public string sfxName;
        public float volume;
    }

    public AudioSaveData GetSaveData()
    {
        AudioSaveData data = new AudioSaveData();
        data.musicName = currentMusicName;
        data.volume = bgmSource.volume;
        data.isPlaying = bgmSource.isPlaying;

        // 保存循环音效
        data.loopSFXList = new List<LoopSFXData>();
        foreach (var kvp in loopSFXVolumes)
        {
            LoopSFXData sfxData = new LoopSFXData();
            sfxData.sfxName = kvp.Key;
            sfxData.volume = kvp.Value;
            data.loopSFXList.Add(sfxData);
        }
        return data;
    }

    public void LoadSaveData(AudioSaveData data)
    {        
        KillCurrentTween();// 清理当前状态
        ClearAllSFX();        
        if (!string.IsNullOrEmpty(data.musicName))// 恢复BGM
        {
            AudioClip clip = GetClipFromPool(data.musicName);
            if (clip != null)
            {
                bgmSource.clip = clip;
                bgmSource.volume = data.volume;
                currentVolume = data.volume;
                targetVolume = data.volume;
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
            }
        }
        else
        {
            bgmSource.clip = null;
            bgmSource.Stop();
            currentMusicName = null;
        }       
        if (data.loopSFXList != null) // 恢复循环音效
        {
            foreach (var sfxData in data.loopSFXList)
            {               
                PlaySFX(sfxData.sfxName, "循环", sfxData.volume); // 重新创建并播放
            }
        }
    }

    // ========================================================
    // 私有辅助方法
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

    private void KillCurrentTween()
    {
        if (currentTween != null)
        {
            currentTween.Kill(true);
            currentTween = null;
        }
    }

    private void OnDestroy()
    {
        ClearAllSFX();
    }
}