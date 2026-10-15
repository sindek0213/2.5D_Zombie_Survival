using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Core
{
    // 플레이어가 설정 화면에서 바꾼 값을 PlayerPrefs에 저장하고 게임의 각 시스템에 같은 값을 제공합니다.
    // 거점 저장 데이터와 분리했기 때문에 새 게임을 시작하거나 레이드 아이템을 잃어도 기기 설정은 그대로 유지됩니다.
    public static class GameSettings
    {
        private const string MasterVolumeKey = "settings.masterVolume";
        private const string EffectsVolumeKey = "settings.effectsVolume";
        private const string MouseSensitivityKey = "settings.mouseSensitivity";
        private const string AimSensitivityKey = "settings.aimSensitivity";
        private const string ResolutionWidthKey = "settings.resolutionWidth";
        private const string ResolutionHeightKey = "settings.resolutionHeight";
        private const string FullscreenKey = "settings.fullscreen";
        private const string FontScaleKey = "settings.fontScale";
        private const string DamageEffectIntensityKey = "settings.damageEffectIntensity";
        private const string ScreenShakeIntensityKey = "settings.screenShakeIntensity";

        private const float DefaultMasterVolume = 1f;
        private const float DefaultEffectsVolume = 0.8f;
        private const float DefaultMouseSensitivity = 1f;
        private const float DefaultAimSensitivity = 0.8f;

        private static bool loaded;
        private static float masterVolume;
        private static float effectsVolume;
        private static float mouseSensitivity;
        private static float aimSensitivity;
        private static int resolutionWidth;
        private static int resolutionHeight;
        private static bool fullscreen;
        private static float fontScale;
        private static float damageEffectIntensity;
        private static float screenShakeIntensity;

        // 설정 화면이 값을 바꾼 직후 다른 HUD도 표시를 갱신할 수 있도록 한 개의 공용 변경 이벤트를 제공합니다.
        public static event Action Changed;

        public static float MasterVolume
        {
            get
            {
                EnsureLoaded();
                return masterVolume;
            }
        }

        public static float EffectsVolume
        {
            get
            {
                EnsureLoaded();
                return effectsVolume;
            }
        }

        public static float MouseSensitivity
        {
            get
            {
                EnsureLoaded();
                return mouseSensitivity;
            }
        }

        public static float AimSensitivity
        {
            get
            {
                EnsureLoaded();
                return aimSensitivity;
            }
        }

        public static bool Fullscreen
        {
            get
            {
                EnsureLoaded();
                return fullscreen;
            }
        }

        public static int ResolutionWidth
        {
            get
            {
                EnsureLoaded();
                return resolutionWidth;
            }
        }

        public static int ResolutionHeight
        {
            get
            {
                EnsureLoaded();
                return resolutionHeight;
            }
        }

        public static float FontScale { get { EnsureLoaded(); return fontScale; } }
        public static float DamageEffectIntensity { get { EnsureLoaded(); return damageEffectIntensity; } }
        public static float ScreenShakeIntensity { get { EnsureLoaded(); return screenShakeIntensity; } }

        // 첫 씬이 열리기 전에 저장값을 읽고 음량과 화면 모드를 적용합니다.
        // 저장된 해상도가 없는 첫 실행에는 현재 운영체제 해상도를 유지해 갑자기 창 크기가 바뀌지 않게 합니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySavedSettingsOnStartup()
        {
            EnsureLoaded();
            ApplyAudioVolume();
            if (PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey(ResolutionHeightKey))
                ApplyDisplayMode();
        }

        public static void SetMasterVolume(float value)
        {
            EnsureLoaded();
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
            ApplyAudioVolume();
            CommitChange();
        }

        public static void SetEffectsVolume(float value)
        {
            EnsureLoaded();
            effectsVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(EffectsVolumeKey, effectsVolume);
            CommitChange();
        }

        public static void SetMouseSensitivity(float value)
        {
            EnsureLoaded();
            mouseSensitivity = ClampSensitivity(value);
            PlayerPrefs.SetFloat(MouseSensitivityKey, mouseSensitivity);
            CommitChange();
        }

        public static void SetAimSensitivity(float value)
        {
            EnsureLoaded();
            aimSensitivity = ClampSensitivity(value);
            PlayerPrefs.SetFloat(AimSensitivityKey, aimSensitivity);
            CommitChange();
        }

        // 해상도 목록에서 선택한 너비와 높이를 저장한 뒤 현재 전체 화면 모드와 함께 즉시 적용합니다.
        // 주사율은 운영체제와 Unity가 해당 해상도에서 지원하는 값을 고르게 해 잘못된 고정값 때문에 화면 전환이 실패하지 않게 합니다.
        public static void SetResolution(int width, int height)
        {
            EnsureLoaded();
            resolutionWidth = Mathf.Max(640, width);
            resolutionHeight = Mathf.Max(360, height);
            PlayerPrefs.SetInt(ResolutionWidthKey, resolutionWidth);
            PlayerPrefs.SetInt(ResolutionHeightKey, resolutionHeight);
            ApplyDisplayMode();
            CommitChange();
        }

        public static void SetFullscreen(bool value)
        {
            EnsureLoaded();
            fullscreen = value;
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            ApplyDisplayMode();
            CommitChange();
        }

        public static void SetFontScale(float value)
        {
            EnsureLoaded();
            fontScale = Mathf.Clamp(value, 0.85f, 1.4f);
            PlayerPrefs.SetFloat(FontScaleKey, fontScale);
            CommitChange();
        }

        public static void SetDamageEffectIntensity(float value)
        {
            EnsureLoaded();
            damageEffectIntensity = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(DamageEffectIntensityKey, damageEffectIntensity);
            CommitChange();
        }

        public static void SetScreenShakeIntensity(float value)
        {
            EnsureLoaded();
            screenShakeIntensity = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(ScreenShakeIntensityKey, screenShakeIntensity);
            CommitChange();
        }

        // Screen.resolutions에는 같은 너비·높이가 여러 주사율로 반복될 수 있습니다.
        // 설정 UI에서는 체감상 같은 선택지가 중복되지 않도록 너비와 높이 조합만 남깁니다.
        public static Resolution[] GetAvailableResolutions()
        {
            Resolution[] source = Screen.resolutions;
            if (source == null || source.Length == 0)
            {
                return new[]
                {
                    new Resolution
                    {
                        width = Mathf.Max(640, Screen.width),
                        height = Mathf.Max(360, Screen.height)
                    }
                };
            }

            var unique = new List<Resolution>();
            var seen = new HashSet<string>();
            foreach (Resolution resolution in source)
            {
                string key = resolution.width + "x" + resolution.height;
                if (seen.Add(key)) unique.Add(resolution);
            }
            return unique.ToArray();
        }

        // 저장된 해상도와 정확히 같은 항목을 우선 찾고 없으면 현재 화면과 가장 가까운 마지막 항목을 반환합니다.
        // 모니터를 바꿔 예전 해상도가 사라진 경우에도 좌우 버튼이 유효한 위치에서 시작하게 합니다.
        public static int FindSelectedResolutionIndex(Resolution[] resolutions)
        {
            EnsureLoaded();
            if (resolutions == null || resolutions.Length == 0) return 0;

            int bestIndex = 0;
            long bestDistance = long.MaxValue;
            for (int i = 0; i < resolutions.Length; i++)
            {
                if (resolutions[i].width == resolutionWidth && resolutions[i].height == resolutionHeight)
                    return i;

                long widthDifference = resolutions[i].width - resolutionWidth;
                long heightDifference = resolutions[i].height - resolutionHeight;
                long distance = widthDifference * widthDifference + heightDifference * heightDifference;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = i;
            }
            return bestIndex;
        }

        // 총성·피격음처럼 개별 효과음이 자신의 기본 크기에 곱할 공용 배율입니다.
        // 마스터 음량은 AudioListener에서 이미 전체 출력에 적용되므로 여기서는 효과음 값만 반환합니다.
        public static float ScaleEffectVolume(float sourceVolume)
        {
            return Mathf.Clamp01(sourceVolume) * EffectsVolume;
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, DefaultMasterVolume));
            effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsVolumeKey, DefaultEffectsVolume));
            mouseSensitivity = ClampSensitivity(PlayerPrefs.GetFloat(MouseSensitivityKey, DefaultMouseSensitivity));
            aimSensitivity = ClampSensitivity(PlayerPrefs.GetFloat(AimSensitivityKey, DefaultAimSensitivity));
            resolutionWidth = Mathf.Max(640, PlayerPrefs.GetInt(ResolutionWidthKey, Mathf.Max(640, Screen.width)));
            resolutionHeight = Mathf.Max(360, PlayerPrefs.GetInt(ResolutionHeightKey, Mathf.Max(360, Screen.height)));
            fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            fontScale = Mathf.Clamp(PlayerPrefs.GetFloat(FontScaleKey, 1f), 0.85f, 1.4f);
            damageEffectIntensity = Mathf.Clamp01(PlayerPrefs.GetFloat(DamageEffectIntensityKey, 1f));
            screenShakeIntensity = Mathf.Clamp01(PlayerPrefs.GetFloat(ScreenShakeIntensityKey, 1f));
        }

        private static float ClampSensitivity(float value) => Mathf.Clamp(value, 0.25f, 2f);

        private static void ApplyAudioVolume()
        {
            // AudioListener.volume은 게임의 모든 AudioSource에 한 번만 곱해지는 Unity 공용 마스터 음량입니다.
            AudioListener.volume = masterVolume;
        }

        private static void ApplyDisplayMode()
        {
            Screen.SetResolution(
                resolutionWidth,
                resolutionHeight,
                fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        private static void CommitChange()
        {
            // 옵션은 항목을 누른 순간 디스크에 기록해 강제 종료가 발생해도 마지막 선택을 잃지 않게 합니다.
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
