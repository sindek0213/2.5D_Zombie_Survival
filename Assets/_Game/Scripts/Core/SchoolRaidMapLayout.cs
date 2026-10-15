using UnityEngine;

namespace ZombieSurvival.Core
{
    // 실제 학교 레이드는 작은 기능 테스트 맵과 크기·구역·스폰 위치가 다르므로 전용 배치 기준을 따로 보관합니다.
    // RaidPrototype의 좌표를 키우지 않고 이 클래스를 분리하면 테스트 맵은 그대로 유지하면서 학교만 84×84 캠퍼스로 확장할 수 있습니다.
    public static class SchoolRaidMapLayout
    {
        // 중심에서 외곽 담장까지 42m이므로 전체 학교 부지는 84×84 크기입니다.
        // 기존 36×36 RaidPrototype보다 가로·세로가 약 2.3배이고 실제 이동 가능 면적은 약 5.4배입니다.
        public const float MapHalfExtent = 42f;

        // 남쪽 정문 안쪽에서 시작해 북쪽 운동장 비상문으로 탈출합니다.
        // 직선으로만 가도 약 76m이므로 교실과 부속 시설을 수색하는 동안 충분한 이동 거리가 생깁니다.
        public static readonly Vector3 PlayerStartPosition = new Vector3(0f, 0f, -37f);
        public static readonly Vector3 ExtractionPosition = new Vector3(0f, 0f, 39f);

        // 각 큰 구역의 대표 중심입니다. 맵 생성기, 좀비 스폰 방향과 자동 테스트가 같은 기준을 사용합니다.
        public static readonly Vector3 EntranceCenter = new Vector3(0f, 0f, -35f);
        public static readonly Vector3 AdministrationCenter = new Vector3(-22f, 0f, -21f);
        public static readonly Vector3 CafeteriaCenter = new Vector3(24f, 0f, -21f);
        public static readonly Vector3 ClassroomCenter = new Vector3(-26f, 0f, 2f);
        public static readonly Vector3 CourtyardCenter = new Vector3(0f, 0f, 1f);
        public static readonly Vector3 SpecialClassCenter = new Vector3(-9f, 0f, 22f);
        public static readonly Vector3 GymCenter = new Vector3(27f, 0f, 4f);
        public static readonly Vector3 PlaygroundCenter = new Vector3(0f, 0f, 34f);

        // 임무 저장에는 화면에 표시되는 한글 이름 대신 바뀌지 않는 짧은 ID를 기록합니다.
        // 나중에 구역 표시 문구를 수정해도 이미 받은 방문 임무와 레이드 결과가 서로 어긋나지 않습니다.
        public const string EntranceZoneId = "school_entrance";
        public const string AdministrationZoneId = "school_administration";
        public const string CafeteriaZoneId = "school_cafeteria";
        public const string ClassroomZoneId = "school_classroom";
        public const string ClassroomSecondFloorZoneId = "school_classroom_2f";
        public const string CourtyardZoneId = "school_courtyard";
        public const string SpecialClassZoneId = "school_special_class";
        public const string GymZoneId = "school_gym";
        public const string PlaygroundZoneId = "school_playground";

        // 서쪽 일반 교실동 한 채만 먼저 2층 테스트 건물로 사용합니다.
        // 아래 경계값을 맵 생성기와 테스트가 함께 사용하므로 건물 크기를 바꿀 때 계단·바닥·구역 판정이 서로 어긋나는 일을 줄일 수 있습니다.
        public const float TwoStoreyClassroomMinX = -38f;
        public const float TwoStoreyClassroomMaxX = -16f;
        public const float TwoStoreyClassroomMinZ = -14f;
        public const float TwoStoreyClassroomMaxZ = 18f;
        // 1층 바닥에서 3.1m 올라간 곳이 2층 보행면이고, 2층 벽 위 6.1m 지점에 옥상 바닥이 놓입니다.
        public const float ClassroomSecondFloorHeight = 3.1f;
        public const float ClassroomRoofHeight = 6.1f;

        // 넓어진 외곽, 건물 안쪽과 2층을 합쳐 총 17개 스폰 지점을 둡니다.
        // 정문 시작점과 운동장 탈출점 바로 앞은 비워 화면 안에서 갑자기 생성되거나 탈출 원통을 막는 상황을 줄입니다.
        private static readonly Vector3[] ZombieSpawnPositions =
        {
            new Vector3(-37f, 0f, -36f),
            new Vector3(37f, 0f, -36f),
            // 행정동과 급식동의 열린 방 안쪽을 사용해 실내 수색 중에도 적이 다시 채워질 수 있게 합니다.
            new Vector3(-35f, 0f, -19f),
            new Vector3(34f, 0f, -20f),
            // 교실과 중앙정원은 시작 지점에서 바로 보이지 않는 북쪽 방과 화단 뒤쪽을 사용합니다.
            new Vector3(-33f, 0f, 10f),
            new Vector3(-8f, 0f, 8f),
            // 특별실과 과학실은 희귀 전리품이 있는 중위험 실내 구역이라 후반 특수 좀비 후보가 됩니다.
            new Vector3(-31f, 0f, 24f),
            new Vector3(-11f, 0f, 24f),
            // 체육관은 넓은 대신 가장 위험한 실내 전투 구역으로 두 지점을 사용합니다.
            new Vector3(24f, 0f, 5f),
            new Vector3(28f, 0f, 14f),
            // 2층 교실은 계단을 통해 접근 가능하며 높은 가치의 상자 근처에 후반 적이 나타날 수 있습니다.
            new Vector3(-32f, ClassroomSecondFloorHeight, 12f),
            // 운동장 스폰은 관중석과 골대가 없는 북쪽 가장자리에 배치합니다.
            new Vector3(-35f, 0f, 39f),
            new Vector3(35f, 0f, 39f),
            new Vector3(-20f, 0f, 38f),
            new Vector3(20f, 0f, 38f),
            // 동서 외곽 통로는 건물 내부가 막혔을 때도 스포너가 사용할 수 있는 낮은 위험도의 예비 위치입니다.
            new Vector3(-40f, 0f, 0f),
            new Vector3(40f, 0f, 0f)
        };

        // 각 스폰 지점의 위험도는 그 지점에서 허용할 좀비 종류와 무작위 선택 가중치에 사용됩니다.
        // 1은 일반형 위주의 외곽, 4는 튼튼한 좀비와 비명 좀비가 등장할 수 있는 고가치 실내입니다.
        private static readonly int[] ZombieSpawnDangerRatings =
        {
            1, 1, 2, 2, 2, 2, 3, 3, 4, 4, 4, 3, 3, 3, 3, 1, 1
        };

        // 시작 직후 안전한 출입구에 특수 좀비가 몰리지 않도록 실내 고위험 지점은 일정 시간이 지난 뒤에만 활성화합니다.
        private static readonly float[] ZombieSpawnUnlockTimes =
        {
            0f, 0f, 15f, 15f, 20f, 20f, 45f, 55f, 75f, 85f, 90f, 45f, 45f, 60f, 60f, 0f, 0f
        };

        public static int ZombieSpawnPointCount => ZombieSpawnPositions.Length;

        // 배열을 직접 공개하지 않고 값만 반환해 다른 코드가 공용 배치 데이터를 실수로 바꾸지 못하게 합니다.
        public static Vector3 GetZombieSpawnPosition(int index) => ZombieSpawnPositions[index];

        // 스포너가 위치 배열과 같은 인덱스로 위험도·활성 시간을 읽게 해 세 데이터가 서로 다른 순서로 적용되지 않게 합니다.
        public static int GetZombieSpawnDangerRating(int index)
            => Mathf.Clamp(ZombieSpawnDangerRatings[index], 1, 5);

        public static float GetZombieSpawnUnlockTime(int index)
            => Mathf.Max(0f, ZombieSpawnUnlockTimes[index]);

        // 학교 전용 외곽 담장 안인지 검사합니다. margin은 스폰 테스트처럼 담장과 추가로 떨어져야 할 거리입니다.
        public static bool IsInsidePlayableArea(Vector3 position, float margin = 0f)
        {
            float safeMargin = Mathf.Clamp(margin, 0f, MapHalfExtent);
            float limit = MapHalfExtent - safeMargin;
            return Mathf.Abs(position.x) <= limit && Mathf.Abs(position.z) <= limit;
        }

        // Y 높이와 관계없이 교실동 건물의 가로·세로 바닥 범위 안인지 검사합니다.
        // 향후 층 표시 UI를 추가할 때 이 발자국 판정과 플레이어 Y값을 조합하면 같은 건물의 1층·2층을 구분할 수 있습니다.
        public static bool IsInsideTwoStoreyClassroomFootprint(Vector3 position)
        {
            return position.x >= TwoStoreyClassroomMinX &&
                   position.x <= TwoStoreyClassroomMaxX &&
                   position.z >= TwoStoreyClassroomMinZ &&
                   position.z <= TwoStoreyClassroomMaxZ;
        }

        // 넓은 학교를 실제 기능별 구역 이름으로 나눠 HUD에 현재 위치를 표시합니다.
        // 남북의 정문·운동장을 먼저 판정하고, 건물이 모인 중앙은 좌표와 높이를 함께 사용해 행정동·교실동·특별실·급식실·체육관을 구분합니다.
        public static string GetZoneDisplayName(Vector3 position)
        {
            switch (GetZoneId(position))
            {
                case EntranceZoneId: return "학교 정문·주차장";
                case AdministrationZoneId: return "행정동·보건실";
                case CafeteriaZoneId: return "급식실·조리실";
                case ClassroomSecondFloorZoneId: return "일반 교실동 2층";
                case ClassroomZoneId: return "일반 교실동 1층";
                case SpecialClassZoneId: return "특별실·도서관동";
                case GymZoneId: return "체육관";
                case PlaygroundZoneId: return "운동장·비상문";
                case CourtyardZoneId: return "중앙정원·연결 복도";
                default: return "맵 외부";
            }
        }

        // 월드 좌표를 임무와 결과 저장에 사용할 안정적인 구역 ID로 변환합니다.
        // 2층은 같은 XZ 발자국이어도 Y값을 먼저 확인해 1층 방문과 별개의 탐색 기록으로 남깁니다.
        public static string GetZoneId(Vector3 position)
        {
            if (!IsInsidePlayableArea(position)) return string.Empty;
            if (position.z <= -29f) return EntranceZoneId;
            if (position.z >= 28f) return PlaygroundZoneId;
            if (IsInsideTwoStoreyClassroomFootprint(position) &&
                position.y >= ClassroomSecondFloorHeight - 0.65f)
                return ClassroomSecondFloorZoneId;
            if (position.z >= 16f && position.x < 18f) return SpecialClassZoneId;
            if (position.x <= -16f && position.z > -15f) return ClassroomZoneId;
            if (position.x <= -9f && position.z <= -15f) return AdministrationZoneId;
            if (position.x >= 14f && position.z <= -12f) return CafeteriaZoneId;
            if (position.x >= 16f) return GymZoneId;
            return CourtyardZoneId;
        }

        // HUD와 스폰 밸런스가 같은 위험 기준을 공유하도록 구역별 1~5 등급을 한곳에서 반환합니다.
        // 고급 상자가 있는 2층과 체육관은 4, 탈출을 앞둔 넓은 운동장은 3, 정문은 1로 설정합니다.
        public static int GetZoneDangerRating(Vector3 position)
        {
            switch (GetZoneId(position))
            {
                case EntranceZoneId: return 1;
                case AdministrationZoneId:
                case CafeteriaZoneId:
                case ClassroomZoneId:
                case CourtyardZoneId: return 2;
                case SpecialClassZoneId:
                case PlaygroundZoneId: return 3;
                case ClassroomSecondFloorZoneId:
                case GymZoneId: return 4;
                default: return 0;
            }
        }
    }
}
