using UnityEngine;

namespace ZombieSurvival.Core
{
    // RaidPrototype 맵에서 여러 시스템이 함께 사용하는 핵심 위치와 경계 크기를 한곳에 모읍니다.
    // 맵 생성 코드, 좀비 스폰 코드와 자동 테스트가 같은 값을 읽으므로 한쪽 좌표만 바뀌어 서로 어긋나는 문제를 막습니다.
    public static class RaidMapLayout
    {
        // 맵 중심에서 외곽 벽 중심까지의 거리입니다. 실제 이동 가능한 공간은 벽 두께 때문에 이 값보다 약간 작습니다.
        public const float MapHalfExtent = 18f;

        // 플레이어는 남쪽 캠프에서 시작하고 북쪽 끝의 탈출 지점을 목표로 이동합니다.
        // 두 지점을 멀리 분리해 서쪽과 동쪽 파밍 구역을 거치는 선택이 의미 있게 만듭니다.
        public static readonly Vector3 PlayerStartPosition = new Vector3(0f, 0f, -14.5f);
        public static readonly Vector3 ExtractionPosition = new Vector3(0f, 0f, 16f);

        // Hierarchy의 구역 이름과 바닥 표시 중심입니다. 레벨 구조를 찾거나 PlayMode 테스트로 연결 상태를 검사할 때 사용합니다.
        public static readonly Vector3 SouthCampCenter = new Vector3(0f, 0f, -13f);
        public static readonly Vector3 WestResidentialCenter = new Vector3(-11.5f, 0f, -1f);
        public static readonly Vector3 CentralCheckpointCenter = new Vector3(0f, 0f, 3f);
        public static readonly Vector3 EastWarehouseCenter = new Vector3(11.5f, 0f, 1f);
        public static readonly Vector3 NorthExtractionCenter = new Vector3(0f, 0f, 14.5f);

        // 시작 지점 바로 옆에서 좀비가 생성되지 않도록 남쪽 중앙은 비우고 외곽의 좌우에 스폰 지점을 배치합니다.
        // 단순 추적 AI가 벽 안쪽에 갇히지 않도록 모든 위치는 외곽 벽보다 안쪽의 열린 공간을 사용합니다.
        private static readonly Vector3[] ZombieSpawnPositions =
        {
            new Vector3(-15f, 0f, -15f),
            new Vector3(15f, 0f, -15f),
            new Vector3(-15f, 0f, -5f),
            new Vector3(15f, 0f, -5f),
            new Vector3(-15f, 0f, 6f),
            new Vector3(15f, 0f, 6f),
            new Vector3(-15f, 0f, 15f),
            new Vector3(15f, 0f, 15f)
        };

        public static int ZombieSpawnPointCount => ZombieSpawnPositions.Length;

        // 배열 자체를 외부에 공개하지 않고 인덱스로 복사된 Vector3 값만 돌려줘 맵 기준 데이터가 실수로 변경되지 않게 합니다.
        public static Vector3 GetZombieSpawnPosition(int index)
        {
            return ZombieSpawnPositions[index];
        }

        // 지정 위치가 외곽 벽 안쪽인지 검사합니다. margin은 벽이나 다른 오브젝트와 떨어져야 하는 추가 여유 거리입니다.
        public static bool IsInsidePlayableArea(Vector3 position, float margin = 0f)
        {
            float safeMargin = Mathf.Clamp(margin, 0f, MapHalfExtent);
            float limit = MapHalfExtent - safeMargin;
            return Mathf.Abs(position.x) <= limit && Mathf.Abs(position.z) <= limit;
        }

        // 플레이어 위치를 다섯 개의 큰 구역 이름으로 바꿔 HUD에 현재 위치를 표시합니다.
        // 남·북 구역을 먼저 판정하고 가운데 세로 범위에서만 X좌표로 서쪽·동쪽을 나누므로 모서리의 이름이 자주 바뀌지 않습니다.
        public static string GetZoneDisplayName(Vector3 position)
        {
            if (!IsInsidePlayableArea(position)) return "맵 외부";
            if (position.z <= -8f) return "남부 캠프";
            if (position.z >= 10f) return "북부 탈출로";
            if (position.x <= -6f) return "서부 주거지";
            if (position.x >= 6f) return "동부 창고";
            return "중앙 검문소";
        }
    }
}
