using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.AI;
using ZombieSurvival.Combat;
using ZombieSurvival.Extraction;
using ZombieSurvival.Inventory;
using ZombieSurvival.Player;
using ZombieSurvival.UI;

namespace ZombieSurvival.Core
{
    // 한 번의 레이드 시작부터 종료까지를 감시하고 결과를 정확히 한 번 생성합니다.
    // 탈출 성공과 플레이어 사망이 가까운 시점에 동시에 발생해도 raidEnded 플래그가 중복 종료와 중복 씬 이동을 막습니다.
    public sealed class RaidSession : MonoBehaviour
    {
        [SerializeField] private PlayerInventory playerInventory;
        [SerializeField] private Health playerHealth;
        [SerializeField] private ExtractionController extractionController;
        [SerializeField] private RaidRules raidRules;
        [SerializeField] private RaidVisitTracker visitTracker;
        [SerializeField] private PlayerCombat playerCombat;
        [SerializeField, Min(0f)] private float resultDisplayDuration = 2f;
        [SerializeField] private string hideoutSceneName = GameSceneNavigator.HideoutSceneName;

        private readonly List<Health> trackedZombieHealth = new List<Health>();
        private readonly Dictionary<string, int> weaponKillCounts = new Dictionary<string, int>();
        private bool raidEnded;
        private float damageTaken;

        public RaidResult Result { get; private set; }
        public int ZombieKills { get; private set; }
        public bool HasEnded => raidEnded;
        public float ElapsedTime { get; private set; }
        public int RequiredZombieKills => raidRules != null ? raidRules.RequiredZombieKills : 0;
        public float MinimumExtractionTime => raidRules != null ? raidRules.MinimumExtractionTimeSeconds : 0f;
        public bool AreExtractionConditionsMet => MeetsExtractionProgress(
            ZombieKills,
            RequiredZombieKills,
            ElapsedTime,
            MinimumExtractionTime);
        public bool ReturnToHideoutFailed { get; private set; }
        public float DamageTaken => damageTaken;

        // 자동 생성기가 private SerializeField 참조를 안전하게 연결할 수 있도록 명시적인 설정 메서드를 제공합니다.
        // 런타임 Start의 자동 탐색보다 정확한 참조를 우선 사용하며 테스트 씬에서는 일부 값을 null로 둘 수 있습니다.
        public void Configure(
            PlayerInventory inventory,
            Health health,
            ExtractionController extraction,
            RaidRules rules = null)
        {
            playerInventory = inventory;
            playerHealth = health;
            extractionController = extraction;
            raidRules = rules;
            extractionController?.SetRaidSession(this);
        }

        // 방문 추적은 기본 전투 종료 규칙과 독립된 선택 기능이므로 별도 설정 함수로 연결합니다.
        // 기존 테스트와 작은 씬은 이 함수를 호출하지 않아도 이전처럼 정상 종료됩니다.
        public void SetVisitTracker(RaidVisitTracker tracker)
        {
            visitTracker = tracker;
        }

        private void Start()
        {
            // 생성기 참조가 누락된 테스트 씬에서도 동작하도록 필요한 컴포넌트를 한 번 자동 탐색합니다.
            // FindObjectOfType은 비용이 있으므로 Start에서 한 번만 호출하고 매 프레임 반복하지 않습니다.
            if (playerInventory == null) playerInventory = FindObjectOfType<PlayerInventory>();
            if (playerHealth == null && playerInventory != null) playerHealth = playerInventory.GetComponent<Health>();
            if (playerCombat == null && playerInventory != null) playerCombat = playerInventory.GetComponent<PlayerCombat>();
            if (extractionController == null) extractionController = FindObjectOfType<ExtractionController>();
            if (visitTracker == null) visitTracker = GetComponent<RaidVisitTracker>();
            extractionController?.SetRaidSession(this);

            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDeath;
                playerHealth.DamageResolved += HandlePlayerDamage;
            }
            if (extractionController != null) extractionController.ExtractionSucceeded += HandleExtractionSuccess;

            // 현재 씬에 미리 배치된 좀비 Health에 사망 이벤트를 연결해 처치 수를 계산합니다.
            // 레이드 도중 생성되는 좀비는 ZombieSpawner가 아래 RegisterZombie를 호출해 같은 목록에 추가합니다.
            foreach (ZombieBrain zombie in FindObjectsOfType<ZombieBrain>())
            {
                Health health = zombie.GetComponent<Health>();
                RegisterZombie(health);
            }
        }

        // 레이드 도중 ZombieSpawner가 만든 좀비도 처치 수에 포함하도록 사망 이벤트를 동적으로 등록합니다.
        // Start 실행 순서 때문에 같은 좀비가 두 번 전달될 수 있으므로 목록에 이미 있으면 다시 구독하지 않습니다.
        public void RegisterZombie(Health zombieHealth)
        {
            if (zombieHealth == null || zombieHealth.IsDead || trackedZombieHealth.Contains(zombieHealth)) return;
            zombieHealth.DiedDetailed += HandleZombieDeath;
            trackedZombieHealth.Add(zombieHealth);
        }

        private void Update()
        {
            if (raidEnded) return;

            // 제한 시간 실패는 없지만 결과 화면과 최소 탈출 시간에 사용할 실제 레이드 경과 시간은 계속 기록합니다.
            // 인벤토리는 timeScale을 바꾸지 않아 시간이 흐르고, ESC 일시정지는 timeScale이 0이므로 경과 시간도 함께 멈춥니다.
            ElapsedTime += Mathf.Max(0f, Time.deltaTime);
        }

        private void OnDestroy()
        {
            // 이벤트 발행자가 세션보다 오래 살아남을 수 있으므로 파괴 시 구독을 반드시 해제합니다.
            // 해제하지 않으면 이전 씬의 메서드가 다음 씬에서 호출되거나 객체가 메모리에 남는 원인이 됩니다.
            if (playerHealth != null)
            {
                playerHealth.Died -= HandlePlayerDeath;
                playerHealth.DamageResolved -= HandlePlayerDamage;
            }
            if (extractionController != null) extractionController.ExtractionSucceeded -= HandleExtractionSuccess;
            foreach (Health health in trackedZombieHealth)
                if (health != null) health.DiedDetailed -= HandleZombieDeath;
        }

        private void HandleZombieDeath(Health _, DamageInfo fatalDamage)
        {
            // 탈출 결과가 확정된 뒤 쓰러진 좀비는 이미 저장된 결과와 HUD 숫자를 바꾸면 안 됩니다.
            if (raidEnded) return;
            ZombieKills++;
            string sourceId = string.IsNullOrWhiteSpace(fatalDamage.SourceId) ? "unknown" : fatalDamage.SourceId;
            weaponKillCounts[sourceId] = weaponKillCounts.TryGetValue(sourceId, out int count) ? count + 1 : 1;
        }
        private void HandlePlayerDamage(DamageInfo damageInfo)
        {
            if (!raidEnded) damageTaken += Mathf.Max(0f, damageInfo.Amount);
        }
        private void HandleExtractionSuccess() => FinishRaid(RaidEndReason.Extracted);
        private void HandlePlayerDeath() => FinishRaid(RaidEndReason.PlayerDied);

        private void FinishRaid(RaidEndReason reason)
        {
            if (raidEnded) return;
            raidEnded = true;

            // 플레이어 오브젝트가 사망으로 Destroy되기 전에 동기적으로 전체 인벤토리 스냅샷을 생성합니다.
            // 탈출은 회수 스냅샷을 남기고, 사망은 lostItemCount를 기록한 뒤 실제 인벤토리를 비웁니다.
            Result = RaidResultFactory.Create(
                playerInventory,
                reason,
                ZombieKills,
                ElapsedTime,
                visitTracker != null ? visitTracker.CaptureVisitedZoneIds() : null,
                // Health는 Died 이벤트를 호출하기 직전에 원인을 확정하므로 여기서 파괴 전 문자열을 안전하게 복사할 수 있습니다.
                playerHealth != null ? playerHealth.DeathCauseDescription : string.Empty,
                playerCombat != null ? playerCombat.ShotsFired : 0,
                playerCombat != null ? playerCombat.SuccessfulShots : 0,
                damageTaken,
                CaptureWeaponKillStats());
            RaidResultStore.Set(Result);

            // 손실 수량을 먼저 계산한 뒤 비워야 결과 화면에 정확한 숫자를 표시할 수 있습니다.
            // 시간 초과 종료는 없지만 사망과 직접 포기는 모두 실패이므로 두 경우 모두 레이드 아이템을 제거합니다.
            if (!Result.succeeded && playerInventory != null) playerInventory.DiscardAllRaidItems();
            DisablePlayerControls();
            DisableZombieActions();
            StartCoroutine(ReturnToHideoutRoutine());
        }

        private List<RaidWeaponKillStat> CaptureWeaponKillStats()
        {
            var result = new List<RaidWeaponKillStat>();
            foreach (KeyValuePair<string, int> pair in weaponKillCounts)
                if (pair.Value > 0)
                    result.Add(new RaidWeaponKillStat { weaponItemId = pair.Key, kills = pair.Value });
            return result;
        }

        // 현재 처치 수와 경과 시간이 에셋에 지정된 두 추가 조건을 모두 만족하는지 반환합니다.
        // 순수 함수로 두어 정확히 요구 수와 최소 시간 경계에서도 탈출 가능한지 자동 테스트합니다.
        public static bool MeetsExtractionProgress(
            int zombieKills,
            int requiredZombieKills,
            float elapsedTime,
            float minimumExtractionTime)
        {
            return zombieKills >= Mathf.Max(0, requiredZombieKills) &&
                   elapsedTime >= Mathf.Max(0f, minimumExtractionTime);
        }

        private void DisablePlayerControls()
        {
            // 결과 화면 중 추가 이동, 조준, 발사와 루팅을 막아 저장된 스냅샷과 화면 상태가 달라지지 않게 합니다.
            // 인벤토리가 열린 상태라면 먼저 닫고, PlayerControlGate에는 RaidSession 자신의 잠금을 남겨 씬 전환까지 조작을 막습니다.
            InventoryHud inventoryHud = FindObjectOfType<InventoryHud>();
            if (inventoryHud != null) inventoryHud.SetOpen(false);

            // 참조가 일부 빠진 테스트 씬에서는 Health가 붙은 플레이어 오브젝트를 예비 기준으로 사용합니다.
            // 둘 다 없으면 결과와 씬 복귀는 계속 진행하고 조작 잠금만 건너뜁니다.
            GameObject playerObject = playerInventory != null
                ? playerInventory.gameObject
                : playerHealth != null ? playerHealth.gameObject : null;
            if (playerObject == null) return;

            PlayerControlGate controlGate = playerObject.GetComponent<PlayerControlGate>();
            if (controlGate != null)
            {
                controlGate.AcquireLock(this);
                return;
            }

            // 이전 프리팹처럼 Gate가 없는 씬에서도 레이드 종료가 멈추지 않도록 기존 직접 비활성 방식을 예비 경로로 유지합니다.
            PlayerMovement movement = playerObject.GetComponent<PlayerMovement>();
            PlayerCombat combat = playerObject.GetComponent<PlayerCombat>();
            PlayerInteraction interaction = playerObject.GetComponent<PlayerInteraction>();
            if (movement != null) movement.enabled = false;
            if (combat != null) combat.enabled = false;
            if (interaction != null) interaction.enabled = false;
        }

        private void DisableZombieActions()
        {
            // 결과 화면을 보여 주는 동안 좀비가 계속 이동하거나 성공한 플레이어를 죽이면 화면 상태와 실제 월드가 어긋납니다.
            // 먼저 생성기를 끈 다음, 현재 레이드가 추적한 살아 있는 좀비의 판단·이동·공격만 멈춥니다.
            // 이 순서로 처리하면 결과 화면이 열린 2초 동안 새 좀비가 나타나는 일도 없고, Health와 시체 생성 결과는 그대로 유지됩니다.
            foreach (ZombieSpawner spawner in FindObjectsOfType<ZombieSpawner>())
                if (spawner != null) spawner.enabled = false;

            foreach (Health health in trackedZombieHealth)
            {
                if (health == null || health.IsDead) continue;
                ZombieBrain brain = health.GetComponent<ZombieBrain>();
                ZombieMovement movement = health.GetComponent<ZombieMovement>();
                ZombieAttack attack = health.GetComponent<ZombieAttack>();
                if (brain != null) brain.enabled = false;
                if (movement != null) movement.enabled = false;
                if (attack != null) attack.enabled = false;
            }
        }

        // 자동 복귀가 실패한 경우 결과 HUD나 향후 재시도 버튼이 같은 전환 경로를 다시 호출할 수 있게 합니다.
        public bool TryReturnToHideout()
        {
            if (!raidEnded) return false;
            // 레이드 종료 뒤에도 Loading 화면을 거쳐 거점 씬과 저장 상태가 준비되는 과정을 보여 줍니다.
            ReturnToHideoutFailed = !GameSceneNavigator.TryLoadSceneThroughLoading(hideoutSceneName);
            return !ReturnToHideoutFailed;
        }

        // 일시정지 메뉴에서 레이드 포기를 확정하면 사망과 같은 손실 규칙으로 결과를 즉시 확정합니다.
        // FinishRaid의 raidEnded 검사 덕분에 사망이나 탈출과 같은 프레임에 눌려도 결과가 두 번 생성되지 않습니다.
        public bool TryAbandonRaid()
        {
            if (raidEnded) return false;
            FinishRaid(RaidEndReason.Abandoned);
            return true;
        }

        private IEnumerator ReturnToHideoutRoutine()
        {
            // Time.timeScale의 영향을 받지 않는 실시간 대기를 사용해 일시정지 상태에서도 결과 화면 뒤 전환이 진행됩니다.
            yield return new WaitForSecondsRealtime(resultDisplayDuration);
            // Build Settings 누락 시 예외로 레이드 결과가 사라지지 않도록 공용 전환기가 씬 존재 여부를 먼저 검사합니다.
            TryReturnToHideout();
        }
    }
}
