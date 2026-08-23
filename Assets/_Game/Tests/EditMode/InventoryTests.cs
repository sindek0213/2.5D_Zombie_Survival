using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

public sealed class InventoryTests
{
    private GameObject player;
    private ItemDefinition definition;

    [SetUp]
    public void SetUp()
    {
        // 테스트용 아이템 정의와 플레이어 인벤토리를 메모리에 생성합니다.
        definition = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("itemId").stringValue = "ammo_test";
        serialized.FindProperty("displayName").stringValue = "테스트 탄약";
        serialized.FindProperty("maxStack").intValue = 30;
        serialized.FindProperty("gridWidth").intValue = 1;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        player = new GameObject("InventoryTestPlayer");
        player.AddComponent<PlayerInventory>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(definition);
    }

    // MaxStack 30인 아이템 35개를 추가했을 때 30과 5 두 묶음으로 나뉘는지 확인합니다.
    // 최대 중첩을 넘거나 전체 수량 일부가 사라지는 회귀 오류를 방지하는 테스트입니다.
    [Test]
    public void Add_UsesMultipleSlotsWhenStackIsFull()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 35), Is.True);
        Assert.That(inventory.Entries[0].Quantity, Is.EqualTo(30));
        Assert.That(inventory.Entries[1].Quantity, Is.EqualTo(5));
    }

    // 전체 슬롯 공간보다 많은 수량은 일부만 넣지 않고 거부하는지 검증합니다.
    [Test]
    public void Add_WhenCapacityIsInsufficient_DoesNotChangeInventory()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 1801), Is.False);
        Assert.That(inventory.Entries, Is.Empty);
    }

    // 다중 칸 아이템이 회전하면 가로와 세로 점유 크기가 교환되는지 검증합니다.
    [Test]
    public void Rotate_SwapsGridDimensions()
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("gridWidth").intValue = 2;
        serialized.FindProperty("gridHeight").intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        Assert.That(inventory.TryAdd(definition, 1), Is.True);
        InventoryEntry entry = inventory.Entries[0];
        Assert.That(inventory.TryRotate(entry), Is.True);
        Assert.That(entry.Width, Is.EqualTo(1));
        Assert.That(entry.Height, Is.EqualTo(2));
    }

    // 테스트 아이템을 Backpack 장비로 설정하고 장착했을 때 루트에서 제거되고 장비 Dictionary로 이동하는지 확인합니다.
    // 동시에 정의의 5x4 크기가 새 컨테이너 Contents에 정확히 복사되는지도 검증합니다.
    [Test]
    public void Equip_MovesEntryIntoMatchingSlot()
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty("equipmentSlot").enumValueIndex = (int)EquipmentSlotType.Backpack;
        serialized.FindProperty("containerWidth").intValue = 5;
        serialized.FindProperty("containerHeight").intValue = 4;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        inventory.TryAdd(definition, 1);
        InventoryEntry entry = inventory.Entries[0];
        Assert.That(inventory.TryEquip(inventory.Root, entry), Is.True);
        Assert.That(inventory.Equipment[EquipmentSlotType.Backpack], Is.SameAs(entry));
        Assert.That(entry.Contents.Width, Is.EqualTo(5));
    }

    // 수량 20 묶음을 절반 분할한 뒤 두 엔트리가 서로 다른 Position을 가지는지 확인합니다.
    // 새 묶음이 원본의 점유 칸 위에 겹쳐 생성됐던 회귀 가능성을 방지합니다.
    [Test]
    public void Split_CreatesNonOverlappingEntry()
    {
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        inventory.TryAdd(definition, 20);
        InventoryEntry original = inventory.Entries[0];
        Assert.That(inventory.TrySplit(inventory.Root, original), Is.True);
        Assert.That(inventory.Entries.Count, Is.EqualTo(2));
        Assert.That(inventory.Entries[0].Position, Is.Not.EqualTo(inventory.Entries[1].Position));
    }
}
