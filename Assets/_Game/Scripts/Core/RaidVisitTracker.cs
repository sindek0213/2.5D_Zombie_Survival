using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Core
{
    // 플레이어가 한 레이드에서 실제로 들어간 학교 구역을 중복 없이 기록합니다.
    // 방문 기록을 RaidSession과 분리해 종료·사망 처리 코드를 키우지 않고, 이후 공장과 숲은 각 지역 판정만 추가할 수 있게 합니다.
    public sealed class RaidVisitTracker : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private bool trackSchoolZones = true;

        private readonly HashSet<string> visitedZoneIds = new HashSet<string>();

        public int VisitedZoneCount => visitedZoneIds.Count;

        // 씬 생성기가 실제 플레이어와 현재 지역 종류를 연결합니다.
        // 학교가 아닌 지역에서 학교 좌표 규칙을 잘못 사용하지 않도록 추적 여부를 함께 전달합니다.
        public void Configure(Transform playerTransform, bool useSchoolZones)
        {
            player = playerTransform;
            trackSchoolZones = useSchoolZones;
            RecordCurrentZone();
        }

        private void Update()
        {
            RecordCurrentZone();
        }

        private void RecordCurrentZone()
        {
            if (!trackSchoolZones || player == null) return;
            string zoneId = SchoolRaidMapLayout.GetZoneId(player.position);
            if (!string.IsNullOrWhiteSpace(zoneId)) visitedZoneIds.Add(zoneId);
        }

        // RaidResult가 HashSet 구현에 의존하지 않도록 저장 가능한 새 List를 반환합니다.
        // 호출자가 목록을 바꿔도 현재 레이드의 내부 방문 기록에는 영향을 주지 않습니다.
        public List<string> CaptureVisitedZoneIds()
        {
            return new List<string>(visitedZoneIds);
        }
    }
}
