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
}
