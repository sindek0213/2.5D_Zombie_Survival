using System;
using UnityEngine;

namespace ZombieSurvival.AI
{
    // 총성이나 특수 좀비의 울음처럼 여러 좀비가 함께 들을 수 있는 월드 소음을 전달하는 가벼운 이벤트 통로입니다.
    // 씬 오브젝트를 매번 검색하지 않고 살아 있는 ZombieBrain만 이벤트를 구독하므로 좀비 수가 늘어도 불필요한 전체 탐색이 없습니다.
    public static class ZombieNoiseSystem
    {
        // 같은 반경이라도 무엇이 만든 소리인지 구분하면 AI와 HUD가 서로 다른 반응을 만들 수 있습니다.
        // 예를 들어 발소리는 조사만 유도하고, 비명은 플레이어에게 특수 좀비 경고도 함께 표시합니다.
        public enum NoiseKind
        {
            Unknown,
            Footstep,
            Gunshot,
            Melee,
            Scream
        }

        public readonly struct NoiseEvent
        {
            public readonly Vector3 Position;
            public readonly float Radius;
            public readonly NoiseKind Kind;

            public NoiseEvent(Vector3 position, float radius, NoiseKind kind = NoiseKind.Unknown)
            {
                Position = position;
                Radius = Mathf.Max(0f, radius);
                Kind = kind;
            }
        }

        public static event Action<NoiseEvent> NoiseEmitted;
        // 특수 좀비 경고는 별도 UI가 소음 전체를 해석하지 않아도 되도록 짧은 문장으로 전달합니다.
        public static event Action<string> ThreatWarning;

        public static void Emit(Vector3 position, float radius, NoiseKind kind = NoiseKind.Unknown)
        {
            if (radius <= 0f) return;
            NoiseEmitted?.Invoke(new NoiseEvent(position, radius, kind));
            if (kind == NoiseKind.Scream)
                ThreatWarning?.Invoke("울부짖는 좀비가 주변 적을 불러모았습니다!");
        }

        // 도메인 리로드를 끈 에디터에서도 이전 Play 세션의 파괴된 ZombieBrain 구독이 남지 않게 합니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeSession()
        {
            NoiseEmitted = null;
            ThreatWarning = null;
        }
    }
}
