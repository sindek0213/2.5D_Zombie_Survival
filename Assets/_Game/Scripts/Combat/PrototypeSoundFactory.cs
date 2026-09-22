using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 외부 음원 에셋이 없는 프로토타입에서도 발사·피격·사망을 구분할 수 있도록 짧은 파형을 생성합니다.
    // 완성 음원이 준비되면 Feedback 컴포넌트의 생성 부분만 AudioClip 에셋 참조로 교체하고 전투 이벤트 구조는 그대로 유지할 수 있습니다.
    public static class PrototypeSoundFactory
    {
        public const int SampleRate = 22050;

        public static AudioClip CreateGunshotClip()
            => CreateClip("Prototype_Gunshot", GenerateGunshotSamples());

        public static AudioClip CreateImpactClip(bool player)
            => CreateClip(player ? "Prototype_PlayerHurt" : "Prototype_ZombieHit", GenerateImpactSamples(player));

        public static AudioClip CreateDeathClip(bool player)
            => CreateClip(player ? "Prototype_PlayerDeath" : "Prototype_ZombieDeath", GenerateDeathSamples(player));

        // 총성은 매우 짧게 감쇠하는 노이즈와 낮은 주파수의 충격음을 섞어 클릭보다 무게감 있게 만듭니다.
        public static float[] GenerateGunshotSamples()
        {
            int length = Mathf.CeilToInt(SampleRate * 0.12f);
            var samples = new float[length];
            uint noiseState = 0xA341316Cu;
            for (int i = 0; i < length; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)Mathf.Max(1, length - 1);
                float envelope = Mathf.Pow(1f - progress, 3.4f);
                float noise = NextSignedNoise(ref noiseState);
                float lowPunch = Mathf.Sin(2f * Mathf.PI * (105f - 45f * progress) * time);
                samples[i] = Mathf.Clamp((noise * 0.72f + lowPunch * 0.38f) * envelope, -1f, 1f);
            }
            return samples;
        }

        // 플레이어 피격은 낮은 둔탁한 음, 좀비 피격은 조금 높은 마찰음으로 만들어 화면을 보지 않아도 대상을 구분할 수 있게 합니다.
        public static float[] GenerateImpactSamples(bool player)
        {
            int length = Mathf.CeilToInt(SampleRate * (player ? 0.16f : 0.09f));
            var samples = new float[length];
            uint noiseState = player ? 0xC8013EA4u : 0xAD90777Du;
            float frequency = player ? 145f : 420f;
            for (int i = 0; i < length; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)Mathf.Max(1, length - 1);
                float envelope = Mathf.Pow(1f - progress, 2.2f);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * time);
                float noise = NextSignedNoise(ref noiseState);
                samples[i] = Mathf.Clamp((tone * 0.62f + noise * 0.3f) * envelope, -1f, 1f);
            }
            return samples;
        }

        // 사망음은 시간이 지날수록 주파수가 내려가는 톤을 사용해 일반 피격음보다 길고 확실하게 구분합니다.
        public static float[] GenerateDeathSamples(bool player)
        {
            int length = Mathf.CeilToInt(SampleRate * (player ? 0.5f : 0.38f));
            var samples = new float[length];
            uint noiseState = player ? 0x9E3779B9u : 0x7F4A7C15u;
            for (int i = 0; i < length; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)Mathf.Max(1, length - 1);
                float frequency = Mathf.Lerp(player ? 170f : 135f, player ? 55f : 42f, progress);
                float envelope = Mathf.Pow(1f - progress, 1.35f);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * time);
                float roughness = NextSignedNoise(ref noiseState) * (1f - progress) * 0.22f;
                samples[i] = Mathf.Clamp((tone * 0.68f + roughness) * envelope, -1f, 1f);
            }
            return samples;
        }

        private static AudioClip CreateClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // UnityEngine.Random을 사용하면 게임의 아이템·AI 난수 순서가 달라질 수 있으므로 효과음 전용 결정적 난수 값을 직접 계산합니다.
        private static float NextSignedNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state / (float)uint.MaxValue) * 2f - 1f;
        }
    }
}
