using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Core
{
    // 레이드가 끝난 원인을 정상 탈출, 플레이어 사망과 사용자의 직접 포기로 명확하게 구분합니다.
    // 레이드에는 제한 시간 실패가 없으므로 시간이 오래 지나도 이 값이 자동으로 만들어지지 않습니다.
    public enum RaidEndReason
    {
        Extracted,
        PlayerDied,
        Abandoned
    }

    // 한 아이템 묶음의 ID, 수량, 격자 위치와 컨테이너 내부를 Unity 참조 없이 복사한 데이터입니다.
    // ScriptableObject를 직접 저장하지 않고 ItemId만 기록하므로 JSON 직렬화와 게임 재실행 후 복원이 가능합니다.
    [Serializable]
    public sealed class RaidItemSnapshot
    {
        public string itemId;
        public int quantity;
        public int gridX;
        public int gridY;
        public bool rotated;
        // 무기일 때만 true이며, magazineRounds가 0인 빈 탄창도 정상 저장값으로 구분합니다.
        // 이전 버전 저장 파일에는 이 bool이 없어서 false로 읽히며 복원 시 기존 규칙대로 가득 찬 탄창을 제공합니다.
        public bool hasMagazineState;
        public int magazineRounds;
        // 버전 8부터 무기마다 남은 내구도와 선택한 발사 모드를 함께 저장합니다.
        // has... 값이 false인 구버전 데이터는 복원 시 최대 내구도와 무기 기본 발사 모드를 사용합니다.
        public bool hasWeaponDurabilityState;
        public float weaponDurability;
        public bool hasTriggerModeState;
        public int selectedTriggerMode;
        // 방탄복일 때만 true이며 0인 완전 파손 상태도 필드가 없던 이전 저장과 구분합니다.
        public bool hasArmorState;
        public float armorDurability;
        // 무기에 설치된 부착물은 격자 아이템과 별도로 각 ItemId만 저장합니다.
        // 실제 ItemDefinition 참조는 복원할 때 ItemCatalog에서 다시 찾습니다.
        public List<string> attachmentItemIds = new List<string>();
        public List<RaidItemSnapshot> contents = new List<RaidItemSnapshot>();
    }

    // 하나의 격자 크기와 그 안의 모든 아이템 묶음을 저장합니다.
    // 루트 인벤토리와 가방 내부가 같은 형식을 사용하므로 복원 코드도 같은 순회 방식을 재사용할 수 있습니다.
    [Serializable]
    public sealed class RaidGridSnapshot
    {
        public int width;
        public int height;
        public List<RaidItemSnapshot> items = new List<RaidItemSnapshot>();
    }

    // 장비 슬롯 이름과 그 슬롯에 장착된 아이템 스냅샷을 한 쌍으로 저장합니다.
    // Dictionary는 Unity JsonUtility가 직접 직렬화하지 못하므로 직렬화 가능한 List 항목 형태를 사용합니다.
    [Serializable]
    public sealed class RaidEquipmentSnapshot
    {
        public string slot;
        public RaidItemSnapshot item;
    }

    // 결과와 임무 계산에서 무기별 처치 수를 JSON으로 저장하기 위한 한 줄 데이터입니다.
    [Serializable]
    public sealed class RaidWeaponKillStat
    {
        public string weaponItemId;
        public int kills;
    }

    // 한 레이드의 최종 결과입니다. 정상 탈출한 경우에만 inventory와 equipment에 회수할 획득품을 넣습니다.
    // 사망 결과에는 lostItemCount만 남기므로 이후 창고 시스템이 실수로 사망한 플레이어에게 보상을 지급할 수 없습니다.
    [Serializable]
    public sealed class RaidResult
    {
        public RaidEndReason endReason;
        public bool succeeded;
        public int zombieKills;
        public float raidDurationSeconds;
        public int lostItemCount;
        public int extractedItemValue;
        public int lostItemValue;
        public int shotsFired;
        public int successfulShots;
        public float damageTaken;
        public List<RaidWeaponKillStat> weaponKills = new List<RaidWeaponKillStat>();
        // 사망 결과에서만 Health가 확정한 마지막 치명상 원인을 복사합니다.
        // 빈 문자열인 이전 결과 데이터도 "플레이어 사망"으로 안전하게 표시하도록 UI에서 예비 문구를 사용합니다.
        public string deathCause;
        public RaidGridSnapshot inventory;
        public List<RaidEquipmentSnapshot> equipment = new List<RaidEquipmentSnapshot>();
        // 레이드 중 들어간 구역 ID를 저장해 거점의 장소 방문 임무가 씬 오브젝트를 직접 참조하지 않고 진행도를 계산합니다.
        public List<string> visitedZoneIds = new List<string>();
    }

    // 씬이 바뀌어도 가장 최근 결과를 Hideout까지 전달하기 위한 한 번용 메모리 저장소입니다.
    // HideoutStateStore가 값을 소비하고 JSON 창고에 반영한 뒤 Clear하므로 같은 보상이 두 번 지급되지 않습니다.
    public static class RaidResultStore
    {
        public static RaidResult PendingResult { get; private set; }

        // 에디터에서 도메인 리로드를 꺼도 새 Play 세션에 이전 테스트 결과가 남지 않도록 런타임 시작 전에 비웁니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeSession() => PendingResult = null;

        public static void Set(RaidResult result) => PendingResult = result;

        // Hideout이 결과를 한 번 처리한 뒤 다시 중복 지급하지 않도록 명시적으로 비웁니다.
        public static void Clear() => PendingResult = null;
    }
}
