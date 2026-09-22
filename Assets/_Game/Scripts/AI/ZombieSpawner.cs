using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Core;
using ZombieSurvival.Player;

namespace ZombieSurvival.AI
{
    // 레이드 안의 살아 있는 좀비 수를 관리하고, 빈자리가 생기면 지정된 지점에서 새 좀비를 생성합니다.
    // 시간이 흐를수록 최대 생존 수는 늘고 생성 간격은 줄어들어 레이드 후반이 자연스럽게 어려워집니다.
    // 초기 배치 좀비의 Start보다 먼저 실행해 모든 좀비에 같은 플레이어 참조를 전달하고 반복적인 씬 검색을 피합니다.
    [DefaultExecutionOrder(-200)]
    public sealed class ZombieSpawner : MonoBehaviour
    {
        [SerializeField] private ZombieArchetype[] archetypes = Array.Empty<ZombieArchetype>();
        [SerializeField] private ZombieSpawnPoint[] spawnPoints = Array.Empty<ZombieSpawnPoint>();
        [SerializeField] private Transform player;
        [SerializeField] private RaidSession raidSession;
        // 처음에는 맵에 세 마리만 유지하고 200초 동안 최대 일곱 마리까지 늘립니다.
        // 생성 위치는 플레이어와 최소 10m 떨어뜨려 화면 바로 앞에서 갑자기 나타나는 상황을 줄입니다.
        [SerializeField, Min(0)] private int startingAliveLimit = 3;
        [SerializeField, Min(0)] private int endingAliveLimit = 7;
        [SerializeField, Min(0.05f)] private float startingSpawnInterval = 9f;
        [SerializeField, Min(0.05f)] private float endingSpawnInterval = 4f;
        [SerializeField, Min(0.05f)] private float difficultyRampSeconds = 200f;
        [SerializeField, Min(0f)] private float minimumPlayerDistance = 10f;
        [SerializeField, Min(0f)] private float firstSpawnDelay = 6f;

        private readonly List<Health> livingZombies = new List<Health>();
        private float startedAt;
        private float nextSpawnAt;
        private int spawnedSerial;

        public int LivingZombieCount => livingZombies.Count;

        // 자동 생성기와 수동 테스트가 private Inspector 필드를 안전하게 연결할 수 있도록 제공하는 설정 메서드입니다.
        // 배열이 null이면 빈 배열로 바꿔 Update에서 매 프레임 null 확인을 반복하지 않게 합니다.
        public void Configure(
            ZombieArchetype[] newArchetypes,
            ZombieSpawnPoint[] newSpawnPoints,
            Transform playerTransform,
            RaidSession session,
            int startLimit = 3,
            int endLimit = 7,
            float startInterval = 9f,
            float endInterval = 4f,
            float rampSeconds = 200f,
            float minPlayerDistance = 10f,
            float initialDelay = 6f)
        {
            archetypes = newArchetypes ?? Array.Empty<ZombieArchetype>();
            spawnPoints = newSpawnPoints ?? Array.Empty<ZombieSpawnPoint>();
            player = playerTransform;
            raidSession = session;
            startingAliveLimit = Mathf.Max(0, startLimit);
            endingAliveLimit = Mathf.Max(0, endLimit);
            startingSpawnInterval = Mathf.Max(0.05f, startInterval);
            endingSpawnInterval = Mathf.Max(0.05f, endInterval);
            difficultyRampSeconds = Mathf.Max(0.05f, rampSeconds);
            minimumPlayerDistance = Mathf.Max(0f, minPlayerDistance);
            firstSpawnDelay = Mathf.Max(0f, initialDelay);
        }

        private void Start()
        {
            // 씬에서 참조를 빠뜨린 경우에도 프로토타입이 동작하도록 시작할 때 한 번만 자동 탐색합니다.
            // Find 계열 호출은 비용이 있으므로 Update에서 반복하지 않고 찾은 결과를 필드에 저장합니다.
            if (player == null)
            {
                PlayerMovement movement = FindObjectOfType<PlayerMovement>();
                player = movement != null ? movement.transform : null;
            }
            if (raidSession == null) raidSession = FindObjectOfType<RaidSession>();
            if (spawnPoints == null || spawnPoints.Length == 0)
                spawnPoints = GetComponentsInChildren<ZombieSpawnPoint>(true);

            // 씬에 처음부터 배치된 좀비도 생존 제한과 처치 수에 포함해야 새 좀비가 겹쳐서 과도하게 생성되지 않습니다.
            // RaidSession.RegisterZombie는 중복 등록을 막으므로 두 컴포넌트의 Start 순서가 달라도 처치 수가 두 번 오르지 않습니다.
            foreach (ZombieBrain zombie in FindObjectsOfType<ZombieBrain>())
            {
                if (zombie == null) continue;
                zombie.SetTarget(player);
                TrackLivingZombie(zombie.GetComponent<Health>());
            }

            startedAt = Time.time;
            nextSpawnAt = Time.time + firstSpawnDelay;
        }

        private void Update()
        {
            if (raidSession != null && raidSession.HasEnded) return;

            RemoveDeadZombies();
            float elapsedTime = raidSession != null
                ? raidSession.ElapsedTime
                : Mathf.Max(0f, Time.time - startedAt);
            float progress = CalculateDifficultyProgress(elapsedTime, difficultyRampSeconds);
            int aliveLimit = CalculateAliveLimit(startingAliveLimit, endingAliveLimit, progress);
            if (Time.time < nextSpawnAt) return;

            // 생존 제한이 가득 찬 동안에도 다음 검사 시각을 계속 갱신합니다.
            // 따라서 한 마리가 죽었다고 즉시 대체하지 않고 현재 난이도에 맞는 생성 주기를 유지합니다.
            nextSpawnAt = Time.time + CalculateSpawnInterval(
                startingSpawnInterval,
                endingSpawnInterval,
                progress);
            if (livingZombies.Count >= aliveLimit) return;

            // 후보가 없거나 모든 지점이 막혀 실패해도 위에서 다음 시각을 정했으므로 매 프레임 물리 검사를 반복하지 않습니다.
            TrySpawnOne(elapsedTime);
        }

        private bool TrySpawnOne(float elapsedTime)
        {
            int archetypeIndex = SelectWeightedArchetypeIndex(archetypes, elapsedTime, UnityEngine.Random.value);
            if (archetypeIndex < 0 || !TrySelectSpawnPoint(out ZombieSpawnPoint spawnPoint)) return false;

            ZombieArchetype archetype = archetypes[archetypeIndex];
            if (archetype == null || archetype.Prefab == null) return false;
            GameObject zombie = Instantiate(archetype.Prefab, spawnPoint.Position, spawnPoint.Rotation);
            zombie.name = archetype.DisplayName + "_Spawned_" + (++spawnedSerial).ToString("D3");
            if (!archetype.ApplyTo(zombie))
            {
                // 필수 컴포넌트가 빠진 잘못된 프리팹은 월드에 반쪽짜리 적으로 남기지 않고 즉시 제거합니다.
                Destroy(zombie);
                return false;
            }

            // Instantiate 직후에는 ZombieBrain.Start가 아직 호출되지 않았으므로 스포너가 가진 플레이어를 바로 전달할 수 있습니다.
            // 이후 추적은 목적지 계산만 사용하며 새 좀비마다 FindObjectOfType를 실행하지 않습니다.
            zombie.GetComponent<ZombieBrain>().SetTarget(player);
            TrackLivingZombie(zombie.GetComponent<Health>());
            return true;
        }

        private void TrackLivingZombie(Health health)
        {
            if (health == null || health.IsDead || livingZombies.Contains(health)) return;
            livingZombies.Add(health);
            raidSession?.RegisterZombie(health);
        }

        private void RemoveDeadZombies()
        {
            // Health는 사망 프레임 끝에 오브젝트를 제거하므로 null뿐 아니라 IsDead도 검사해 같은 프레임에 빈자리를 인식합니다.
            for (int i = livingZombies.Count - 1; i >= 0; i--)
            {
                Health health = livingZombies[i];
                if (health == null || health.IsDead) livingZombies.RemoveAt(i);
            }
        }

        private bool TrySelectSpawnPoint(out ZombieSpawnPoint selected)
        {
            selected = null;
            int validCount = 0;
            if (spawnPoints == null) return false;

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                ZombieSpawnPoint candidate = spawnPoints[i];
                if (candidate == null || !IsFarEnoughFromPlayer(candidate.Position) || !IsSpawnAreaClear(candidate.Position))
                    continue;

                // Reservoir sampling 방식이라 유효 지점 수를 미리 세지 않아도 모든 후보가 같은 확률로 선택됩니다.
                validCount++;
                if (UnityEngine.Random.Range(0, validCount) == 0) selected = candidate;
            }

            return selected != null;
        }

        private bool IsFarEnoughFromPlayer(Vector3 position)
        {
            if (player == null) return true;
            Vector3 offset = position - player.position;
            offset.y = 0f;
            return offset.sqrMagnitude >= minimumPlayerDistance * minimumPlayerDistance;
        }

        private static bool IsSpawnAreaClear(Vector3 position)
        {
            // 지면은 피하면서 사람 크기 공간만 검사해 벽, 상자 또는 다른 좀비 안에 생성되는 일을 막습니다.
            Vector3 bottom = position + Vector3.up * 0.4f;
            Vector3 top = position + Vector3.up * 1.3f;
            return !Physics.CheckCapsule(bottom, top, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        // 경과 시간을 0~1 난이도 비율로 바꿉니다. rampSeconds가 지나면 1에 고정되어 난이도가 무한히 증가하지 않습니다.
        public static float CalculateDifficultyProgress(float elapsedTime, float rampSeconds)
        {
            return Mathf.Clamp01(Mathf.Max(0f, elapsedTime) / Mathf.Max(0.05f, rampSeconds));
        }

        // 시작 제한에서 종료 제한까지 정수로 보간해 현재 동시에 살아 있을 수 있는 좀비 수를 계산합니다.
        public static int CalculateAliveLimit(int startLimit, int endLimit, float progress)
        {
            return Mathf.Max(0, Mathf.RoundToInt(Mathf.Lerp(
                Mathf.Max(0, startLimit),
                Mathf.Max(0, endLimit),
                Mathf.Clamp01(progress))));
        }

        // 난이도 비율에 따라 다음 생성까지 기다릴 시간을 계산하며 0초 연속 생성을 막기 위해 최소 0.05초를 보장합니다.
        public static float CalculateSpawnInterval(float startInterval, float endInterval, float progress)
        {
            return Mathf.Max(0.05f, Mathf.Lerp(
                Mathf.Max(0.05f, startInterval),
                Mathf.Max(0.05f, endInterval),
                Mathf.Clamp01(progress)));
        }

        // 아직 해금되지 않았거나 가중치가 0인 종류를 제외한 뒤 가중치 비율로 한 종류의 배열 인덱스를 고릅니다.
        // 난수 대신 0~1 값을 인자로 받으므로 같은 입력은 항상 같은 결과를 내고 EditMode 테스트로 경계값을 검증할 수 있습니다.
        public static int SelectWeightedArchetypeIndex(
            ZombieArchetype[] definitions,
            float elapsedTime,
            float normalizedRoll)
        {
            if (definitions == null || definitions.Length == 0) return -1;

            float totalWeight = 0f;
            int lastEligibleIndex = -1;
            for (int i = 0; i < definitions.Length; i++)
            {
                ZombieArchetype definition = definitions[i];
                if (definition == null || !definition.IsUnlocked(elapsedTime) || definition.SpawnWeight <= 0f) continue;
                totalWeight += definition.SpawnWeight;
                lastEligibleIndex = i;
            }
            if (totalWeight <= 0f) return -1;

            float target = Mathf.Clamp01(normalizedRoll) * totalWeight;
            float accumulated = 0f;
            for (int i = 0; i < definitions.Length; i++)
            {
                ZombieArchetype definition = definitions[i];
                if (definition == null || !definition.IsUnlocked(elapsedTime) || definition.SpawnWeight <= 0f) continue;
                accumulated += definition.SpawnWeight;
                if (target < accumulated) return i;
            }

            // normalizedRoll이 정확히 1이면 target과 totalWeight가 같아지므로 마지막 유효 후보를 반환합니다.
            return lastEligibleIndex;
        }
    }
}
