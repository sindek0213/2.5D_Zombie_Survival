using System;
using UnityEngine;
using ZombieSurvival.Combat;

namespace ZombieSurvival.AI
{
    public enum ZombieBehaviorTrait
    {
        Standard,
        Runner,
        Brute,
        Screamer
    }

    // 좀비 한 종류의 밸런스와 전리품을 모아 두는 ScriptableObject 데이터입니다.
    // 일반·빠른·튼튼한 좀비를 새로 만들 때 코드를 복사하지 않고 이 에셋의 숫자만 다르게 설정할 수 있습니다.
    [CreateAssetMenu(menuName = "Zombie Survival/Zombie Archetype")]
    public sealed class ZombieArchetype : ScriptableObject
    {
        [SerializeField] private string displayName = "일반 좀비";
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0f)] private float spawnWeight = 1f;
        [SerializeField, Min(0f)] private float unlockTimeSeconds;
        [SerializeField, Min(1f)] private float maximumHealth = 90f;
        [SerializeField, Min(0f)] private float movementSpeed = 2.35f;
        [SerializeField, Min(0f)] private float attackDamage = 12f;
        [SerializeField] private Color visualColor = new Color(0.48f, 0.72f, 0.36f, 1f);
        [SerializeField] private ZombieBehaviorTrait behaviorTrait;
        [SerializeField, Min(0f)] private float detectionRange = 9f;
        [SerializeField, Min(0f)] private float hearingRange = 18f;
        [SerializeField, Min(0f)] private float loseTargetRange = 13f;
        [SerializeField, Min(0.1f)] private float investigationDuration = 4f;
        [SerializeField] private ZombieLootEntry[] lootEntries = Array.Empty<ZombieLootEntry>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public GameObject Prefab => prefab;
        public float SpawnWeight => Mathf.Max(0f, spawnWeight);
        public float UnlockTimeSeconds => Mathf.Max(0f, unlockTimeSeconds);
        public float MaximumHealth => Mathf.Max(1f, maximumHealth);
        public float MovementSpeed => Mathf.Max(0f, movementSpeed);
        public float AttackDamage => Mathf.Max(0f, attackDamage);
        public Color VisualColor => visualColor;
        public ZombieBehaviorTrait BehaviorTrait => behaviorTrait;
        public float DetectionRange => Mathf.Max(0f, detectionRange);
        public float HearingRange => Mathf.Max(0f, hearingRange);
        public float LoseTargetRange => Mathf.Max(DetectionRange, loseTargetRange);
        public float InvestigationDuration => Mathf.Max(0.1f, investigationDuration);
        public ZombieLootEntry[] LootEntries => lootEntries;

        // 레이드가 시작된 뒤 설정된 시간이 지나야 이 종류를 무작위 후보에 포함합니다.
        // 예를 들어 unlockTimeSeconds가 60이면 시작 후 첫 60초 동안은 이 좀비가 나타나지 않습니다.
        public bool IsUnlocked(float elapsedTime)
        {
            return Mathf.Max(0f, elapsedTime) >= UnlockTimeSeconds;
        }

        // 에디터 자동 생성기가 기본 좀비 데이터 에셋을 만들 때 모든 값을 한곳에서 기록합니다.
        // 전달된 전리품 배열이 null이면 빈 배열로 바꿔 스폰 직후 null 예외가 생기지 않게 합니다.
        public void Configure(
            string newDisplayName,
            GameObject newPrefab,
            float newSpawnWeight,
            float newUnlockTimeSeconds,
            float newMaximumHealth,
            float newMovementSpeed,
            float newAttackDamage,
            Color newVisualColor,
            params ZombieLootEntry[] newLootEntries)
        {
            displayName = newDisplayName;
            prefab = newPrefab;
            spawnWeight = Mathf.Max(0f, newSpawnWeight);
            unlockTimeSeconds = Mathf.Max(0f, newUnlockTimeSeconds);
            maximumHealth = Mathf.Max(1f, newMaximumHealth);
            movementSpeed = Mathf.Max(0f, newMovementSpeed);
            attackDamage = Mathf.Max(0f, newAttackDamage);
            visualColor = newVisualColor;
            lootEntries = newLootEntries ?? Array.Empty<ZombieLootEntry>();
        }

        // 종류별 감지·청각과 특수 행동은 체력·속도 설정과 별도로 바꿀 수 있게 분리했습니다.
        // 기존 생성 코드와 테스트는 원래 Configure만 사용해도 안전한 기본 감지값을 그대로 유지합니다.
        public void ConfigureBehavior(
            ZombieBehaviorTrait trait,
            float newDetectionRange,
            float newHearingRange,
            float newLoseTargetRange,
            float newInvestigationDuration)
        {
            behaviorTrait = trait;
            detectionRange = Mathf.Max(0f, newDetectionRange);
            hearingRange = Mathf.Max(0f, newHearingRange);
            loseTargetRange = Mathf.Max(detectionRange, newLoseTargetRange);
            investigationDuration = Mathf.Max(0.1f, newInvestigationDuration);
        }

        // 공용 좀비 프리팹을 복제한 뒤 이 종류만의 체력, 이동 속도, 공격력과 전리품을 적용합니다.
        // 각 기능의 실제 실행은 기존 컴포넌트가 계속 담당하고, 이 데이터는 숫자를 전달하는 역할만 합니다.
        public bool ApplyTo(GameObject zombie)
        {
            if (zombie == null) return false;

            Health health = zombie.GetComponent<Health>();
            ZombieBrain brain = zombie.GetComponent<ZombieBrain>();
            ZombieMovement movement = zombie.GetComponent<ZombieMovement>();
            ZombieAttack attack = zombie.GetComponent<ZombieAttack>();
            ZombieLootDrop lootDrop = zombie.GetComponent<ZombieLootDrop>();
            // 상태 판단 코드가 빠지면 체력과 이동 컴포넌트가 있어도 실제로는 아무 행동도 하지 않는 반쪽 좀비가 됩니다.
            // 스포너가 그런 프리팹을 즉시 제거할 수 있도록 필수 실행 컴포넌트를 모두 한 번에 검증합니다.
            if (health == null || brain == null || movement == null || attack == null || lootDrop == null) return false;

            health.ConfigureMaximumHealth(MaximumHealth, true);
            movement.ConfigureSpeed(MovementSpeed);
            attack.ConfigureDamage(AttackDamage);
            // 에셋 파일 이름(walker, runner 등)을 지역 몬스터 드랍 표의 sourceId로 전달합니다.
            // 지역 프로필이 없는 씬에서는 두 번째 인자의 기존 lootEntries가 그대로 사용됩니다.
            lootDrop.Configure(name, lootEntries);
            brain.ConfigureSenses(
                DetectionRange,
                HearingRange,
                LoseTargetRange,
                InvestigationDuration,
                BehaviorTrait);

            // 종류마다 다른 색을 적용하고 DamageFlash에도 새 기본색을 알려 피격 점멸 뒤 초록색으로 잘못 돌아가지 않게 합니다.
            DamageFlash damageFlash = zombie.GetComponent<DamageFlash>();
            if (damageFlash != null)
            {
                damageFlash.ConfigureBaseColor(VisualColor);
            }
            else
            {
                SpriteRenderer renderer = zombie.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null) renderer.color = VisualColor;
            }
            return true;
        }

        // Inspector에서 음수 값을 직접 입력해도 실제 게임 데이터가 잘못되지 않도록 즉시 안전 범위로 고칩니다.
        private void OnValidate()
        {
            spawnWeight = Mathf.Max(0f, spawnWeight);
            unlockTimeSeconds = Mathf.Max(0f, unlockTimeSeconds);
            maximumHealth = Mathf.Max(1f, maximumHealth);
            movementSpeed = Mathf.Max(0f, movementSpeed);
            attackDamage = Mathf.Max(0f, attackDamage);
            detectionRange = Mathf.Max(0f, detectionRange);
            hearingRange = Mathf.Max(0f, hearingRange);
            loseTargetRange = Mathf.Max(detectionRange, loseTargetRange);
            investigationDuration = Mathf.Max(0.1f, investigationDuration);
            if (lootEntries == null) lootEntries = Array.Empty<ZombieLootEntry>();
        }
    }
}
