using NUnit.Framework;
using UnityEngine;
using ZombieSurvival.Items;
using ZombieSurvival.UI;

public sealed class ItemRarityTests
{
    // 네 희귀도 enum이 인벤토리 상세 화면에서 비어 있지 않은 한글 이름으로 변환되는지 검사합니다.
    [TestCase(ItemRarity.Common, "일반")]
    [TestCase(ItemRarity.Uncommon, "고급")]
    [TestCase(ItemRarity.Rare, "희귀")]
    [TestCase(ItemRarity.Quest, "퀘스트")]
    public void RarityName_ReturnsReadableKoreanLabel(ItemRarity rarity, string expected)
    {
        Assert.That(ItemDetailFormatter.RarityName(rarity), Is.EqualTo(expected));
    }

    // 일반 등급은 UI 프리팹의 사용자 지정 색을 그대로 사용하고, 희귀·퀘스트 등급은 서로 다른 강조색을 사용해야 합니다.
    [Test]
    public void RarityColor_PreservesCommonColorAndSeparatesHigherTiers()
    {
        Color common = new Color(0.1f, 0.2f, 0.3f, 1f);
        Color commonResult = InventoryItemView.GetRarityColor(ItemRarity.Common, common);
        Color rare = InventoryItemView.GetRarityColor(ItemRarity.Rare, common);
        Color quest = InventoryItemView.GetRarityColor(ItemRarity.Quest, common);

        Assert.That(commonResult, Is.EqualTo(common));
        Assert.That(rare, Is.Not.EqualTo(common));
        Assert.That(quest, Is.Not.EqualTo(rare));
    }
}
