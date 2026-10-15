using System;
using System.Collections.Generic;
using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Items;

namespace ZombieSurvival.Hideout
{
    // 제작 중인 한 작업을 Unity 오브젝트 참조 없이 저장할 수 있는 데이터입니다.
    // 결과 ItemId와 남은 시간만 저장하므로 제작법 에셋의 설명을 바꿔도 이미 시작한 제작 결과는 변하지 않습니다.
    [Serializable]
    public sealed class HideoutCraftJobState
    {
        public string recipeId;
        public string displayName;
        public string outputItemId;
        public int outputQuantity;
        public float remainingSeconds;
        public bool completed;
    }

    // 최대 세 개 제작 작업, 발전기 켜짐 여부와 남은 연료를 씬 전환 사이에서 유지합니다.
    // 실제 재료 소비와 결과 배치는 서비스·컨트롤러가 담당하고 이 저장소는 시간 상태만 책임집니다.
    public static class HideoutCraftQueueStateStore
    {
        public const int MaximumQueueSize = 3;
        public const float DefaultFuelSeconds = 900f;

        private static readonly List<HideoutCraftJobState> jobs = new List<HideoutCraftJobState>();

        public static IReadOnlyList<HideoutCraftJobState> Jobs => jobs;
        public static bool GeneratorEnabled { get; private set; } = true;
        public static float GeneratorFuelSeconds { get; private set; } = DefaultFuelSeconds;
        public static string LastNotification { get; private set; } = string.Empty;
        public static event Action StateChanged;

        public static bool TryEnqueue(
            PlayerInventory.GridData stash,
            HideoutFacilityDefinition facility,
            HideoutFacilityDefinition generator,
            HideoutRecipeDefinition recipe,
            out string message)
        {
            if (jobs.Count >= MaximumQueueSize)
            {
                message = $"제작 대기열이 가득 찼습니다. ({jobs.Count}/{MaximumQueueSize})";
                return false;
            }
            if (!GeneratorEnabled || GeneratorFuelSeconds <= 0f)
            {
                message = "발전기를 켜고 연료를 보충해야 제작을 시작할 수 있습니다.";
                return false;
            }
            if (!HideoutCraftingService.TryConsumeForQueue(stash, facility, generator, recipe, out message))
                return false;

            int facilityLevel = HideoutFacilityStateStore.GetLevel(facility);
            jobs.Add(new HideoutCraftJobState
            {
                recipeId = recipe.RecipeId,
                displayName = recipe.DisplayName,
                outputItemId = recipe.OutputItem.ItemId,
                outputQuantity = recipe.GetOutputQuantity(facilityLevel),
                remainingSeconds = recipe.GetCraftDuration(facilityLevel),
                completed = false
            });
            message = $"{recipe.DisplayName} 제작을 시작했습니다. 대기열 {jobs.Count}/{MaximumQueueSize}";
            StateChanged?.Invoke();
            return true;
        }

        // 발전기가 켜져 있고 연료가 남은 동안 모든 작업 시간을 함께 줄입니다.
        // 프레임 시간이 연료보다 길면 실제 사용 가능한 시간만 적용해 연료가 음수가 되지 않게 합니다.
        public static bool Tick(float unscaledDeltaTime)
        {
            if (!GeneratorEnabled || GeneratorFuelSeconds <= 0f || unscaledDeltaTime <= 0f) return false;
            bool hasRunningJob = false;
            foreach (HideoutCraftJobState job in jobs)
                if (job != null && !job.completed) { hasRunningJob = true; break; }
            if (!hasRunningJob) return false;

            float elapsed = Mathf.Min(Mathf.Max(0f, unscaledDeltaTime), GeneratorFuelSeconds);
            GeneratorFuelSeconds = Mathf.Max(0f, GeneratorFuelSeconds - elapsed);
            bool completedAny = false;
            foreach (HideoutCraftJobState job in jobs)
            {
                if (job == null || job.completed) continue;
                job.remainingSeconds = Mathf.Max(0f, job.remainingSeconds - elapsed);
                if (job.remainingSeconds > 0f) continue;
                job.completed = true;
                completedAny = true;
                LastNotification = $"제작 완료: {job.displayName} · 시설에서 수령하세요.";
            }
            if (completedAny || GeneratorFuelSeconds <= 0f) StateChanged?.Invoke();
            return true;
        }

        // 완성된 결과가 창고에 전부 들어갈 때만 작업을 제거합니다. 공간 부족이면 완료 상태로 남겨 나중에 다시 수령할 수 있습니다.
        public static bool TryClaim(string recipeId, PlayerInventory.GridData stash, ItemCatalog catalog, out string message)
        {
            HideoutCraftJobState job = FindCompleted(recipeId);
            ItemDefinition output = job != null && catalog != null ? catalog.Find(job.outputItemId) : null;
            if (job == null || output == null)
            {
                message = "수령할 완성품이 없거나 아이템 데이터가 없습니다.";
                return false;
            }
            if (!InventoryGridContent.TryAdd(stash, output, job.outputQuantity))
            {
                message = "완성품 전체가 들어갈 창고 공간이 없습니다.";
                return false;
            }
            jobs.Remove(job);
            message = $"{output.DisplayName} x{job.outputQuantity}을 창고에 수령했습니다.";
            StateChanged?.Invoke();
            return true;
        }

        public static HideoutCraftJobState FindJob(string recipeId)
        {
            if (string.IsNullOrWhiteSpace(recipeId)) return null;
            foreach (HideoutCraftJobState job in jobs)
                if (job != null && job.recipeId == recipeId) return job;
            return null;
        }

        private static HideoutCraftJobState FindCompleted(string recipeId)
        {
            HideoutCraftJobState job = FindJob(recipeId);
            return job != null && job.completed ? job : null;
        }

        public static void SetGeneratorEnabled(bool enabled)
        {
            GeneratorEnabled = enabled;
            StateChanged?.Invoke();
        }

        public static void AddFuel(float seconds)
        {
            if (seconds <= 0f) return;
            GeneratorFuelSeconds = Mathf.Min(86400f, GeneratorFuelSeconds + seconds);
            StateChanged?.Invoke();
        }

        public static List<HideoutCraftJobState> CaptureState()
        {
            var result = new List<HideoutCraftJobState>();
            foreach (HideoutCraftJobState job in jobs)
                if (job != null) result.Add(Clone(job));
            return result;
        }

        public static void Restore(
            IEnumerable<HideoutCraftJobState> savedJobs,
            bool generatorEnabled,
            float generatorFuelSeconds)
        {
            jobs.Clear();
            if (savedJobs != null)
                foreach (HideoutCraftJobState job in savedJobs)
                {
                    if (!IsValid(job) || jobs.Count >= MaximumQueueSize) continue;
                    jobs.Add(Clone(job));
                }
            GeneratorEnabled = generatorEnabled;
            GeneratorFuelSeconds = Mathf.Clamp(generatorFuelSeconds, 0f, 86400f);
            LastNotification = string.Empty;
            StateChanged?.Invoke();
        }

        public static void ResetForTests()
        {
            Restore(null, true, DefaultFuelSeconds);
            StateChanged = null;
        }

        private static bool IsValid(HideoutCraftJobState job)
            => job != null && !string.IsNullOrWhiteSpace(job.recipeId) &&
               !string.IsNullOrWhiteSpace(job.outputItemId) && job.outputQuantity > 0 && job.remainingSeconds >= 0f;

        private static HideoutCraftJobState Clone(HideoutCraftJobState source)
            => new HideoutCraftJobState
            {
                recipeId = source.recipeId,
                displayName = source.displayName,
                outputItemId = source.outputItemId,
                outputQuantity = source.outputQuantity,
                remainingSeconds = Mathf.Max(0f, source.remainingSeconds),
                completed = source.completed || source.remainingSeconds <= 0f
            };
    }
}
