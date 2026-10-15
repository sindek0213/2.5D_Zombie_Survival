using System;
using UnityEngine;

namespace ZombieSurvival.Player
{
    // 한 번의 레이드 안에서 사용하는 포만감·스태미나와 두 수치가 이동 속도에 주는 영향을 관리합니다.
    // 이 값은 Player 프리팹이 생성될 때마다 시작 수치로 초기화되므로 거점 저장 데이터에는 포함하지 않습니다.
    // 음식 아이템은 PlayerInventory를 통해 RestoreSatiety를 호출하고, PlayerMovement는 MovementSpeedMultiplier만 읽습니다.
    public sealed class PlayerSurvival : MonoBehaviour
    {
        // HUD는 포만감을 정수로 표시하므로 1보다 작은 변화는 화면에서 사용자가 구분하기 어렵습니다.
        // 이 값보다 적게 회복되는 상황에서는 통조림 한 개를 통째로 낭비하지 않도록 사용을 거부합니다.
        private const float MinimumUsefulRecovery = 1f;

        // 시작 포만감 85에서 초당 0.25가 줄면 4분 레이드 종료 시 약 25가 남습니다.
        // 즉 탐색이 느린 첫 플레이도 음식이 강제되지는 않지만, 다음 단계에서 장기 레이드를 만들면 식량 가치가 생깁니다.
        [SerializeField, Min(1f)] private float maximumSatiety = 100f;
        [SerializeField, Min(0f)] private float startingSatiety = 85f;
        [SerializeField, Min(0f)] private float drainPerSecond = 0.25f;
        [SerializeField, Min(0f)] private float lowSatietyThreshold = 20f;
        [SerializeField, Range(0.1f, 1f)] private float minimumMovementMultiplier = 0.75f;

        [Header("달리기 스태미나")]
        // 최대 상태에서 약 5초 달릴 수 있고 완전 회복에는 약 6.25초가 걸려 도주와 무한 질주를 구분합니다.
        [SerializeField, Min(1f)] private float maximumStamina = 100f;
        [SerializeField, Min(0f)] private float startingStamina = 100f;
        [SerializeField, Min(0f)] private float sprintDrainPerSecond = 20f;
        [SerializeField, Min(0f)] private float staminaRecoveryPerSecond = 16f;
        [SerializeField, Range(1f, 3f)] private float sprintSpeedMultiplier = 1.4f;
        [SerializeField, Min(0f)] private float exhaustedResumeStamina = 25f;

        public float CurrentSatiety { get; private set; }
        public float MaximumSatiety => Mathf.Max(1f, maximumSatiety);
        public float DrainPerSecond => Mathf.Max(0f, drainPerSecond);
        public float LowSatietyThreshold => Mathf.Clamp(lowSatietyThreshold, 0f, MaximumSatiety);
        public float MovementSpeedMultiplier => CalculateMovementSpeedMultiplier(
            CurrentSatiety,
            LowSatietyThreshold,
            minimumMovementMultiplier);
        public float CurrentStamina { get; private set; }
        public float MaximumStamina => Mathf.Max(1f, maximumStamina);
        public float SprintSpeedMultiplier => IsSprinting ? Mathf.Max(1f, sprintSpeedMultiplier) : 1f;
        public bool IsSprinting { get; private set; }

        // 스태미나가 0이 된 직후 Shift를 계속 누르면 회복과 소모가 매 프레임 번갈아 일어날 수 있습니다.
        // exhausted는 최소 재개 수치까지 달리기를 잠가 짧게 덜컥거리는 움직임을 막는 내부 상태입니다.
        private bool exhausted;
        private bool satietyDrainEnabled = true;
        private float staminaRecoveryResumesAt;

        // HUD처럼 포만감이 바뀌는 순간만 갱신하고 싶은 화면이 사용할 수 있는 알림입니다.
        public event Action<float, float> SatietyChanged;
        public event Action<float, float> StaminaChanged;

        private void Awake()
        {
            // 시작값이 최대치보다 크게 설정되어도 유효한 범위 안에서 레이드를 시작합니다.
            CurrentSatiety = Mathf.Clamp(startingSatiety, 0f, MaximumSatiety);
            CurrentStamina = Mathf.Clamp(startingStamina, 0f, MaximumStamina);
            exhausted = CurrentStamina <= 0f;
        }

        private void Update()
        {
            // Time.deltaTime을 곱해 초당 감소량을 계산하므로 프레임 속도가 달라도 같은 시간 동안 같은 양이 줄어듭니다.
            // 인벤토리를 열면 조작만 잠기고 레이드 시간은 계속 흐르므로, 아이템을 정리하는 동안에도 포만감은 계속 감소합니다.
            if (satietyDrainEnabled) ConsumeSatiety(DrainPerSecond * Time.deltaTime);

            float previousStamina = CurrentStamina;
            CurrentStamina = CalculateStaminaAfterTick(
                CurrentStamina,
                MaximumStamina,
                IsSprinting,
                sprintDrainPerSecond,
                Time.time >= staminaRecoveryResumesAt ? staminaRecoveryPerSecond : 0f,
                Time.deltaTime);

            // 완전히 고갈된 순간에는 달리기를 즉시 중지하고, 설정한 재개 수치까지 회복되기 전에는 다시 시작하지 않습니다.
            if (IsSprinting && CurrentStamina <= 0f)
            {
                CurrentStamina = 0f;
                IsSprinting = false;
                exhausted = true;
            }

            if (!Mathf.Approximately(previousStamina, CurrentStamina))
                StaminaChanged?.Invoke(CurrentStamina, MaximumStamina);
        }

        // PlayerMovement가 이번 프레임에 실제 이동 입력과 Shift가 함께 눌렸는지를 전달합니다.
        // 이동 입력이 없거나 조작 컴포넌트가 꺼지면 false가 전달되어 제자리에서 스태미나가 소모되지 않습니다.
        public void SetSprintRequested(bool requested)
        {
            if (exhausted && CurrentStamina >= Mathf.Clamp(exhaustedResumeStamina, 0f, MaximumStamina))
                exhausted = false;

            IsSprinting = requested && !exhausted && CurrentStamina > 0f;
        }

        // 거점에서는 포만감만 멈추고 달리기·구르기 스태미나의 소모와 회복은 계속 사용합니다.
        // 컴포넌트 전체를 끄지 않으므로 거점과 레이드에서 Space 구르기 조작이 서로 다르게 느껴지지 않습니다.
        public void SetSatietyDrainEnabled(bool value) => satietyDrainEnabled = value;

        // 구르기처럼 한 번에 정해진 양을 쓰는 행동이 스태미나를 원자적으로 지불할 때 사용합니다.
        // 부족하면 값을 전혀 바꾸지 않고 false를 반환합니다. recoveryDelay 동안은 기력 자동 회복만 미룹니다.
        public bool TrySpendStamina(float amount, float recoveryDelay = 0f)
        {
            float cost = Mathf.Max(0f, amount);
            if (cost <= 0f || CurrentStamina + 0.0001f < cost) return false;

            CurrentStamina = Mathf.Max(0f, CurrentStamina - cost);
            staminaRecoveryResumesAt = Mathf.Max(staminaRecoveryResumesAt, Time.time + Mathf.Max(0f, recoveryDelay));
            IsSprinting = false;
            if (CurrentStamina <= 0f) exhausted = true;
            StaminaChanged?.Invoke(CurrentStamina, MaximumStamina);
            return true;
        }

        // 25%는 0.25로 전달합니다. 최대 기력을 기준으로 계산하므로 현재 기력이 적다고 구르기 비용이 싸지지 않습니다.
        public bool TrySpendStaminaFraction(float fraction, float recoveryDelay = 0f)
            => TrySpendStamina(MaximumStamina * Mathf.Clamp01(fraction), recoveryDelay);

        // 실제로 회복될 양은 음식의 회복량과 현재 비어 있는 포만감 중 더 작은 값입니다.
        // HUD가 100으로 보이지만 시간 경과로 99.9처럼 아주 조금 줄어든 경우에는 1도 회복되지 않으므로
        // false를 반환해 PlayerInventory가 음식 한 개를 통째로 소비하지 않도록 합니다.
        public bool RestoreSatiety(float amount)
        {
            float actualRecovery = Mathf.Min(Mathf.Max(0f, amount), MaximumSatiety - CurrentSatiety);
            if (actualRecovery < MinimumUsefulRecovery) return false;

            CurrentSatiety = Mathf.Min(MaximumSatiety, CurrentSatiety + actualRecovery);
            SatietyChanged?.Invoke(CurrentSatiety, MaximumSatiety);
            return true;
        }

        // 경과 시간에 해당하는 포만감을 0 아래로 내려가지 않게 제거합니다.
        // 값이 실제로 달라졌을 때만 이벤트를 보내 포만감이 이미 0인 동안 불필요한 UI 갱신을 만들지 않습니다.
        public void ConsumeSatiety(float amount)
        {
            if (amount <= 0f || CurrentSatiety <= 0f) return;

            CurrentSatiety = Mathf.Max(0f, CurrentSatiety - amount);
            SatietyChanged?.Invoke(CurrentSatiety, MaximumSatiety);
        }

        // 임계치 이상이면 정상 속도 1을 사용하고, 임계치 아래에서는 포만감이 낮을수록 최소 배율까지 부드럽게 감소합니다.
        // 예를 들어 임계치 20·최소 배율 0.75이면 포만감 20에서는 100%, 0에서는 75% 속도로 움직입니다.
        public static float CalculateMovementSpeedMultiplier(
            float currentSatiety,
            float lowSatietyThreshold,
            float minimumMultiplier)
        {
            float safeMinimum = Mathf.Clamp(minimumMultiplier, 0.1f, 1f);
            float safeThreshold = Mathf.Max(0f, lowSatietyThreshold);
            if (safeThreshold <= 0f || currentSatiety >= safeThreshold) return 1f;

            float normalizedSatiety = Mathf.Clamp01(currentSatiety / safeThreshold);
            return Mathf.Lerp(safeMinimum, 1f, normalizedSatiety);
        }

        // 한 프레임 동안 달리면 초당 소모량을 빼고, 달리지 않으면 초당 회복량을 더한 뒤 0~최대 범위로 제한합니다.
        // 컴포넌트 상태에 의존하지 않는 계산 함수로 분리해 소모·회복·최대치 경계를 자동 테스트할 수 있습니다.
        public static float CalculateStaminaAfterTick(
            float current,
            float maximum,
            bool sprinting,
            float drainPerSecond,
            float recoveryPerSecond,
            float deltaTime)
        {
            float safeMaximum = Mathf.Max(1f, maximum);
            float rate = sprinting ? -Mathf.Max(0f, drainPerSecond) : Mathf.Max(0f, recoveryPerSecond);
            return Mathf.Clamp(current + rate * Mathf.Max(0f, deltaTime), 0f, safeMaximum);
        }

        // Inspector에서 최대치나 임계치를 줄였을 때 서로 모순되는 값이 에셋에 남지 않도록 즉시 보정합니다.
        private void OnValidate()
        {
            maximumSatiety = Mathf.Max(1f, maximumSatiety);
            startingSatiety = Mathf.Clamp(startingSatiety, 0f, maximumSatiety);
            drainPerSecond = Mathf.Max(0f, drainPerSecond);
            lowSatietyThreshold = Mathf.Clamp(lowSatietyThreshold, 0f, maximumSatiety);
            minimumMovementMultiplier = Mathf.Clamp(minimumMovementMultiplier, 0.1f, 1f);
            maximumStamina = Mathf.Max(1f, maximumStamina);
            startingStamina = Mathf.Clamp(startingStamina, 0f, maximumStamina);
            sprintDrainPerSecond = Mathf.Max(0f, sprintDrainPerSecond);
            staminaRecoveryPerSecond = Mathf.Max(0f, staminaRecoveryPerSecond);
            sprintSpeedMultiplier = Mathf.Clamp(sprintSpeedMultiplier, 1f, 3f);
            exhaustedResumeStamina = Mathf.Clamp(exhaustedResumeStamina, 0f, maximumStamina);
        }
    }
}
