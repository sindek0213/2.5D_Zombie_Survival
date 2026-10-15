using UnityEngine;

namespace ZombieSurvival.Core
{
    // 월드 Y좌표를 1층, 2층처럼 사람이 이해하기 쉬운 층 번호로 바꾸는 공용 계산 도우미입니다.
    // MonoBehaviour가 아니므로 플레이어나 좀비에 새 컴포넌트를 붙이지 않아도 필요한 시스템에서 같은 판정 규칙을 사용할 수 있습니다.
    public static class FloorLevelUtility
    {
        // floorHeight는 한 층의 바닥에서 다음 층 바닥까지의 높이입니다.
        // 경사로나 계단에서는 두 바닥 높이의 중간을 넘는 순간 다음 층으로 판정해 층 번호가 바닥 근처에서 흔들리지 않게 합니다.
        public static int ResolveFloorLevel(float worldY, float floorHeight)
        {
            float safeFloorHeight = Mathf.Max(0.1f, floorHeight);
            float nonNegativeHeight = Mathf.Max(0f, worldY);
            return Mathf.FloorToInt((nonNegativeHeight + safeFloorHeight * 0.5f) / safeFloorHeight) + 1;
        }

        // 두 위치가 같은 층인지 검사합니다.
        // 각 AI가 서로 다른 임의 기준을 사용하지 않게 ResolveFloorLevel 호출을 한곳에 모았습니다.
        public static bool AreOnSameFloor(Vector3 first, Vector3 second, float floorHeight)
        {
            return ResolveFloorLevel(first.y, floorHeight) == ResolveFloorLevel(second.y, floorHeight);
        }
    }
}
