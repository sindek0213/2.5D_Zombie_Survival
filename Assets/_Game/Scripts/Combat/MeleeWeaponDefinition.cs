using UnityEngine;

namespace ZombieSurvival.Combat
{
    // 근접 공격이 화면과 판정에서 어떤 성격을 가지는지 구분합니다.
    // 현재 전투 코드는 두 방식 모두 같은 공통 판정을 사용하지만, 나중에 찌르기·휘두르기 전용 애니메이션과 소리를 연결할 때 이 값을 그대로 사용할 수 있습니다.
    public enum MeleeAttackStyle
    {
        Stab,
        Swing
    }

    // 단검·도끼처럼 같은 종류의 근접무기가 공유하는 전투 수치를 저장하는 ScriptableObject입니다.
    // ItemDefinition에는 이름과 인벤토리 크기만 두고 전투 수치를 이 에셋으로 분리해, 새 무기를 추가할 때 PlayerCombat 코드를 수정하지 않게 합니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Melee Weapon Definition")]
    public sealed class MeleeWeaponDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "근접무기";
        [SerializeField] private MeleeAttackStyle attackStyle = MeleeAttackStyle.Swing;

        [Header("공격 성능")]
        // damage는 한 대상에게 적용할 기본 피해량입니다.
        [SerializeField, Min(0.01f)] private float damage = 35f;
        // range는 플레이어 중심에서 앞으로 공격 판정을 뻗는 거리입니다.
        [SerializeField, Min(0.1f)] private float range = 1.8f;
        // hitRadius는 공격 경로 좌우의 판정 폭입니다. 찌르기는 작게, 휘두르기는 크게 설정하면 됩니다.
        [SerializeField, Min(0.05f)] private float hitRadius = 0.45f;
        // attackInterval은 다음 공격을 시작할 수 있을 때까지 기다리는 시간입니다. 값이 작을수록 빠르게 공격합니다.
        [SerializeField, Min(0.05f)] private float attackInterval = 0.65f;
        // maximumTargets가 1이면 처음 맞은 대상에서 끝나고, 2 이상이면 한 번의 휘두르기로 가까운 여러 대상을 공격할 수 있습니다.
        [SerializeField, Min(1)] private int maximumTargets = 1;

        [Header("충격과 소음")]
        // knockbackForce는 IKnockbackReceiver를 구현한 적을 조준 방향으로 밀어내는 세기입니다.
        [SerializeField, Min(0f)] private float knockbackForce = 2f;
        // armorPenetration은 대상 방어 등급과 비교되는 값이며 도끼처럼 무거운 무기에 더 높은 값을 줄 수 있습니다.
        [SerializeField, Range(0, 5)] private int armorPenetration;
        // noiseRadius는 공격 순간 주변 좀비가 소리를 들을 수 있는 거리입니다. 0이면 소음을 만들지 않습니다.
        [SerializeField, Min(0f)] private float noiseRadius = 4f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "근접무기" : displayName;
        public MeleeAttackStyle AttackStyle => attackStyle;
        public float Damage => Mathf.Max(0.01f, damage);
        public float Range => Mathf.Max(0.1f, range);
        public float HitRadius => Mathf.Max(0.05f, hitRadius);
        public float AttackInterval => Mathf.Max(0.05f, attackInterval);
        public int MaximumTargets => Mathf.Max(1, maximumTargets);
        public float KnockbackForce => Mathf.Max(0f, knockbackForce);
        public int ArmorPenetration => Mathf.Clamp(armorPenetration, 0, 5);
        public float NoiseRadius => Mathf.Max(0f, noiseRadius);

        // 에디터 생성 도구와 자동 테스트가 Inspector를 우회해도 같은 검증 규칙으로 데이터를 만들 수 있게 합니다.
        public void Configure(
            string weaponName,
            MeleeAttackStyle style,
            float attackDamage,
            float attackRange,
            float attackHitRadius,
            float interval,
            int targetCount,
            float knockback,
            int penetration,
            float audibleRadius)
        {
            displayName = weaponName;
            attackStyle = style;
            damage = Mathf.Max(0.01f, attackDamage);
            range = Mathf.Max(0.1f, attackRange);
            hitRadius = Mathf.Max(0.05f, attackHitRadius);
            attackInterval = Mathf.Max(0.05f, interval);
            maximumTargets = Mathf.Max(1, targetCount);
            knockbackForce = Mathf.Max(0f, knockback);
            armorPenetration = Mathf.Clamp(penetration, 0, 5);
            noiseRadius = Mathf.Max(0f, audibleRadius);
        }

        // Inspector에서 잘못된 음수나 0을 입력해도 런타임에 공격 불가능한 에셋이 남지 않도록 저장 직전에 보정합니다.
        private void OnValidate()
        {
            damage = Mathf.Max(0.01f, damage);
            range = Mathf.Max(0.1f, range);
            hitRadius = Mathf.Max(0.05f, hitRadius);
            attackInterval = Mathf.Max(0.05f, attackInterval);
            maximumTargets = Mathf.Max(1, maximumTargets);
            knockbackForce = Mathf.Max(0f, knockbackForce);
            armorPenetration = Mathf.Clamp(armorPenetration, 0, 5);
            noiseRadius = Mathf.Max(0f, noiseRadius);
        }
    }
}
