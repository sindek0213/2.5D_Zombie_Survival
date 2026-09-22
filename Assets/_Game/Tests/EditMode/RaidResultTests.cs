using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

public sealed class RaidResultTests
{
    private GameObject player;
    private ItemDefinition definition;

    [SetUp]
    public void SetUp()
    {
        // 디스크 에셋을 변경하지 않도록 메모리 전용 ItemDefinition과 PlayerInventory를 생성합니다.
        definition = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("itemId").stringValue = "quest_radio_test";
        serialized.FindProperty("displayName").stringValue = "테스트 무전기";
        serialized.FindProperty("maxStack").intValue = 1;
        serialized.FindProperty("gridWidth").intValue = 2;
        serialized.FindProperty("gridHeight").intValue = 2;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        player = new GameObject("RaidResultTestPlayer");
        player.AddComponent<PlayerInventory>().TryAdd(definition, 1);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(definition);
        RaidResultStore.Clear();
    }

    // 탈출 성공은 아이템 ID, 수량과 격자 위치를 결과 스냅샷에 그대로 복사해야 합니다.
    [Test]
    public void SuccessfulRaid_PreservesInventorySnapshot()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        RaidResult result = RaidResultFactory.Create(inventory, RaidEndReason.Extracted, 3);

        Assert.That(result.succeeded, Is.True);
        Assert.That(result.zombieKills, Is.EqualTo(3));
        Assert.That(result.inventory.items.Count, Is.EqualTo(1));
        Assert.That(result.inventory.items[0].itemId, Is.EqualTo("quest_radio_test"));
    }

    // 사망 결과는 손실 수량만 기록하고 창고로 지급할 인벤토리 목록은 비워야 합니다.
    [Test]
    public void FailedRaid_DoesNotExposeRewardInventory()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        RaidResult result = RaidResultFactory.Create(inventory, RaidEndReason.PlayerDied, 0);

        Assert.That(result.succeeded, Is.False);
        Assert.That(result.lostItemCount, Is.EqualTo(1));
        Assert.That(result.inventory.items, Is.Empty);
        Assert.That(result.equipment, Is.Empty);
    }

    // 씬 참조가 잘못되어 PlayerInventory를 찾지 못해도 탈출·사망 결과 생성과 거점 복귀 흐름이 중단되면 안 됩니다.
    // 이 경우 복원할 수 있는 실제 아이템 상태가 없으므로 안전한 빈 스냅샷과 손실 수량 0을 사용합니다.
    [TestCase(RaidEndReason.PlayerDied, false)]
    [TestCase(RaidEndReason.Extracted, true)]
    public void MissingInventory_StillCreatesAUsableRaidResult(
        RaidEndReason reason,
        bool expectedSuccess)
    {
        RaidResult result = RaidResultFactory.Create(null, reason, 2, 40f);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.succeeded, Is.EqualTo(expectedSuccess));
        Assert.That(result.inventory, Is.Not.Null);
        Assert.That(result.inventory.items, Is.Empty);
        Assert.That(result.equipment, Is.Empty);
        Assert.That(result.lostItemCount, Is.Zero);
    }

    // 사망 결과를 만든 뒤 실제 플레이어 인벤토리를 비우면 루트 아이템뿐 아니라 장착 가방과 그 내부 내용물까지 모두 사라지는지 검사합니다.
    // 결과의 손실 수량은 비우기 전에 계산되므로 무엇을 잃었는지는 표시할 수 있지만 복원 가능한 아이템 스냅샷은 남지 않아야 합니다.
    [Test]
    public void FailedRaid_DiscardAllRaidItemsRemovesRootEquipmentAndNestedContents()
    {
        ItemDefinition backpack = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            var serialized = new SerializedObject(backpack);
            serialized.FindProperty("itemId").stringValue = "death_loss_backpack";
            serialized.FindProperty("displayName").stringValue = "사망 손실 테스트 가방";
            serialized.FindProperty("itemType").enumValueIndex = (int)ItemType.Container;
            serialized.FindProperty("maxStack").intValue = 1;
            serialized.FindProperty("gridWidth").intValue = 1;
            serialized.FindProperty("gridHeight").intValue = 1;
            serialized.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.Backpack;
            serialized.FindProperty("containerWidth").intValue = 3;
            serialized.FindProperty("containerHeight").intValue = 3;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(backpack, 1), Is.True);
            InventoryEntry backpackEntry = inventory.Entries[inventory.Entries.Count - 1];
            // 가방을 장착하기 전에 내부에 아이템을 넣어 중첩 컨테이너도 장비와 함께 제거되는 경로를 재현합니다.
            backpackEntry.Contents.Entries.Add(new InventoryEntry(definition, 1, Vector2Int.zero));
            Assert.That(inventory.TryEquip(inventory.Root, backpackEntry), Is.True);

            RaidResult result = RaidResultFactory.Create(inventory, RaidEndReason.PlayerDied, 0);
            Assert.That(result.lostItemCount, Is.EqualTo(3));

            inventory.DiscardAllRaidItems();

            Assert.That(inventory.Root.Entries, Is.Empty);
            Assert.That(inventory.Equipment, Is.Empty);
            Assert.That(result.inventory.items, Is.Empty);
            Assert.That(result.equipment, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(backpack);
        }
    }

    // 테스트 레이드가 무료로 장착한 권총은 성공 보상과 사망 손실 수량 어디에도 포함되지 않는지 검사합니다.
    [Test]
    public void RaidProvidedSidearm_IsExcludedFromRewardAndLoss()
    {
        WeaponDefinition weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        ItemDefinition pistol = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            var serialized = new SerializedObject(pistol);
            serialized.FindProperty("itemId").stringValue = "provided_pistol_test";
            serialized.FindProperty("displayName").stringValue = "테스트 지급 권총";
            serialized.FindProperty("itemType").enumValueIndex = (int)ItemType.Weapon;
            serialized.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.SecondaryWeapon;
            serialized.FindProperty("weaponDefinition").objectReferenceValue = weapon;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            Assert.That(inventory.TryEquipRaidProvidedItem(pistol), Is.True);
            Assert.That(inventory.Equipment[EquipmentSlotType.SecondaryWeapon].IsRaidProvided, Is.True);

            RaidResult success = RaidResultFactory.Create(inventory, RaidEndReason.Extracted, 0);
            RaidResult failure = RaidResultFactory.Create(inventory, RaidEndReason.PlayerDied, 0);

            Assert.That(success.inventory.items.Count, Is.EqualTo(1));
            Assert.That(success.equipment, Is.Empty);
            Assert.That(failure.lostItemCount, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(pistol);
            Object.DestroyImmediate(weapon);
        }
    }

    // 플레이어가 사용한 무기의 남은 탄창이 결과 스냅샷에 기록되고 새 인벤토리로 복원될 때도 같은 수량인지 검사합니다.
    [Test]
    public void WeaponMagazine_RaidSnapshotAndRestorePreserveLoadedRounds()
    {
        WeaponDefinition weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        ItemDefinition weaponItem = ScriptableObject.CreateInstance<ItemDefinition>();
        ItemCatalog catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        try
        {
            var weaponData = new SerializedObject(weapon);
            weaponData.FindProperty("magazineSize").intValue = 8;
            weaponData.FindProperty("ammoType").enumValueIndex = (int)AmmoType.NineMillimeter;
            weaponData.ApplyModifiedPropertiesWithoutUndo();

            var itemData = new SerializedObject(weaponItem);
            itemData.FindProperty("itemId").stringValue = "stored_magazine_pistol";
            itemData.FindProperty("displayName").stringValue = "탄창 저장 테스트 권총";
            itemData.FindProperty("itemType").enumValueIndex = (int)ItemType.Weapon;
            itemData.FindProperty("maxStack").intValue = 1;
            itemData.FindProperty("gridWidth").intValue = 1;
            itemData.FindProperty("gridHeight").intValue = 1;
            itemData.FindProperty("weaponDefinition").objectReferenceValue = weapon;
            itemData.ApplyModifiedPropertiesWithoutUndo();

            var catalogData = new SerializedObject(catalog);
            SerializedProperty definitions = catalogData.FindProperty("definitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = weaponItem;
            catalogData.ApplyModifiedPropertiesWithoutUndo();

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            Assert.That(inventory.TryAdd(weaponItem, 1), Is.True);
            InventoryEntry original = inventory.Entries[inventory.Entries.Count - 1];
            Assert.That(original.TryConsumeMagazineRound(), Is.True);
            Assert.That(original.TryConsumeMagazineRound(), Is.True);
            Assert.That(original.TryConsumeMagazineRound(), Is.True);
            Assert.That(original.MagazineRounds, Is.EqualTo(5));

            RaidResult result = RaidResultFactory.Create(inventory, RaidEndReason.Extracted, 0);
            RaidItemSnapshot snapshot = result.inventory.items.Find(item => item.itemId == "stored_magazine_pistol");
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.hasMagazineState, Is.True);
            Assert.That(snapshot.magazineRounds, Is.EqualTo(5));

            var restoredGrid = new PlayerInventory.GridData(4, 4);
            Assert.That(RaidSnapshotUtility.TryRestoreItem(restoredGrid, catalog, snapshot, out InventoryEntry restored), Is.True);
            Assert.That(restored.MagazineRounds, Is.EqualTo(5));
        }
        finally
        {
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(weaponItem);
            Object.DestroyImmediate(weapon);
        }
    }
}
