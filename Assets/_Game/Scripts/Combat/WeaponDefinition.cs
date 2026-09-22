using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 한 번 눌렀을 때 한 발만 나가는 반자동과 버튼을 누르는 동안 계속 발사하는 자동을 구분합니다.
    // 발사 입력 방식만 나타내며 실제 발사 가능 여부와 탄약 소비는 PlayerCombat이 최종 판단합니다.
    public enum WeaponTriggerMode
    {
        SemiAutomatic,
        Automatic
    }

    // 무기와 탄약 아이템이 서로 호환되는지 비교할 때 사용하는 탄종입니다.
    // None은 총기 데이터가 아직 설정되지 않은 잘못된 상태이며, 이 상태의 무기는 발사는 가능해도 인벤토리 탄약으로 재장전할 수 없습니다.
    public enum AmmoType
    {
        None,
        NineMillimeter,
        FiveFiveSix,
        // 기존 저장 데이터의 enum 숫자가 바뀌지 않도록 새 탄종은 항상 배열 마지막에 추가합니다.
        TwelveGauge
    }

    // 피해량, 사거리와 탄창 크기처럼 같은 종류의 총기가 공유하는 고정 전투 수치를 저장합니다.
    // ItemDefinition은 인벤토리 크기와 장착 위치를 담당하고, 이 에셋은 전투 수치만 담당하도록 분리했습니다.
    // 실제 장전 탄 수는 무기 InventoryEntry에, 예비탄은 PlayerInventory의 탄약 아이템 수량으로 저장됩니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "무기";
        [SerializeField] private WeaponTriggerMode triggerMode = WeaponTriggerMode.SemiAutomatic;
        [SerializeField, Min(0.01f)] private float damage = 34f;
        [SerializeField, Min(0.1f)] private float range = 30f;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.2f;
        [SerializeField, Min(0f)] private float reloadDuration = 1.1f;
        [SerializeField, Min(1)] private int magazineSize = 8;
        [SerializeField] private AmmoType ammoType = AmmoType.NineMillimeter;
        [SerializeField, Min(1)] private int pelletCount = 1;
        [SerializeField, Range(0f, 90f)] private float spreadAngle;
        [SerializeField, Range(0f, 15f)] private float accuracySpreadAngle = 1f;
        [SerializeField, Min(0f)] private float knockbackForce = 1f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "무기" : displayName;
        public WeaponTriggerMode TriggerMode => triggerMode;
        public bool IsAutomatic => triggerMode == WeaponTriggerMode.Automatic;
        public float Damage => Mathf.Max(0.01f, damage);
        public float Range => Mathf.Max(0.1f, range);
        public float FireInterval => Mathf.Max(0.01f, fireInterval);
        public float ReloadDuration => Mathf.Max(0f, reloadDuration);
        public int MagazineSize => Mathf.Max(1, magazineSize);
        public AmmoType Caliber => ammoType;
        // 일반 총기는 1이고 산탄총은 한 번의 발사에서 나가는 작은 탄환 수를 사용합니다.
        public int PelletCount => Mathf.Max(1, pelletCount);
        // 전체 펠릿이 왼쪽 끝부터 오른쪽 끝까지 퍼지는 XZ 평면의 총 각도입니다.
        public float SpreadAngle => Mathf.Clamp(spreadAngle, 0f, 90f);
        // 조준점 중심에서 한 발 전체가 무작위로 흔들릴 수 있는 총 각도이며, 값이 작을수록 정확한 무기입니다.
        public float AccuracySpreadAngle => Mathf.Clamp(accuracySpreadAngle, 0f, 15f);
        // 명중한 대상이 IKnockbackReceiver를 구현했다면 탄환 진행 방향으로 더해 줄 밀림 속도입니다.
        public float KnockbackForce => Mathf.Max(0f, knockbackForce);
        // 모든 펠릿이 같은 대상에 맞았을 때 가능한 최대 피해량이며 상세 비교 화면에서 사용합니다.
        public float PotentialDamagePerShot => Damage * PelletCount;

        // Inspector나 에디터 도구가 잘못된 음수 값을 기록해도 에셋 자체에는 안전한 범위만 남도록 보정합니다.
        // 런타임 프로퍼티도 같은 보정을 수행하므로 오래된 에셋을 읽는 순간에도 유효한 전투 수치가 보장됩니다.
        private void OnValidate()
        {
            damage = Mathf.Max(0.01f, damage);
            range = Mathf.Max(0.1f, range);
            fireInterval = Mathf.Max(0.01f, fireInterval);
            reloadDuration = Mathf.Max(0f, reloadDuration);
            magazineSize = Mathf.Max(1, magazineSize);
            pelletCount = Mathf.Max(1, pelletCount);
            spreadAngle = Mathf.Clamp(spreadAngle, 0f, 90f);
            accuracySpreadAngle = Mathf.Clamp(accuracySpreadAngle, 0f, 15f);
            knockbackForce = Mathf.Max(0f, knockbackForce);
        }
    }
}
