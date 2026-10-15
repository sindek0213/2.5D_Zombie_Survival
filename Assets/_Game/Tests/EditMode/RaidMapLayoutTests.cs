using NUnit.Framework;
using UnityEngine;
using ZombieSurvival.Core;

public sealed class RaidMapLayoutTests
{
    // 대표 좌표가 각 구역 이름으로 안정적으로 바뀌어 HUD가 플레이어에게 잘못된 위치를 안내하지 않는지 검사합니다.
    [TestCase(0f, -14f, "남부 캠프")]
    [TestCase(-11f, 0f, "서부 주거지")]
    [TestCase(0f, 3f, "중앙 검문소")]
    [TestCase(11f, 0f, "동부 창고")]
    [TestCase(0f, 15f, "북부 탈출로")]
    [TestCase(30f, 0f, "맵 외부")]
    public void ZoneDisplayName_UsesStableMapRegions(float x, float z, string expected)
    {
        Assert.That(RaidMapLayout.GetZoneDisplayName(new Vector3(x, 0f, z)), Is.EqualTo(expected));
    }

    // 플레이어 시작점과 탈출 지점이 모두 외곽 벽 안에 있고 서로 충분히 멀어 실제 파밍 동선이 생기는지 검사합니다.
    [Test]
    public void StartAndExtraction_AreInsideMapAndFarApart()
    {
        Assert.That(RaidMapLayout.IsInsidePlayableArea(RaidMapLayout.PlayerStartPosition, 1f), Is.True);
        Assert.That(RaidMapLayout.IsInsidePlayableArea(RaidMapLayout.ExtractionPosition, 1f), Is.True);
        Assert.That(
            Vector3.Distance(RaidMapLayout.PlayerStartPosition, RaidMapLayout.ExtractionPosition),
            Is.GreaterThanOrEqualTo(28f));
    }

    // 모든 좀비 스폰 지점은 외곽 벽 안쪽이어야 하며 레이드 시작과 동시에 플레이어 바로 옆에 생성되어서는 안 됩니다.
    [Test]
    public void ZombieSpawnPoints_AreInsideMapAndAwayFromPlayerStart()
    {
        Assert.That(RaidMapLayout.ZombieSpawnPointCount, Is.GreaterThanOrEqualTo(8));

        for (int i = 0; i < RaidMapLayout.ZombieSpawnPointCount; i++)
        {
            Vector3 spawnPosition = RaidMapLayout.GetZombieSpawnPosition(i);
            Assert.That(RaidMapLayout.IsInsidePlayableArea(spawnPosition, 2f), Is.True, $"스폰 지점 {i + 1}이 외곽 벽과 너무 가깝습니다.");
            Assert.That(
                Vector3.Distance(spawnPosition, RaidMapLayout.PlayerStartPosition),
                Is.GreaterThanOrEqualTo(12f),
                $"스폰 지점 {i + 1}이 플레이어 시작점과 너무 가깝습니다.");
        }
    }

    // 다섯 구역의 중심이 모두 맵 내부에 있어 바닥 표시나 구역 오브젝트가 외곽 벽 밖으로 잘못 이동하지 않았는지 검사합니다.
    [Test]
    public void ZoneCenters_AreInsidePlayableArea()
    {
        Vector3[] zoneCenters =
        {
            RaidMapLayout.SouthCampCenter,
            RaidMapLayout.WestResidentialCenter,
            RaidMapLayout.CentralCheckpointCenter,
            RaidMapLayout.EastWarehouseCenter,
            RaidMapLayout.NorthExtractionCenter
        };

        for (int i = 0; i < zoneCenters.Length; i++)
            Assert.That(RaidMapLayout.IsInsidePlayableArea(zoneCenters[i], 1f), Is.True, $"구역 중심 {i + 1}이 맵 밖에 있습니다.");
    }

    // 학교는 기능 검증용 RaidPrototype과 크기가 다르므로 학교 전용 시작점과 탈출점이 84×84 담장 안에 있는지 별도로 검사합니다.
    [Test]
    public void SchoolStartAndExtraction_AreInsideLargeCampusAndFarApart()
    {
        Assert.That(SchoolRaidMapLayout.MapHalfExtent, Is.EqualTo(42f));
        Assert.That(SchoolRaidMapLayout.IsInsidePlayableArea(SchoolRaidMapLayout.PlayerStartPosition, 1f), Is.True);
        Assert.That(SchoolRaidMapLayout.IsInsidePlayableArea(SchoolRaidMapLayout.ExtractionPosition, 1f), Is.True);
        Assert.That(
            Vector3.Distance(SchoolRaidMapLayout.PlayerStartPosition, SchoolRaidMapLayout.ExtractionPosition),
            Is.GreaterThanOrEqualTo(70f));
    }

    // 넓어진 학교의 모든 스폰 지점이 담장 안에 있고 정문 시작 위치와 운동장 탈출구 바로 옆에는 생성되지 않는지 검사합니다.
    // 위치 배열과 위험도·해금 시간 배열의 길이가 달라지면 아래 접근 자체가 실패하므로 맵을 다시 만들 때 데이터 순서 누락도 함께 잡습니다.
    [Test]
    public void SchoolZombieSpawnPoints_AreInsideCampusAndAwayFromPlayerStart()
    {
        Assert.That(SchoolRaidMapLayout.ZombieSpawnPointCount, Is.GreaterThanOrEqualTo(12));

        for (int i = 0; i < SchoolRaidMapLayout.ZombieSpawnPointCount; i++)
        {
            Vector3 spawnPosition = SchoolRaidMapLayout.GetZombieSpawnPosition(i);
            int dangerRating = SchoolRaidMapLayout.GetZombieSpawnDangerRating(i);
            float unlockTime = SchoolRaidMapLayout.GetZombieSpawnUnlockTime(i);
            Assert.That(SchoolRaidMapLayout.IsInsidePlayableArea(spawnPosition, 2f), Is.True, $"학교 스폰 지점 {i + 1}이 외곽 담장과 너무 가깝습니다.");
            Assert.That(
                Vector3.Distance(spawnPosition, SchoolRaidMapLayout.PlayerStartPosition),
                Is.GreaterThanOrEqualTo(12f),
                $"학교 스폰 지점 {i + 1}이 정문 시작점과 너무 가깝습니다.");
            Assert.That(
                Vector3.Distance(spawnPosition, SchoolRaidMapLayout.ExtractionPosition),
                Is.GreaterThanOrEqualTo(12f),
                $"학교 스폰 지점 {i + 1}이 운동장 탈출구를 바로 막을 수 있습니다.");
            Assert.That(dangerRating, Is.InRange(1, 5), $"학교 스폰 지점 {i + 1}의 위험도가 1~5 범위를 벗어났습니다.");
            Assert.That(unlockTime, Is.GreaterThanOrEqualTo(0f), $"학교 스폰 지점 {i + 1}의 해금 시간이 음수입니다.");

            // 고위험 실내는 시작 직후 열리지 않아야 하고, 저위험 외곽은 즉시 사용할 수 있어야 초반 난이도가 갑자기 튀지 않습니다.
            if (dangerRating >= 4) Assert.That(unlockTime, Is.GreaterThanOrEqualTo(60f));
            if (dangerRating == 1) Assert.That(unlockTime, Is.Zero);
        }
    }

    // 대표 좌표가 정문, 행정동, 교실동, 중앙정원, 특별실동, 급식실, 체육관과 운동장 이름으로 구분되는지 검사합니다.
    [TestCase(0f, -35f, "학교 정문·주차장")]
    [TestCase(-28f, -21f, "행정동·보건실")]
    [TestCase(-27f, 2f, "일반 교실동 1층")]
    [TestCase(0f, 1f, "중앙정원·연결 복도")]
    [TestCase(-10f, 23f, "특별실·도서관동")]
    [TestCase(24f, -21f, "급식실·조리실")]
    [TestCase(27f, 4f, "체육관")]
    [TestCase(0f, 34f, "운동장·비상문")]
    public void SchoolZoneDisplayName_UsesLargeCampusRegions(float x, float z, string expected)
    {
        Assert.That(SchoolRaidMapLayout.GetZoneDisplayName(new Vector3(x, 0f, z)), Is.EqualTo(expected));
    }

    // 2층 테스트 교실동의 중심은 발자국 안에 있고 바로 바깥 좌표는 제외되어 맵 생성용 경계가 뒤집히지 않았는지 확인합니다.
    [Test]
    public void SchoolTwoStoreyClassroom_UsesValidFootprintAndFloorHeights()
    {
        Assert.That(SchoolRaidMapLayout.IsInsideTwoStoreyClassroomFootprint(new Vector3(-27f, 0f, 2f)), Is.True);
        Assert.That(SchoolRaidMapLayout.IsInsideTwoStoreyClassroomFootprint(new Vector3(-15.9f, 0f, 2f)), Is.False);
        Assert.That(SchoolRaidMapLayout.ClassroomSecondFloorHeight, Is.GreaterThan(2.5f));
        Assert.That(SchoolRaidMapLayout.ClassroomRoofHeight, Is.GreaterThan(SchoolRaidMapLayout.ClassroomSecondFloorHeight + 2.5f));
    }
}
