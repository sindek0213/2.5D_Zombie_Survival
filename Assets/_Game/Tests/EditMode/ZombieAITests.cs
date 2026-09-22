using NUnit.Framework;
using UnityEngine;
using ZombieSurvival.AI;

public sealed class ZombieAITests
{
    // 아직 목적지가 없다면 시간 제한이나 이동 거리에 관계없이 첫 NavMesh 경로를 즉시 요청해야 합니다.
    // 목적지가 있다면 갱신 간격이 지났고 플레이어가 임계 거리 이상 움직인 경우에만 다시 요청합니다.
    [TestCase(false, 0f, 0f, 0f, 0.35f, true)]
    [TestCase(false, 0.1f, 0.2f, 0f, 0.35f, false)]
    [TestCase(true, 0.1f, 0.2f, 1f, 0.35f, false)]
    [TestCase(true, 0.3f, 0.2f, 0.2f, 0.35f, false)]
    [TestCase(true, 0.3f, 0.2f, 0.4f, 0.35f, true)]
    public void DestinationRefresh_UsesTimeAndMovementThreshold(
        bool hasDestination,
        float currentTime,
        float nextAllowedTime,
        float targetMoveDistance,
        float moveThreshold,
        bool expected)
    {
        Assert.That(
            ZombieMovement.ShouldRefreshDestination(
                hasDestination,
                currentTime,
                nextAllowedTime,
                targetMoveDistance,
                moveThreshold),
            Is.EqualTo(expected));
    }

    // 좀비마다 다른 회피 우선순위를 사용하되 Unity가 허용하는 값 가운데 25~74의 안전한 범위에 들어오는지 검사합니다.
    [TestCase(0, 25)]
    [TestCase(49, 74)]
    [TestCase(-1, 26)]
    public void AvoidancePriority_UsesStableMiddleRange(int instanceId, int expected)
    {
        Assert.That(ZombieMovement.CalculateAvoidancePriority(instanceId), Is.EqualTo(expected));
    }

    // 감지 범위 밖의 좀비가 Idle 상태를 유지하는지 검증합니다.
    [Test]
    public void OutsideDetectionRange_RemainsIdle()
    {
        ZombieBrain.ZombieState state = ZombieBrain.EvaluateState(
            ZombieBrain.ZombieState.Idle, 12f, 9f, 1.35f, 13f);
        Assert.That(state, Is.EqualTo(ZombieBrain.ZombieState.Idle));
    }

    // 감지 범위와 공격 범위에 따른 상태 전환을 검증합니다.
    [TestCase(8f, ZombieBrain.ZombieState.Chase)]
    [TestCase(1f, ZombieBrain.ZombieState.Attack)]
    public void Distance_SelectsExpectedState(float distance, ZombieBrain.ZombieState expected)
    {
        ZombieBrain.ZombieState state = ZombieBrain.EvaluateState(
            ZombieBrain.ZombieState.Idle, distance, 9f, 1.35f, 13f);
        Assert.That(state, Is.EqualTo(expected));
    }

    // 한번 추적한 대상은 감지 범위를 조금 벗어나도 추적을 유지하는지 검증합니다.
    [Test]
    public void Chase_UsesLargerLoseTargetRange()
    {
        ZombieBrain.ZombieState state = ZombieBrain.EvaluateState(
            ZombieBrain.ZombieState.Chase, 11f, 9f, 1.35f, 13f);
        Assert.That(state, Is.EqualTo(ZombieBrain.ZombieState.Chase));
    }

    // 150초 동안 난이도가 0에서 1까지 증가하고 그 이후에는 1을 넘지 않는지 검사합니다.
    [TestCase(0f, 150f, 0f)]
    [TestCase(75f, 150f, 0.5f)]
    [TestCase(200f, 150f, 1f)]
    public void SpawnDifficultyProgress_StaysInsideZeroToOne(
        float elapsed,
        float rampSeconds,
        float expected)
    {
        Assert.That(
            ZombieSpawner.CalculateDifficultyProgress(elapsed, rampSeconds),
            Is.EqualTo(expected).Within(0.001f));
    }

    // 난이도가 절반일 때 최대 생존 수와 생성 간격도 시작·종료 설정의 중간값이 되는지 검사합니다.
    [Test]
    public void SpawnDifficulty_InterpolatesAliveLimitAndInterval()
    {
        Assert.That(ZombieSpawner.CalculateAliveLimit(3, 8, 0.5f), Is.EqualTo(6));
        Assert.That(ZombieSpawner.CalculateSpawnInterval(8f, 3f, 0.5f), Is.EqualTo(5.5f).Within(0.001f));
    }

    // 빠른 좀비의 해금 시간 전에는 난수 값이 커도 일반 좀비만 선택되는지 검사합니다.
    [Test]
    public void WeightedSpawnSelection_ExcludesLockedArchetypes()
    {
        ZombieArchetype walker = CreateArchetype("일반 좀비", 1f, 0f);
        ZombieArchetype runner = CreateArchetype("빠른 좀비", 3f, 45f);
        try
        {
            int selected = ZombieSpawner.SelectWeightedArchetypeIndex(
                new[] { walker, runner },
                44.99f,
                0.99f);

            Assert.That(selected, Is.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(walker);
            Object.DestroyImmediate(runner);
        }
    }

    // 두 종류가 모두 해금되면 1:3 가중치에서 상위 25%는 일반형, 나머지는 빠른형으로 나뉘는지 검사합니다.
    [TestCase(0f, 0)]
    [TestCase(0.249f, 0)]
    [TestCase(0.25f, 1)]
    [TestCase(1f, 1)]
    public void WeightedSpawnSelection_UsesConfiguredWeights(float roll, int expectedIndex)
    {
        ZombieArchetype walker = CreateArchetype("일반 좀비", 1f, 0f);
        ZombieArchetype runner = CreateArchetype("빠른 좀비", 3f, 45f);
        try
        {
            int selected = ZombieSpawner.SelectWeightedArchetypeIndex(
                new[] { walker, runner },
                60f,
                roll);

            Assert.That(selected, Is.EqualTo(expectedIndex));
        }
        finally
        {
            Object.DestroyImmediate(walker);
            Object.DestroyImmediate(runner);
        }
    }

    // 테스트는 에셋 파일을 만들지 않고 메모리에 임시 종류 데이터를 생성해 선택 계산만 독립적으로 확인합니다.
    private static ZombieArchetype CreateArchetype(string displayName, float weight, float unlockTime)
    {
        ZombieArchetype archetype = ScriptableObject.CreateInstance<ZombieArchetype>();
        archetype.Configure(displayName, null, weight, unlockTime, 100f, 2f, 10f, Color.white);
        return archetype;
    }
}
