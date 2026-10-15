using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 한 무기에 동시에 하나씩 장착할 수 있는 부착물 위치입니다.
    // 같은 위치의 새 부착물을 장착하면 기존 부착물은 인벤토리로 돌아갑니다.
    public enum WeaponAttachmentSlot
    {
        None,
        Optic,
        Muzzle,
        Magazine
    }

    // 조준경·총구·확장 탄창이 무기 성능에 주는 보정값을 저장하는 ScriptableObject입니다.
    // 부착물 아이템의 격자 크기와 가격은 ItemDefinition에 두고, 실제 전투 수치만 이 데이터에 분리합니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Weapon Attachment Definition")]
    public sealed class WeaponAttachmentDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "무기 부착물";
        [SerializeField] private WeaponAttachmentSlot slot = WeaponAttachmentSlot.Optic;
        // None이면 모든 총기에 호환되고, 탄종이 지정되면 같은 탄종 무기에만 장착할 수 있습니다.
        [SerializeField] private AmmoType compatibleCaliber = AmmoType.None;
        [SerializeField, Range(0.5f, 2f)] private float damageMultiplier = 1f;
        [SerializeField, Range(0.5f, 2f)] private float fireIntervalMultiplier = 1f;
        [SerializeField, Range(0.25f, 2f)] private float accuracySpreadMultiplier = 1f;
        [SerializeField, Range(0.5f, 2f)] private float reloadDurationMultiplier = 1f;
        [SerializeField, Range(0, 60)] private int magazineCapacityBonus;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public WeaponAttachmentSlot Slot => slot;
        public AmmoType CompatibleCaliber => compatibleCaliber;
        public float DamageMultiplier => Mathf.Clamp(damageMultiplier, 0.5f, 2f);
        public float FireIntervalMultiplier => Mathf.Clamp(fireIntervalMultiplier, 0.5f, 2f);
        public float AccuracySpreadMultiplier => Mathf.Clamp(accuracySpreadMultiplier, 0.25f, 2f);
        public float ReloadDurationMultiplier => Mathf.Clamp(reloadDurationMultiplier, 0.5f, 2f);
        public int MagazineCapacityBonus => Mathf.Clamp(magazineCapacityBonus, 0, 60);

        public bool IsCompatible(WeaponDefinition weapon)
        {
            return weapon != null && slot != WeaponAttachmentSlot.None &&
                   (compatibleCaliber == AmmoType.None || weapon.Caliber == compatibleCaliber);
        }

        // 프로토타입 생성기와 EditMode 테스트가 private 직렬화 필드를 직접 만지지 않고 완전한 데이터를 만들 때 사용합니다.
        public void Configure(
            string newDisplayName,
            WeaponAttachmentSlot newSlot,
            AmmoType caliber,
            float newDamageMultiplier,
            float newFireIntervalMultiplier,
            float newAccuracySpreadMultiplier,
            float newReloadDurationMultiplier,
            int newMagazineCapacityBonus)
        {
            displayName = newDisplayName ?? string.Empty;
            slot = newSlot;
            compatibleCaliber = caliber;
            damageMultiplier = Mathf.Clamp(newDamageMultiplier, 0.5f, 2f);
            fireIntervalMultiplier = Mathf.Clamp(newFireIntervalMultiplier, 0.5f, 2f);
            accuracySpreadMultiplier = Mathf.Clamp(newAccuracySpreadMultiplier, 0.25f, 2f);
            reloadDurationMultiplier = Mathf.Clamp(newReloadDurationMultiplier, 0.5f, 2f);
            magazineCapacityBonus = Mathf.Clamp(newMagazineCapacityBonus, 0, 60);
        }

        private void OnValidate()
        {
            damageMultiplier = Mathf.Clamp(damageMultiplier, 0.5f, 2f);
            fireIntervalMultiplier = Mathf.Clamp(fireIntervalMultiplier, 0.5f, 2f);
            accuracySpreadMultiplier = Mathf.Clamp(accuracySpreadMultiplier, 0.25f, 2f);
            reloadDurationMultiplier = Mathf.Clamp(reloadDurationMultiplier, 0.5f, 2f);
            magazineCapacityBonus = Mathf.Clamp(magazineCapacityBonus, 0, 60);
        }
    }
}
