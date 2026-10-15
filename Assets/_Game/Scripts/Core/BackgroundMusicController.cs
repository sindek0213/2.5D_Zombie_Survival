using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZombieSurvival.Core
{
    // 씬마다 별도 오브젝트를 배치하지 않아도 타이틀, 거점과 학교 음악을 자동으로 선택합니다.
    // Loading 씬에서는 재생 중인 곡을 조용히 유지하고 목적지에 도착했을 때 두 AudioSource를 교차시켜 끊김을 숨깁니다.
    public sealed class BackgroundMusicController : MonoBehaviour
    {
        public const string TitleMusicResource = "Audio/Music/BGM_Title_LastLight";
        public const string HideoutMusicResource = "Audio/Music/BGM_Hideout_EmbersRemain";
        public const string SchoolMusicResource = "Audio/Music/BGM_School_SilentBell";

        private const float TitleVolume = 0.36f;
        private const float HideoutVolume = 0.3f;
        private const float SchoolVolume = 0.27f;
        private const float LoadingVolumeMultiplier = 0.42f;
        private const float SceneCrossfadeSeconds = 2.4f;
        private const float LoadingFadeSeconds = 1.2f;

        private static BackgroundMusicController instance;

        private readonly AudioSource[] sources = new AudioSource[2];
        private int activeSourceIndex;
        private string activeResourcePath;
        private float activeNominalVolume;

        private bool fadeRunning;
        private float fadeElapsed;
        private float fadeDuration;
        private readonly float[] fadeStartVolumes = new float[2];
        private readonly float[] fadeTargetVolumes = new float[2];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreatePersistentPlayer()
        {
            if (instance != null) return;

            var musicObject = new GameObject("[Background Music]");
            instance = musicObject.AddComponent<BackgroundMusicController>();
            DontDestroyOnLoad(musicObject);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            sources[0] = CreateSource("Music A");
            sources[1] = CreateSource("Music B");
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            ApplySceneMusic(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (!fadeRunning) return;

            fadeElapsed += Time.unscaledDeltaTime;
            float progress = fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / fadeDuration);
            // SmoothStep은 선형 페이드보다 시작과 끝의 음량 변화가 덜 튀어 장면 전환에 자연스럽습니다.
            float eased = progress * progress * (3f - 2f * progress);
            for (int i = 0; i < sources.Length; i++)
                sources[i].volume = Mathf.Lerp(fadeStartVolumes[i], fadeTargetVolumes[i], eased);

            if (progress < 1f) return;
            fadeRunning = false;
            StopSilentInactiveSource();
        }

        private AudioSource CreateSource(string sourceName)
        {
            var sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 16;
            source.volume = 0f;
            return source;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplySceneMusic(scene.name);
        }

        private void ApplySceneMusic(string sceneName)
        {
            if (sceneName == GameSceneNavigator.LoadingSceneName)
            {
                if (string.IsNullOrEmpty(activeResourcePath)) return;
                BeginFade(
                    activeSourceIndex == 0 ? activeNominalVolume * LoadingVolumeMultiplier : 0f,
                    activeSourceIndex == 1 ? activeNominalVolume * LoadingVolumeMultiplier : 0f,
                    LoadingFadeSeconds);
                return;
            }

            string resourcePath = GetMusicResourceForScene(sceneName);
            if (string.IsNullOrEmpty(resourcePath))
            {
                BeginFade(0f, 0f, SceneCrossfadeSeconds);
                activeResourcePath = null;
                activeNominalVolume = 0f;
                return;
            }

            float nominalVolume = GetMusicVolumeForScene(sceneName);
            if (resourcePath == activeResourcePath)
            {
                activeNominalVolume = nominalVolume;
                BeginFade(
                    activeSourceIndex == 0 ? nominalVolume : 0f,
                    activeSourceIndex == 1 ? nominalVolume : 0f,
                    SceneCrossfadeSeconds);
                return;
            }

            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null)
            {
                Debug.LogWarning($"배경음악을 불러오지 못했습니다: Resources/{resourcePath}");
                return;
            }

            int nextSourceIndex = 1 - activeSourceIndex;
            AudioSource nextSource = sources[nextSourceIndex];
            nextSource.Stop();
            nextSource.clip = clip;
            nextSource.time = 0f;
            nextSource.volume = 0f;
            nextSource.Play();

            activeSourceIndex = nextSourceIndex;
            activeResourcePath = resourcePath;
            activeNominalVolume = nominalVolume;
            BeginFade(
                activeSourceIndex == 0 ? nominalVolume : 0f,
                activeSourceIndex == 1 ? nominalVolume : 0f,
                SceneCrossfadeSeconds);
        }

        private void BeginFade(float sourceATarget, float sourceBTarget, float duration)
        {
            fadeStartVolumes[0] = sources[0].volume;
            fadeStartVolumes[1] = sources[1].volume;
            fadeTargetVolumes[0] = Mathf.Clamp01(sourceATarget);
            fadeTargetVolumes[1] = Mathf.Clamp01(sourceBTarget);
            fadeElapsed = 0f;
            fadeDuration = Mathf.Max(0.01f, duration);
            fadeRunning = true;
        }

        private void StopSilentInactiveSource()
        {
            int inactiveSourceIndex = 1 - activeSourceIndex;
            AudioSource inactive = sources[inactiveSourceIndex];
            if (inactive.volume > 0.0001f) return;
            inactive.Stop();
            inactive.clip = null;
        }

        public static string GetMusicResourceForScene(string sceneName)
        {
            if (sceneName == GameSceneNavigator.TitleSceneName) return TitleMusicResource;
            if (sceneName == GameSceneNavigator.HideoutSceneName) return HideoutMusicResource;
            if (GameSceneNavigator.IsRaidScene(sceneName)) return SchoolMusicResource;
            return null;
        }

        public static float GetMusicVolumeForScene(string sceneName)
        {
            if (sceneName == GameSceneNavigator.TitleSceneName) return TitleVolume;
            if (sceneName == GameSceneNavigator.HideoutSceneName) return HideoutVolume;
            if (GameSceneNavigator.IsRaidScene(sceneName)) return SchoolVolume;
            return 0f;
        }
    }
}
