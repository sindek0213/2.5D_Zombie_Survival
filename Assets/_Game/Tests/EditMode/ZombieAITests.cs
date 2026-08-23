using NUnit.Framework;
using ZombieSurvival.AI;

public sealed class ZombieAITests
{
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
}
