using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.AI;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;
using ZombieSurvival.UI;

public sealed class RoadmapFeatureTests
{
    private string savePath;

    [SetUp]
    public void SetUp()
    {
        // 실제 사용자의 저장 파일 대신 테스트 전용 임시 경로를 사용합니다.
        // 각 테스트 시작마다 정적 창고 상태도 비워 서로의 결과가 다음 테스트에 섞이지 않게 합니다.
        savePath = Path.Combine(Application.temporaryCachePath, "zombie_survival_roadmap_save_test.json");
        DeleteSaveFiles();
        HideoutStateStore.ResetForTests();
        HideoutSaveService.SetDefaultPathForTests(savePath);
    }

    [TearDown]
    public void TearDown()
    {
        HideoutStateStore.ResetForTests();
        HideoutSaveService.ResetTestOverrides();
        DeleteSaveFiles();
    }

    // 방어구 감소율만큼 피해를 흡수하되 남은 내구도보다 많은 피해를 사라지게 만들지 않는지 검사합니다.
    [TestCase(20f, 0.4f, 100f, 12f, 8f)]
    [TestCase(20f, 0.4f, 3f, 17f, 3f)]
    [TestCase(20f, 0.4f, 0f, 20f, 0f)]
    public void ArmorCalculation_UsesReductionWithoutExceedingDurability(
        float incoming,
        float reduction,
        float durability,
        float expectedHealthDamage,
        float expectedDurabilityDamage)
    {
        float result = ArmorProtection.CalculateDamageAfterArmor(
            incoming,
            reduction,
            durability,
            out float absorbed);

        Assert.That(result, Is.EqualTo(expectedHealthDamage).Within(0.001f));
        Assert.That(absorbed, Is.EqualTo(expectedDurabilityDamage).Within(0.001f));
    }

    // 좀비 드롭 확률의 실패·성공과 최소/최대 수량 경계가 의도한 정수로 계산되는지 검사합니다.
    [TestCase(0f, 2, 5, 0f, 0f, 0)]
    [TestCase(0.5f, 2, 5, 0.75f, 0f, 0)]
    [TestCase(0.5f, 2, 5, 0.49f, 0f, 2)]
    [TestCase(1f, 2, 5, 1f, 1f, 5)]
    public void ZombieLootCalculation_RespectsChanceAndQuantityBounds(
        float chance,
        int minimum,
        int maximum,
        float chanceRoll,
        float quantityRoll,
        int expected)
    {
        Assert.That(
            ZombieLootDrop.CalculateDropQuantity(chance, minimum, maximum, chanceRoll, quantityRoll),
            Is.EqualTo(expected));
    }

    // 추가 탈출 조건은 요구 처치 수와 최소 생존 시간을 둘 다 만족해야 하며 정확한 경계값은 허용합니다.
    [TestCase(1, 2, 20f, 15f, false)]
    [TestCase(2, 2, 14.99f, 15f, false)]
    [TestCase(2, 2, 15f, 15f, true)]
    public void RaidExtractionProgress_RequiresKillsAndMinimumTime(
        int kills,
        int requiredKills,
        float elapsed,
        float minimumTime,
        bool expected)
    {
        Assert.That(
            RaidSession.MeetsExtractionProgress(kills, requiredKills, elapsed, minimumTime),
            Is.EqualTo(expected));
    }

    // 현재 방향으로 들어가지 않는 긴 아이템은 반대 방향의 빈칸을 찾아 회전한 뒤 다른 격자로 이동해야 합니다.
    [Test]
    public void GridTransfer_RotatesOnlyWhenNeeded()
    {
        ItemDefinition item = CreateItem("long_item", ItemType.Quest, 2, 1);
        try
        {
            var source = new PlayerInventory.GridData(3, 3);
            var target = new PlayerInventory.GridData(1, 2);
            var entry = new InventoryEntry(item, 1, Vector2Int.zero);
            source.Entries.Add(entry);

            Assert.That(InventoryGridOperations.TryTransferToFirstSpace(source, target, entry), Is.True);
            Assert.That(source.Entries.Contains(entry), Is.False);
            Assert.That(target.Entries.Contains(entry), Is.True);
            Assert.That(entry.Rotated, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
    }

    // 반대 격자에도 자리가 없으면 이동과 회전이 모두 취소되어 원래 격자 상태가 그대로 남아야 합니다.
    [Test]
    public void GridTransfer_NoSpaceLeavesOriginalStateUntouched()
    {
        ItemDefinition item = CreateItem("too_large_item", ItemType.Quest, 2, 1);
        try
        {
            var source = new PlayerInventory.GridData(3, 3);
            var target = new PlayerInventory.GridData(1, 1);
            var entry = new InventoryEntry(item, 1, new Vector2Int(1, 1));
            source.Entries.Add(entry);

            Assert.That(InventoryGridOperations.TryTransferToFirstSpace(source, target, entry), Is.False);
            Assert.That(source.Entries.Contains(entry), Is.True);
            Assert.That(target.Entries, Is.Empty);
            Assert.That(entry.Position, Is.EqualTo(new Vector2Int(1, 1)));
            Assert.That(entry.Rotated, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(item);
        }
    }

    // 현재 버전 5 저장도 기존 평탄화 구조를 유지해 컨테이너 내부와 파손된 방어구의 0 내구도를 잃지 않아야 합니다.
    [Test]
    public void VersionFiveSave_RoundTripPreservesHierarchyAndArmorDurability()
    {
        var backpack = new RaidItemSnapshot { itemId = "backpack", quantity = 1, gridX = 2, gridY = 3 };
        backpack.contents.Add(new RaidItemSnapshot
        {
            itemId = "armor",
            quantity = 1,
            hasArmorState = true,
            armorDurability = 0f
        });
        var result = new RaidResult { succeeded = true, inventory = new RaidGridSnapshot() };
        result.inventory.items.Add(backpack);
        RaidResultStore.Set(result);
        HideoutStateStore.ApplyPendingRaidResult();

        Assert.That(HideoutSaveService.SaveDefault(), Is.True);
        string json = File.ReadAllText(savePath);
        Assert.That(json, Does.Contain("\"version\": 5"));
        Assert.That(json, Does.Contain("\"parentIndex\""));

        HideoutStateStore.ResetForTests();
        Assert.That(HideoutSaveService.LoadFromPath(savePath), Is.True);
        RaidItemSnapshot restoredBackpack = HideoutStateStore.StashItems[0];
        RaidItemSnapshot restoredArmor = restoredBackpack.contents[0];
        Assert.That(restoredBackpack.gridX, Is.EqualTo(2));
        Assert.That(restoredBackpack.gridY, Is.EqualTo(3));
        Assert.That(restoredArmor.hasArmorState, Is.True);
        Assert.That(restoredArmor.armorDurability, Is.Zero);
    }

    // 시설과 임무 필드가 없던 버전 3 파일도 읽고 새 진행 정보는 빈 기본 상태로 시작해야 기존 저장과 호환됩니다.
    [Test]
    public void VersionThreeSave_RemainsLoadableAfterHideoutProgressionUpgrade()
    {
        File.WriteAllText(
            savePath,
            "{\"version\":3,\"stashItems\":[{\"itemId\":\"medkit\",\"quantity\":1,\"parentIndex\":-1}],\"loadoutItems\":[]}");

        Assert.That(HideoutSaveService.LoadFromPath(savePath), Is.True);
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.StashItems[0].itemId, Is.EqualTo("medkit"));
        Assert.That(HideoutMissionStateStore.HasActiveMission, Is.False);
        Assert.That(HideoutFacilityStateStore.CaptureState(), Is.Empty);
    }

    // 이전 버전 2 저장의 탄창 필드도 새 버전 코드가 계속 불러올 수 있어야 기존 사용자의 데이터가 유지됩니다.
    [Test]
    public void VersionTwoSave_RemainsLoadableAfterArmorAndFlatSaveUpgrade()
    {
        File.WriteAllText(
            savePath,
            "{\"version\":2,\"stashItems\":[{\"itemId\":\"weapon_pistol\",\"quantity\":1," +
            "\"hasMagazineState\":true,\"magazineRounds\":3,\"contents\":[]}],\"loadoutItems\":[]}");

        Assert.That(HideoutSaveService.LoadFromPath(savePath), Is.True);
        Assert.That(HideoutStateStore.StashItems.Count, Is.EqualTo(1));
        Assert.That(HideoutStateStore.StashItems[0].hasMagazineState, Is.True);
        Assert.That(HideoutStateStore.StashItems[0].magazineRounds, Is.EqualTo(3));
        Assert.That(HideoutStateStore.StashItems[0].hasArmorState, Is.False);
    }

    // 상세 설명은 방어구의 현재 상태와 파손 시 효과가 없다는 안내를 함께 보여 줍니다.
    [Test]
    public void ItemDetail_ExplainsBrokenArmor()
    {
        ItemDefinition armor = CreateItem("armor_detail", ItemType.Armor, 2, 2, 0.4f, 100f);
        try
        {
            var entry = new InventoryEntry(armor, 1, Vector2Int.zero, false, false, -1, 0f);
            string detail = ItemDetailFormatter.Build(entry);

            Assert.That(detail, Does.Contain("피해 감소: 40%"));
            Assert.That(detail, Does.Contain("내구도: 0 / 100"));
            Assert.That(detail, Does.Contain("파손됨"));
        }
        finally
        {
            Object.DestroyImmediate(armor);
        }
    }

    // 단일 바닥 아이템과 시체는 통과 가능한 Trigger를 사용하고, 아이템 상자는 단단한 Collider를 유지하는지 검사합니다.
    [Test]
    public void WorldLootCollisionPolicy_SeparatesPickupsCorpsesAndSolidBoxes()
    {
        ItemDefinition item = CreateItem("collision_item", ItemType.Quest, 1, 1);
        GameObject pickupObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        WorldLootContainer corpse = null;
        WorldLootContainer lootBox = null;
        try
        {
            WorldItem pickup = pickupObject.AddComponent<WorldItem>();
            pickup.Initialize(item, 1);
            corpse = WorldLootContainer.SpawnCorpse("Test", Vector3.zero, Quaternion.identity);
            lootBox = WorldLootContainer.SpawnLootBox("Test Box", new Vector3(3f, 0f, 0f));

            Assert.That(pickupObject.GetComponent<Collider>().isTrigger, Is.True);
            foreach (Collider corpseCollider in corpse.GetComponentsInChildren<Collider>())
                Assert.That(corpseCollider.isTrigger, Is.True);
            Assert.That(lootBox.GetComponent<BoxCollider>().isTrigger, Is.False);
            Assert.That(lootBox.transform.localScale.y, Is.GreaterThan(0.3f));
        }
        finally
        {
            Object.DestroyImmediate(pickupObject);
            if (corpse != null) Object.DestroyImmediate(corpse.gameObject);
            if (lootBox != null) Object.DestroyImmediate(lootBox.gameObject);
            Object.DestroyImmediate(item);
        }
    }

    // 전리품 표의 추첨 횟수는 0 난수에서 최소값, 1 난수에서 최대값을 정확히 반환해야 합니다.
    [TestCase(2, 4, 0f, 2)]
    [TestCase(2, 4, 0.34f, 3)]
    [TestCase(2, 4, 1f, 4)]
    public void LootTableRollCount_UsesInclusiveConfiguredRange(
        int minimum,
        int maximum,
        float roll,
        int expected)
    {
        Assert.That(
            LootTableDefinition.CalculateRollCount(minimum, maximum, roll),
            Is.EqualTo(expected));
    }

    // 1:3 가중치에서 첫 후보가 전체 범위의 25%, 두 번째 후보가 나머지 75%를 차지하는지 검사합니다.
    [TestCase(0f, 0)]
    [TestCase(0.249f, 0)]
    [TestCase(0.25f, 1)]
    [TestCase(1f, 1)]
    public void LootTableWeightedSelection_UsesEntryWeights(float roll, int expectedIndex)
    {
        ItemDefinition first = CreateItem("weighted_first", ItemType.Food, 1, 1);
        ItemDefinition second = CreateItem("weighted_second", ItemType.Healing, 1, 1);
        try
        {
            LootTableEntry[] entries =
            {
                new LootTableEntry { item = first, weight = 1f },
                new LootTableEntry { item = second, weight = 3f }
            };

            Assert.That(
                LootTableDefinition.SelectWeightedEntryIndex(entries, roll),
                Is.EqualTo(expectedIndex));
        }
        finally
        {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }
    }

    // 필드 상자는 첫 수색에서만 표를 굴리고 다시 열어도 같은 아이템을 복제하지 않는지 검사합니다.
    [Test]
    public void FieldLootSource_PopulatesOnlyOnFirstSearch()
    {
        ItemDefinition item = CreateItem("field_loot_item", ItemType.Food, 1, 1);
        LootTableDefinition table = ScriptableObject.CreateInstance<LootTableDefinition>();
        WorldLootContainer box = null;
        GameObject player = null;
        try
        {
            table.Configure(
                "테스트 보급 상자",
                2,
                2,
                new LootTableEntry
                {
                    item = item,
                    weight = 1f,
                    minimumQuantity = 1,
                    maximumQuantity = 1
                });

            box = WorldLootContainer.SpawnLootBox("테스트 보급 상자", Vector3.zero, 3, 3);
            FieldLootSource source = box.gameObject.AddComponent<FieldLootSource>();
            source.Configure(table);
            player = new GameObject("FieldLootTestPlayer");
            PlayerInventory inventory = player.AddComponent<PlayerInventory>();
            box.SetInteractor(inventory);

            Assert.That(box.InteractionPrompt, Does.Contain("수색"));
            Assert.That(box.Interact(), Is.True);
            Assert.That(source.HasPopulated, Is.True);
            Assert.That(box.LootGrid.Entries.Count, Is.EqualTo(2));

            Assert.That(box.Interact(), Is.True);
            Assert.That(box.LootGrid.Entries.Count, Is.EqualTo(2));
        }
        finally
        {
            if (box != null) Object.DestroyImmediate(box.gameObject);
            if (player != null) Object.DestroyImmediate(player);
            Object.DestroyImmediate(table);
            Object.DestroyImmediate(item);
        }
    }

    // 실제 ScriptableObject 에셋이 첫 밸런스 기준과 일치하는지 검사해 생성기와 수동 에셋 수정이 서로 어긋나는 일을 막습니다.
    // 여기서는 모든 세부 숫자보다 게임 흐름을 결정하는 탈출 조건, 대표 무기, 대표 좀비와 보급 상자 범위를 확인합니다.
    [Test]
    public void PrototypeBalanceAssets_MatchFirstPlayablePass()
    {
        RaidRules rules = AssetDatabase.LoadAssetAtPath<RaidRules>("Assets/_Game/Data/Raid/default_raid_rules.asset");
        WeaponDefinition pistol = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/Data/Weapons/pistol.asset");
        WeaponDefinition rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/Data/Weapons/rifle.asset");
        ZombieArchetype walker = AssetDatabase.LoadAssetAtPath<ZombieArchetype>("Assets/_Game/Data/Zombies/walker.asset");
        ZombieArchetype runner = AssetDatabase.LoadAssetAtPath<ZombieArchetype>("Assets/_Game/Data/Zombies/runner.asset");
        LootTableDefinition supply = AssetDatabase.LoadAssetAtPath<LootTableDefinition>("Assets/_Game/Data/Loot/supply_crate.asset");

        Assert.That(rules, Is.Not.Null);
        Assert.That(rules.RequiredZombieKills, Is.EqualTo(3));
        Assert.That(rules.MinimumExtractionTimeSeconds, Is.EqualTo(25f));

        Assert.That(pistol.Damage, Is.EqualTo(32f));
        Assert.That(pistol.MagazineSize, Is.EqualTo(10));
        Assert.That(rifle.Damage, Is.EqualTo(28f));
        Assert.That(rifle.FireInterval, Is.EqualTo(0.14f));

        Assert.That(walker.MaximumHealth, Is.EqualTo(90f));
        Assert.That(walker.AttackDamage, Is.EqualTo(12f));
        // Runner는 기본 이동 속도 5인 플레이어보다 조금 빨라야 추격 압박을 만들고, 속도 7의 달리기로는 탈출할 수 있습니다.
        Assert.That(runner.MovementSpeed, Is.EqualTo(5.2f));
        Assert.That(runner.UnlockTimeSeconds, Is.EqualTo(50f));

        Assert.That(supply.MinimumRolls, Is.EqualTo(2));
        Assert.That(supply.MaximumRolls, Is.EqualTo(4));
    }

    private static ItemDefinition CreateItem(
        string itemId,
        ItemType type,
        int width,
        int height,
        float armorReduction = 0f,
        float armorDurability = 0f)
    {
        ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(item);
        serialized.FindProperty("itemId").stringValue = itemId;
        serialized.FindProperty("displayName").stringValue = itemId;
        serialized.FindProperty("itemType").enumValueIndex = (int)type;
        serialized.FindProperty("maxStack").intValue = 1;
        serialized.FindProperty("gridWidth").intValue = width;
        serialized.FindProperty("gridHeight").intValue = height;
        serialized.FindProperty("armorDamageReduction").floatValue = armorReduction;
        serialized.FindProperty("armorMaxDurability").floatValue = armorDurability;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private void DeleteSaveFiles()
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        if (File.Exists(savePath + ".tmp")) File.Delete(savePath + ".tmp");
    }
}
