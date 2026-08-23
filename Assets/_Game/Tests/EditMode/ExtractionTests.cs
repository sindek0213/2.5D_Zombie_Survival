using NUnit.Framework;
using ZombieSurvival.Extraction;

public sealed class ExtractionTests
{
    // 요구 수량과 정확히 같거나 더 많이 보유할 때만 탈출 조건이 충족되는지 확인합니다.
    [TestCase(0, 1, false)]
    [TestCase(1, 1, true)]
    [TestCase(2, 1, true)]
    public void RequiredQuantity_IsEvaluatedAtBoundary(int owned, int required, bool expected)
    {
        Assert.That(ExtractionController.HasRequiredQuantity(owned, required), Is.EqualTo(expected));
    }

    // 유효한 상태에서는 시간이 누적되고 구역 이탈처럼 valid가 false가 되면 즉시 0으로 취소되는지 검증합니다.
    [Test]
    public void Countdown_AdvancesAndCancels()
    {
        float progress = ExtractionController.AdvanceProgress(0f, 1.25f, 3f, true);
        Assert.That(progress, Is.EqualTo(1.25f));
        Assert.That(ExtractionController.AdvanceProgress(progress, 0.1f, 3f, false), Is.Zero);
    }
}
