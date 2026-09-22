using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Hideout;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Core
{
    // Hideout에서 선택한 출격품을 RaidPrototype 플레이어의 루트 인벤토리에 한 번 복원합니다.
    // 성공한 항목은 현재 레이드 소지품이 되고, 복원할 수 없는 항목은 손실시키지 않고 거점 창고로 반환합니다.
    public sealed class RaidLoadoutApplier : MonoBehaviour
    {
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private string testSidearmItemId = "weapon_pistol";
        [SerializeField] private string hideoutSceneName = GameSceneNavigator.HideoutSceneName;
        private bool loadoutApplied;

        public int AppliedItemCount { get; private set; }
        public int ReturnedItemCount { get; private set; }
        public bool ProvidedTestSidearm { get; private set; }
        public bool LoadoutApplicationFailed { get; private set; }

        // 에디터 생성기가 플레이어와 공용 카탈로그를 정확히 연결하도록 제공하는 설정 메서드입니다.
        public void Configure(PlayerInventory inventory, ItemCatalog catalog)
        {
            playerInventory = inventory;
            itemCatalog = catalog;
        }

        private void Start() => ApplyPreparedLoadout();

        // 일반 플레이에서는 Start가 호출하고 EditMode 테스트나 개발용 도구에서도 같은 출격 복원 흐름을 직접 검증할 수 있습니다.
        // 두 번 호출되어도 첫 실행 뒤 loadoutApplied가 막으므로 같은 출격품이 복제되어 플레이어에게 중복 지급되지 않습니다.
        public void ApplyPreparedLoadout()
        {
            if (loadoutApplied) return;
            loadoutApplied = true;
            if (playerInventory == null) playerInventory = FindObjectOfType<PlayerInventory>();

            // Raid 씬을 직접 실행하거나 게임을 재시작한 경우에도 저장된 출격 목록을 먼저 메모리에 복원합니다.
            HideoutSaveService.LoadDefaultIfNeeded();
            List<RaidItemSnapshot> loadout = HideoutStateStore.TakeLoadoutForRaid();

            var failedItems = new List<RaidItemSnapshot>();
            // 저장 실패 시 플레이어에게 들어간 항목만 정확히 회수할 수 있도록 성공한 원본 스냅샷도 따로 기억합니다.
            // failedItems는 이미 창고로 반환되므로 전체 loadout을 다시 반환하면 실패 항목이 두 번 생길 수 있습니다.
            var appliedItems = new List<RaidItemSnapshot>();
            foreach (RaidItemSnapshot snapshot in loadout)
            {
                if (playerInventory != null &&
                    RaidSnapshotUtility.TryRestoreItem(playerInventory.Root, itemCatalog, snapshot, out _))
                {
                    AppliedItemCount++;
                    appliedItems.Add(snapshot);
                }
                else
                {
                    // 정의를 찾지 못했거나 격자 공간이 부족한 항목은 별도 목록에 모아 전체 순회가 끝난 뒤 창고로 반환합니다.
                    failedItems.Add(snapshot);
                }
            }

            ReturnedItemCount = failedItems.Count;
            HideoutStateStore.ReturnItemsToStash(failedItems);

            // 출격 목록을 메모리에서 비운 상태가 디스크에도 기록된 뒤에만 실제 레이드를 계속 진행합니다.
            // 이 저장이 실패한 채 플레이하면 강제 종료 후 이전 저장의 출격품과 현재 플레이어 아이템이 함께 존재해 복제될 수 있습니다.
            if (loadout.Count > 0 && !HideoutSaveService.SaveDefault())
            {
                // 성공적으로 복원했던 출격품을 플레이어에게서 제거하고, 실패 항목과 겹치지 않게 appliedItems만 창고로 돌려놓습니다.
                // 디스크에는 이전 출격 목록이 그대로 남고 메모리에는 같은 아이템이 창고에 한 번만 있으므로 어느 쪽에서도 복제되지 않습니다.
                playerInventory?.DiscardAllRaidItems();
                HideoutStateStore.ReturnItemsToStash(appliedItems);
                AppliedItemCount = 0;
                ReturnedItemCount = loadout.Count;
                LoadoutApplicationFailed = true;
                Debug.LogError("출격품 소비 상태를 저장하지 못해 레이드를 취소하고 아이템을 거점으로 반환합니다.");

                // 정상 게임에서는 즉시 거점으로 돌아가며, 씬 이름이 비어 있는 EditMode 테스트에서는 상태 검증만 하고 전환을 생략합니다.
                if (!string.IsNullOrWhiteSpace(hideoutSceneName))
                    GameSceneNavigator.TryLoadScene(hideoutSceneName);
                return;
            }

            // 플레이어가 보조무기로 가져온 권총이 있으면 그 실제 아이템을 우선 장착합니다.
            // 준비한 권총이 없을 때만 카탈로그에서 테스트 지급 권총을 만들어, 무기 없이도 발사되던 이전 우회 동작을 없앱니다.
            EnsureTestSidearmEquipped();
        }

        private void EnsureTestSidearmEquipped()
        {
            if (playerInventory == null || itemCatalog == null ||
                playerInventory.Equipment.ContainsKey(EquipmentSlotType.SecondaryWeapon)) return;

            // Hideout에서 가져온 권총이 루트 격자에 있으면 무료 지급품을 만들지 않고 그 엔트리를 그대로 장착합니다.
            foreach (InventoryEntry entry in playerInventory.Root.Entries.ToArray())
            {
                if (entry.Item.ItemId != testSidearmItemId ||
                    entry.Item.EquipmentSlot != EquipmentSlotType.SecondaryWeapon) continue;
                if (playerInventory.TryEquip(playerInventory.Root, entry)) return;
            }

            ItemDefinition sidearm = itemCatalog.Find(testSidearmItemId);
            ProvidedTestSidearm = sidearm != null && playerInventory.TryEquipRaidProvidedItem(sidearm);
        }
    }
}
