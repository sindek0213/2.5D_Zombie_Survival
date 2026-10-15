using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.Player;
using Object = UnityEngine.Object;

public sealed class StatusEffectAndQuickSlotTests
{
    [SetUp]
    public void SetUp() => QuickSlotAssignmentStore.ResetForTests();

    [TearDown]
    public void TearDown() => QuickSlotAssignmentStore.ResetForTests();

    // 같은 그룹에서는 배율 숫자를 비교하지 않고 Priority가 큰 정의만 기존 효과를 교체해야 합니다.
    // 강한 효과가 적용된 뒤 약한 아이템을 다시 사용하려는 경우에는 거부되어야 아이템 소비도 막을 수 있습니다.
    [Test]
    public void SameGroup_HigherPriorityReplacesAndLowerPriorityIsRejected()
    {
        var player = new GameObject("StatusPriorityTestPlayer");
        StatusEffectDefinition weak = CreateEffect("weak_speed", "이동 강화", 10, 60f, EffectStatToggle.MovementSpeed, 1.15f, 1f);
        StatusEffectDefinition strong = CreateEffect("strong_speed", "이동 강화", 20, 25f, EffectStatToggle.MovementSpeed, 1.3f, 1f);
        try
        {
            PlayerStatusEffects effects = player.AddComponent<PlayerStatusEffects>();

            Assert.That(effects.TryApply(weak, 100f), Is.EqualTo(StatusEffectApplyResult.Applied));
            Assert.That(effects.TryApply(strong, 101f), Is.EqualTo(StatusEffectApplyResult.ReplacedWeakerEffect));
            Assert.That(effects.MovementSpeedMultiplier, Is.EqualTo(1.3f).Within(0.001f));
            Assert.That(effects.TryApply(weak, 102f), Is.EqualTo(StatusEffectApplyResult.RejectedLowerPriority));
            Assert.That(effects.ActiveEffects.Count, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(weak);
            Object.DestroyImmediate(strong);
            Object.DestroyImmediate(player);
        }
    }

    // 서로 다른 그룹은 동시에 유지되며 각 정의가 켠 능력치 토글에만 배율을 적용해야 합니다.
    [Test]
    public void DifferentGroups_StackOnlyTheirEnabledStats()
    {
        var player = new GameObject("StatusStackTestPlayer");
        StatusEffectDefinition speed = CreateEffect("speed", "이동 강화", 10, 30f, EffectStatToggle.MovementSpeed, 1.2f, 1f, "movement");
        StatusEffectDefinition guard = CreateEffect("guard", "피해 감소", 10, 30f, EffectStatToggle.IncomingDamage, 1f, 0.75f, "guard");
        try
        {
            PlayerStatusEffects effects = player.AddComponent<PlayerStatusEffects>();
            effects.TryApply(speed, 0f);
            effects.TryApply(guard, 0f);

            Assert.That(effects.ActiveEffects.Count, Is.EqualTo(2));
            Assert.That(effects.MovementSpeedMultiplier, Is.EqualTo(1.2f).Within(0.001f));
            Assert.That(effects.IncomingDamageMultiplier, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(effects.OutgoingDamageMultiplier, Is.EqualTo(1f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(speed);
            Object.DestroyImmediate(guard);
            Object.DestroyImmediate(player);
        }
    }

    // 주는 피해 토글은 총기뿐 아니라 PlayerCombat의 맨손·근접무기 피해 계산에도 같은 배율을 적용해야 합니다.
    [Test]
    public void OutgoingDamageEffect_ModifiesCurrentMeleeDamage()
    {
        var player = new GameObject("OutgoingDamageTestPlayer");
        player.SetActive(false);
        StatusEffectDefinition effect = CreateEffect(
            "melee_power", "공격 강화", 10, 30f, EffectStatToggle.OutgoingDamage, 1f, 1f, "power", 1.5f);
        try
        {
            PlayerStatusEffects status = player.AddComponent<PlayerStatusEffects>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            InvokeAwake(combat);
            status.TryApply(effect, Time.time);

            // PlayerCombat의 기본 맨손 피해 18에 1.5배가 적용되므로 27이 되어야 합니다.
            Assert.That(combat.CurrentDamage, Is.EqualTo(27f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(player);
        }
    }

    // 종료 시각과 같은 순간부터 효과가 제거되어 기본 배율 1로 돌아오는지 경계값을 검사합니다.
    [Test]
    public void ExpiredEffect_IsRemovedAtExactEndTime()
    {
        var player = new GameObject("StatusExpiryTestPlayer");
        StatusEffectDefinition effect = CreateEffect("short_speed", "짧은 강화", 10, 5f, EffectStatToggle.MovementSpeed, 1.5f, 1f);
        try
        {
            PlayerStatusEffects effects = player.AddComponent<PlayerStatusEffects>();
            effects.TryApply(effect, 10f);

            Assert.That(effects.RemoveExpiredEffects(14.999f), Is.False);
            Assert.That(effects.RemoveExpiredEffects(15f), Is.True);
            Assert.That(effects.MovementSpeedMultiplier, Is.EqualTo(1f));
        }
        finally
        {
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(player);
        }
    }

    // 강한 버프가 활성 중일 때 약한 버프 아이템 사용은 실패하고 인벤토리 수량을 그대로 보존해야 합니다.
    [Test]
    public void BuffConsumable_RejectedByPriorityDoesNotConsumeItem()
    {
        var player = new GameObject("BuffConsumptionTestPlayer");
        player.SetActive(false);
        StatusEffectDefinition weak = CreateEffect("weak_guard", "약한 보호", 10, 60f, EffectStatToggle.IncomingDamage, 1f, 0.85f, "guard");
        StatusEffectDefinition strong = CreateEffect("strong_guard", "강한 보호", 20, 60f, EffectStatToggle.IncomingDamage, 1f, 0.65f, "guard");
        ItemDefinition weakItem = CreateBuffItem("weak_guard_item", weak);
        try
        {
            PlayerStatusEffects effects = player.AddComponent<PlayerStatusEffects>();
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            InvokeAwake(inventory);
            effects.TryApply(strong, Time.time);
            Assert.That(inventory.TryAdd(weakItem, 1), Is.True);
            InventoryEntry entry = inventory.Entries[0];

            Assert.That(inventory.TryUse(inventory.Root, entry), Is.False);
            Assert.That(entry.Quantity, Is.EqualTo(1));
            Assert.That(inventory.GetUseFailureReason(inventory.Root, entry), Does.Contain("더 강한 효과"));
        }
        finally
        {
            Object.DestroyImmediate(weakItem);
            Object.DestroyImmediate(weak);
            Object.DestroyImmediate(strong);
            Object.DestroyImmediate(player);
        }
    }

    // 퀵슬롯은 격자 위치가 아니라 ItemId를 저장하므로 등록 뒤 현재 인벤토리에서 같은 ID를 다시 찾아 사용해야 합니다.
    [Test]
    public void QuickSlot_AssignsByItemIdAndUsesInventoryConsumable()
    {
        var player = new GameObject("QuickSlotUseTestPlayer");
        player.SetActive(false);
        StatusEffectDefinition effect = CreateEffect("quick_speed", "신속", 10, 30f, EffectStatToggle.MovementSpeed, 1.2f, 1f);
        ItemDefinition item = CreateBuffItem("quick_speed_item", effect);
        try
        {
            PlayerStatusEffects status = player.AddComponent<PlayerStatusEffects>();
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerQuickSlots slots = player.AddComponent<PlayerQuickSlots>();
            var inventoryData = new SerializedObject(inventory);
            // 테스트에서는 코루틴을 기다리지 않고 같은 최종 사용 경로를 즉시 검증합니다.
            inventoryData.FindProperty("buffUseDuration").floatValue = 0f;
            inventoryData.ApplyModifiedPropertiesWithoutUndo();
            InvokeAwake(inventory);
            InvokeAwake(slots);
            Assert.That(inventory.TryAdd(item, 1), Is.True);

            Assert.That(slots.TryAssign(0, item, out string assignMessage), Is.True, assignMessage);
            Assert.That(QuickSlotAssignmentStore.GetItemId(0), Is.EqualTo(item.ItemId));
            Assert.That(slots.TryUseSlot(0, out string useMessage), Is.True, useMessage);
            Assert.That(inventory.CountItem(item.ItemId), Is.Zero);
            Assert.That(status.MovementSpeedMultiplier, Is.EqualTo(1.2f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(item);
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(player);
        }
    }

    // 기본 두 칸에 방탄복의 증설 수치를 더하되 전체 키 수인 열 칸을 넘지 않아야 합니다.
    // 잠긴 칸으로 드래그한 등록 요청은 실패해야 기존 아이템과 다른 슬롯 배치를 건드리지 않습니다.
    [Test]
    public void QuickSlotCapacity_UsesArmorBonusAndRejectsLockedSlot()
    {
        var player = new GameObject("QuickSlotArmorCapacityTestPlayer");
        player.SetActive(false);
        StatusEffectDefinition effect = CreateEffect("capacity_buff", "테스트", 1, 10f, EffectStatToggle.MovementSpeed, 1.1f, 1f);
        ItemDefinition consumable = CreateBuffItem("capacity_item", effect);
        ItemDefinition armor = CreateArmorItem("capacity_armor", 2);
        try
        {
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            PlayerQuickSlots slots = player.AddComponent<PlayerQuickSlots>();
            InvokeAwake(inventory);
            InvokeAwake(slots);
            Assert.That(inventory.TryAdd(consumable, 1), Is.True);

            Assert.That(slots.UnlockedSlotCount, Is.EqualTo(2));
            Assert.That(slots.TryAssign(2, consumable, out string lockedMessage), Is.False);
            Assert.That(lockedMessage, Does.Contain("잠겨"));

            Assert.That(inventory.TryEquipRaidProvidedItem(armor), Is.True);
            Assert.That(slots.UnlockedSlotCount, Is.EqualTo(4));
            Assert.That(slots.TryAssign(3, consumable, out string assignedMessage), Is.True, assignedMessage);
            Assert.That(slots.TryAssign(4, consumable, out _), Is.False);
            Assert.That(PlayerQuickSlots.GetSlotKeyLabel(9), Is.EqualTo("0"));
        }
        finally
        {
            Object.DestroyImmediate(armor);
            Object.DestroyImmediate(consumable);
            Object.DestroyImmediate(effect);
            Object.DestroyImmediate(player);
        }
    }

    // 판정 앞에 벽이 있으면 그 뒤의 피해 대상까지 근접 공격이 통과해서는 안 됩니다.
    [Test]
    public void MeleeResolver_NonDamageableWallBlocksTarget()
    {
        var attacker = new GameObject("MeleeResolverAttacker");
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            attacker.transform.position = Vector3.zero;
            wall.transform.position = new Vector3(0f, 0f, 1f);
            target.transform.position = new Vector3(0f, 0f, 2f);
            target.SetActive(false);
            Health health = target.AddComponent<Health>();
            InvokeAwake(health);
            target.SetActive(true);
            Physics.SyncTransforms();

            var resolver = new MeleeAttackResolver();
            MeleeAttackResult result = resolver.Resolve(
                attacker.transform, Vector3.zero, Vector3.forward, 3f, 0.2f, 3, 25f, 0f, 0, "test", ~0);

            Assert.That(result.DamagedTarget, Is.False);
            Assert.That(health.CurrentHealth, Is.EqualTo(100f));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(attacker);
        }
    }

    // 한 적에게 여러 Collider가 붙어 있어도 Health 한 개에는 공격 한 번당 피해를 한 번만 적용해야 합니다.
    [Test]
    public void MeleeResolver_MultipleCollidersOnOneTargetDealDamageOnce()
    {
        var attacker = new GameObject("MeleeDuplicateAttacker");
        var target = new GameObject("MeleeDuplicateTarget");
        target.SetActive(false);
        try
        {
            target.transform.position = new Vector3(0f, 0f, 1.5f);
            target.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
            target.AddComponent<SphereCollider>().radius = 0.35f;
            Health health = target.AddComponent<Health>();
            InvokeAwake(health);
            target.SetActive(true);
            Physics.SyncTransforms();

            var resolver = new MeleeAttackResolver();
            MeleeAttackResult result = resolver.Resolve(
                attacker.transform, Vector3.zero, Vector3.forward, 3f, 0.2f, 3, 20f, 0f, 0, "test", ~0);

            Assert.That(result.DamagedTargetCount, Is.EqualTo(1));
            Assert.That(health.CurrentHealth, Is.EqualTo(80f));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(attacker);
        }
    }

    // 도끼처럼 다중 타격이 가능한 무기도 Definition의 MaximumTargets 수를 넘겨 뒤쪽 대상까지 피해를 주면 안 됩니다.
    [Test]
    public void MeleeResolver_StopsAtConfiguredMaximumTargets()
    {
        var attacker = new GameObject("MeleeMaximumAttacker");
        var targets = new GameObject[4];
        var healthValues = new Health[4];
        try
        {
            for (int index = 0; index < targets.Length; index++)
            {
                targets[index] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                targets[index].name = "MeleeMaximumTarget" + index;
                targets[index].SetActive(false);
                targets[index].transform.position = new Vector3(0f, 0f, 0.7f + index * 0.7f);
                targets[index].transform.localScale = Vector3.one * 0.3f;
                healthValues[index] = targets[index].AddComponent<Health>();
                InvokeAwake(healthValues[index]);
                targets[index].SetActive(true);
            }
            Physics.SyncTransforms();

            var resolver = new MeleeAttackResolver();
            MeleeAttackResult result = resolver.Resolve(
                attacker.transform, Vector3.zero, Vector3.forward, 4f, 0.15f, 2, 10f, 0f, 0, "test", ~0);

            Assert.That(result.DamagedTargetCount, Is.EqualTo(2));
            Assert.That(healthValues[0].CurrentHealth, Is.EqualTo(90f));
            Assert.That(healthValues[1].CurrentHealth, Is.EqualTo(90f));
            Assert.That(healthValues[2].CurrentHealth, Is.EqualTo(100f));
            Assert.That(healthValues[3].CurrentHealth, Is.EqualTo(100f));
        }
        finally
        {
            foreach (GameObject target in targets)
                if (target != null) Object.DestroyImmediate(target);
            Object.DestroyImmediate(attacker);
        }
    }

    private static StatusEffectDefinition CreateEffect(
        string id,
        string displayName,
        int priority,
        float duration,
        EffectStatToggle stats,
        float movementMultiplier,
        float incomingMultiplier,
        string group = "movement",
        float outgoingMultiplier = 1f)
    {
        StatusEffectDefinition definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();
        definition.Configure(
            id,
            displayName,
            StatusEffectDisposition.Buff,
            group,
            priority,
            duration,
            stats,
            movementMultiplier,
            incomingMultiplier,
            outgoingMultiplier);
        return definition;
    }

    private static ItemDefinition CreateBuffItem(string itemId, StatusEffectDefinition effect)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = itemId;
        serialized.FindProperty("itemType").enumValueIndex = (int)ItemType.BuffConsumable;
        serialized.FindProperty("maxStack").intValue = 3;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.FindProperty("usable").boolValue = true;
        serialized.FindProperty("statusEffectDefinition").objectReferenceValue = effect;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static ItemDefinition CreateArmorItem(string itemId, int quickSlotBonus)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = itemId;
        serialized.FindProperty("itemType").enumValueIndex = (int)ItemType.Armor;
        serialized.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.Armor;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.FindProperty("armorMaxDurability").floatValue = 100f;
        serialized.FindProperty("quickSlotBonus").intValue = quickSlotBonus;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    // 비활성 테스트 오브젝트에서는 Unity 생명주기가 자동 실행되지 않으므로 실제 private Awake를 같은 순서로 호출합니다.
    private static void InvokeAwake(MonoBehaviour component)
    {
        MethodInfo awake = component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(awake, Is.Not.Null, component.GetType().Name + "에 Awake가 없습니다.");
        awake.Invoke(component, null);
    }
}
