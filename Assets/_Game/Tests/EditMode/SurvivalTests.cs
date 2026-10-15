using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using ZombieSurvival.UI;
using Object = UnityEngine.Object;

public sealed class SurvivalTests
{
    // 달리는 동안에는 소모되고 쉬는 동안에는 회복되며, 두 방향 모두 0과 최대치 경계를 넘지 않아야 합니다.
    [TestCase(100f, true, 22f, 18f, 1f, 78f)]
    [TestCase(10f, true, 22f, 18f, 1f, 0f)]
    [TestCase(90f, false, 22f, 18f, 1f, 100f)]
    [TestCase(20f, false, 22f, 18f, 0.5f, 29f)]
    public void StaminaTick_DrainsRecoversAndClamps(
        float current,
        bool sprinting,
        float drain,
        float recovery,
        float deltaTime,
        float expected)
    {
        Assert.That(
            PlayerSurvival.CalculateStaminaAfterTick(current, 100f, sprinting, drain, recovery, deltaTime),
            Is.EqualTo(expected).Within(0.001f));
    }

    // 현재값이 아니라 최대치의 25%를 지불해야 합니다. 정확히 25%가 있으면 가능하고 그보다 부족하면 비용도 빠지지 않습니다.
    [TestCase(100f, 100f, true, 75f)]
    [TestCase(200f, 200f, true, 150f)]
    [TestCase(200f, 50f, true, 0f)]
    [TestCase(200f, 40f, false, 40f)]
    public void SpendStamina_ForRollUsesMaximumAndIsAtomic(float maximum, float starting, bool expectedSuccess, float expectedRemaining)
    {
        var player = new GameObject("RollStaminaTestPlayer");
        player.SetActive(false);
        try
        {
            PlayerSurvival survival = player.AddComponent<PlayerSurvival>();
            var data = new SerializedObject(survival);
            data.FindProperty("maximumStamina").floatValue = maximum;
            data.FindProperty("startingStamina").floatValue = starting;
            data.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(survival);

            Assert.That(survival.TrySpendStaminaFraction(0.25f), Is.EqualTo(expectedSuccess));
            Assert.That(survival.CurrentStamina, Is.EqualTo(expectedRemaining).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    // HUD는 평상시에는 간단한 기력 수치만 보여 주고 실제 달리기 중에만 상태 안내를 추가합니다.
    [Test]
    public void StaminaHud_ShowsSprintStateOnlyWhileRunning()
    {
        Assert.That(AmmoHud.BuildStaminaText(80f, 100f, false), Is.EqualTo("기력 80/100"));
        Assert.That(AmmoHud.BuildStaminaText(80f, 100f, true), Does.Contain("달리기"));
    }

    // 정상 구간, 임계치 중간과 완전 고갈 상태에서 이동 속도 배율이 의도한 선형 값인지 검사합니다.
    [TestCase(25f, 25f, 0.7f, 1f)]
    [TestCase(12.5f, 25f, 0.7f, 0.85f)]
    [TestCase(0f, 25f, 0.7f, 0.7f)]
    [TestCase(50f, 0f, 0.7f, 1f)]
    public void MovementMultiplier_UsesLowSatietyPenalty(
        float current,
        float threshold,
        float minimum,
        float expected)
    {
        Assert.That(
            PlayerSurvival.CalculateMovementSpeedMultiplier(current, threshold, minimum),
            Is.EqualTo(expected).Within(0.001f));
    }

    // 통조림은 ItemDefinition에 저장된 50만큼 포만감을 회복하고 실제 효과가 있을 때만 수량을 한 개 줄여야 합니다.
    [Test]
    public void FoodUse_RestoresConfiguredSatietyAndConsumesOneItem()
    {
        var player = new GameObject("FoodUseTestPlayer");
        player.SetActive(false);
        ItemDefinition food = CreateConsumable("food_test", ItemType.Food, 2, 0f, 50f);
        try
        {
            PlayerSurvival survival = player.AddComponent<PlayerSurvival>();
            var survivalData = new SerializedObject(survival);
            survivalData.FindProperty("maximumSatiety").floatValue = 100f;
            survivalData.FindProperty("startingSatiety").floatValue = 40f;
            survivalData.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(survival);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(food, 2), Is.True);
            InventoryEntry entry = inventory.Entries[0];

            Assert.That(inventory.TryUse(inventory.Root, entry), Is.True);
            Assert.That(survival.CurrentSatiety, Is.EqualTo(90f));
            Assert.That(entry.Quantity, Is.EqualTo(1));

            // 두 번째 사용은 남은 10만 회복해도 실제 변화가 있으므로 성공하고 최대 100을 넘지 않습니다.
            Assert.That(inventory.TryUse(inventory.Root, entry), Is.True);
            Assert.That(survival.CurrentSatiety, Is.EqualTo(100f));
            Assert.That(inventory.Entries, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(food);
            Object.DestroyImmediate(player);
        }
    }

    // 포만감이 이미 가득 찼다면 음식 효과가 없으므로 사용 요청을 거부하고 아이템 수량을 보존해야 합니다.
    [Test]
    public void FoodUse_AtMaximumSatietyDoesNotConsumeItem()
    {
        var player = new GameObject("FullSatietyFoodTestPlayer");
        player.SetActive(false);
        ItemDefinition food = CreateConsumable("full_food_test", ItemType.Food, 1, 0f, 50f);
        try
        {
            PlayerSurvival survival = player.AddComponent<PlayerSurvival>();
            var survivalData = new SerializedObject(survival);
            survivalData.FindProperty("maximumSatiety").floatValue = 100f;
            survivalData.FindProperty("startingSatiety").floatValue = 100f;
            survivalData.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(survival);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(food, 1), Is.True);
            InventoryEntry entry = inventory.Entries[0];

            Assert.That(inventory.TryUse(inventory.Root, entry), Is.False);
            Assert.That(entry.Quantity, Is.EqualTo(1));
            Assert.That(inventory.Entries.Count, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(food);
            Object.DestroyImmediate(player);
        }
    }

    // 사용 실패가 단순 false로 끝나지 않고 UI가 그대로 보여 줄 수 있는 구체적인 이유를 제공하는지 검사합니다.
    [Test]
    public void ConsumableFailureReason_ExplainsFullSatiety()
    {
        var player = new GameObject("ConsumableFailureReasonTestPlayer");
        player.SetActive(false);
        ItemDefinition food = CreateConsumable("failure_reason_food", ItemType.Food, 1, 0f, 50f);
        try
        {
            PlayerSurvival survival = player.AddComponent<PlayerSurvival>();
            var survivalData = new SerializedObject(survival);
            survivalData.FindProperty("maximumSatiety").floatValue = 100f;
            survivalData.FindProperty("startingSatiety").floatValue = 100f;
            survivalData.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(survival);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(food, 1), Is.True);

            Assert.That(
                inventory.GetUseFailureReason(inventory.Root, inventory.Entries[0]),
                Is.EqualTo("포만감이 이미 충분합니다."));
        }
        finally
        {
            Object.DestroyImmediate(food);
            Object.DestroyImmediate(player);
        }
    }

    // 포만감이 시간 경과로 100에서 아주 조금 줄어도 HUD에는 올림되어 100으로 표시됩니다.
    // 이때 통조림을 사용하면 눈에 보이지 않는 소량 회복 때문에 아이템 한 개가 사라져서는 안 됩니다.
    [Test]
    public void FoodUse_WithLessThanOnePointMissingDoesNotConsumeItem()
    {
        var player = new GameObject("NearlyFullSatietyFoodTestPlayer");
        player.SetActive(false);
        ItemDefinition food = CreateConsumable("nearly_full_food_test", ItemType.Food, 1, 0f, 50f);
        try
        {
            PlayerSurvival survival = player.AddComponent<PlayerSurvival>();
            var survivalData = new SerializedObject(survival);
            survivalData.FindProperty("maximumSatiety").floatValue = 100f;
            survivalData.FindProperty("startingSatiety").floatValue = 100f;
            survivalData.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(survival);
            survival.ConsumeSatiety(0.25f);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(food, 1), Is.True);
            InventoryEntry entry = inventory.Entries[0];

            Assert.That(inventory.TryUse(inventory.Root, entry), Is.False);
            Assert.That(survival.CurrentSatiety, Is.EqualTo(99.75f).Within(0.001f));
            Assert.That(entry.Quantity, Is.EqualTo(1));
            Assert.That(inventory.Entries.Count, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(food);
            Object.DestroyImmediate(player);
        }
    }

    // 구급상자도 이전의 고정 35가 아니라 아이템 에셋에 기록된 체력 회복량을 사용해야 합니다.
    [Test]
    public void HealingUse_UsesItemDefinitionAmount()
    {
        var player = new GameObject("ConfiguredHealingTestPlayer");
        player.SetActive(false);
        ItemDefinition medkit = CreateConsumable("medkit_test", ItemType.Healing, 1, 20f, 0f);
        try
        {
            Health health = player.AddComponent<Health>();
            InvokeAwake(health);
            health.TakeDamage(60f, Vector3.zero);
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(medkit, 1), Is.True);

            Assert.That(inventory.TryUse(inventory.Root, inventory.Entries[0]), Is.True);
            Assert.That(health.CurrentHealth, Is.EqualTo(60f));
            Assert.That(inventory.Entries, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(medkit);
            Object.DestroyImmediate(player);
        }
    }

    // 체력도 HUD에서 100으로 보일 정도로 아주 조금만 비어 있다면 구급상자를 소비하지 않아야 합니다.
    // 포만감과 의료품이 같은 사용 규칙을 따르는지 함께 검사해 한쪽에서만 문제가 다시 생기는 것을 막습니다.
    [Test]
    public void HealingUse_WithLessThanOnePointMissingDoesNotConsumeItem()
    {
        var player = new GameObject("NearlyFullHealthItemTestPlayer");
        player.SetActive(false);
        ItemDefinition medkit = CreateConsumable("nearly_full_medkit_test", ItemType.Healing, 1, 35f, 0f);
        try
        {
            Health health = player.AddComponent<Health>();
            InvokeAwake(health);
            health.TakeDamage(0.25f, Vector3.zero);

            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(medkit, 1), Is.True);
            InventoryEntry entry = inventory.Entries[0];

            Assert.That(inventory.TryUse(inventory.Root, entry), Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(99.75f).Within(0.001f));
            Assert.That(entry.Quantity, Is.EqualTo(1));
            Assert.That(inventory.Entries.Count, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(medkit);
            Object.DestroyImmediate(player);
        }
    }

    // 이동 페널티가 실제로 적용될 때만 HUD 문구에 속도 비율이 붙어 사용자가 느려진 이유를 알 수 있어야 합니다.
    [Test]
    public void SatietyHud_ShowsMovementPenaltyOnlyWhenActive()
    {
        Assert.That(AmmoHud.BuildSatietyText(75f, 100f, 1f), Is.EqualTo("포만 75/100"));
        Assert.That(AmmoHud.BuildSatietyText(0f, 100f, 0.7f), Does.Contain("이동 70%"));
    }

    // 실제 에셋 파일을 만들지 않고 소비 효과가 설정된 메모리 전용 아이템을 생성합니다.
    private static ItemDefinition CreateConsumable(
        string itemId,
        ItemType type,
        int maxStack,
        float healthRestore,
        float satietyRestore)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = itemId;
        serialized.FindProperty("itemType").enumValueIndex = (int)type;
        serialized.FindProperty("maxStack").intValue = maxStack;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.FindProperty("healthRestoreAmount").floatValue = healthRestore;
        serialized.FindProperty("satietyRestoreAmount").floatValue = satietyRestore;
        serialized.FindProperty("usable").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    // 비활성 GameObject에서는 Awake가 자동 실행되지 않으므로 테스트가 실제 초기화 순서를 직접 호출합니다.
    private static void InvokeAwake(MonoBehaviour component)
    {
        MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(awake, Is.Not.Null);
        awake.Invoke(component, null);
    }
}
