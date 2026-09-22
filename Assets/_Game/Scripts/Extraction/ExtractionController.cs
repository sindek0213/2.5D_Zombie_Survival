using System;
using UnityEngine;
using ZombieSurvival.Inventory;
using ZombieSurvival.Player;
using ZombieSurvival.Core;

namespace ZombieSurvival.Extraction
{
    // 탈출 구역의 조건 확인, 플레이어 진입 상태, 카운트다운과 성공 처리를 관리합니다.
    // Trigger는 플레이어가 구역에 있는지만 전달하고, 실제 조건과 시간 계산은 Update에서 매 프레임 처리합니다.
    [RequireComponent(typeof(Collider))]
    public sealed class ExtractionController : MonoBehaviour
    {
        public enum ExtractionState
        {
            Locked,
            Available,
            CountingDown,
            Succeeded
        }

        [SerializeField] private ExtractionRequirement requirement;
        [SerializeField, Min(0.1f)] private float countdownDuration = 3f;
        [SerializeField] private Renderer visualRenderer;

        private PlayerInventory playerInventory;
        private RaidSession raidSession;
        private bool playerInside;
        private ExtractionState lastRenderedState;
        private bool hasRenderedState;

        public ExtractionState State { get; private set; } = ExtractionState.Locked;
        public float Progress { get; private set; }
        public float RemainingTime => Mathf.Max(0f, countdownDuration - Progress);
        public ExtractionRequirement Requirement => requirement;
        public RaidSession RaidSession => raidSession;
        public bool ItemRequirementMet => HasItemRequirement();
        public bool RaidRequirementMet => raidSession == null || raidSession.AreExtractionConditionsMet;
        public event Action ExtractionSucceeded;

        // RaidSession이 처치 수와 최소 생존 시간 조건을 제공하도록 서로를 연결합니다.
        public void SetRaidSession(RaidSession session) => raidSession = session;

        private void Awake()
        {
            // 탈출 지점 Collider는 물리적으로 막는 벽이 아니라 진입 감지 구역이어야 합니다.
            // 생성기나 Inspector에서 실수로 isTrigger를 끈 경우에도 실행 시 안전하게 바로잡습니다.
            GetComponent<Collider>().isTrigger = true;
        }

        private void Start()
        {
            // 구역 밖에서도 HUD와 Visual이 조건 충족 여부를 갱신할 수 있도록 씬의 플레이어 인벤토리를 미리 찾습니다.
            // OnTriggerEnter에서만 참조를 얻으면 무전기를 주운 순간이 아니라 구역에 들어간 뒤에야 Available로 바뀌게 됩니다.
            playerInventory = FindObjectOfType<PlayerInventory>();
            if (raidSession == null) raidSession = FindObjectOfType<RaidSession>();
        }

        private void Update()
        {
            if (State == ExtractionState.Succeeded) return;

            bool requirementMet = HasRequirement();
            if (!requirementMet)
            {
                // 카운트다운 도중 아이템을 버렸다면 즉시 잠기고 진행 시간을 0으로 되돌립니다.
                State = ExtractionState.Locked;
                Progress = 0f;
                UpdateVisual();
                return;
            }

            if (!playerInside)
            {
                // 조건은 충족했지만 구역 밖이라면 사용할 수 있는 상태만 표시하고 시간은 누적하지 않습니다.
                State = ExtractionState.Available;
                Progress = 0f;
                UpdateVisual();
                return;
            }

            State = ExtractionState.CountingDown;
            Progress = AdvanceProgress(Progress, Time.deltaTime, countdownDuration, true);
            UpdateVisual();
            if (Progress < countdownDuration) return;

            CompleteExtraction();
        }

        private void OnTriggerEnter(Collider other)
        {
            // 플레이어의 CharacterController 또는 자식 Collider가 들어올 수 있으므로 부모 방향으로 인벤토리를 찾습니다.
            PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
            if (inventory == null) return;
            playerInventory = inventory;
            playerInside = true;
        }

        private void OnTriggerExit(Collider other)
        {
            PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
            if (inventory == null || inventory != playerInventory) return;

            // 구역을 벗어나면 이번 카운트다운은 취소됩니다. 다시 들어오면 0초부터 시작합니다.
            playerInside = false;
            Progress = 0f;
        }

        private bool HasRequirement()
        {
            return HasItemRequirement() && RaidRequirementMet;
        }

        private bool HasItemRequirement()
        {
            return requirement != null && playerInventory != null &&
                   HasRequiredQuantity(playerInventory.CountItem(requirement.RequiredItemId), requirement.RequiredQuantity);
        }

        private void CompleteExtraction()
        {
            State = ExtractionState.Succeeded;

            // 성공 즉시 PlayerControlGate에 탈출 지점 자신의 잠금을 남겨 결과 생성 전에 추가 조작이 들어오지 않게 합니다.
            // 아래 이벤트를 RaidSession이 받아 별도 종료 잠금을 추가하고 RaidResult 생성과 Hideout 복귀를 순서대로 진행합니다.
            PlayerControlGate controlGate = playerInventory.GetComponent<PlayerControlGate>();
            if (controlGate != null)
            {
                controlGate.AcquireLock(this);
            }
            else
            {
                // Gate가 없는 이전 테스트 프리팹에서도 탈출 직후 이동과 사격이 계속되지 않도록 예비 처리를 유지합니다.
                PlayerMovement movement = playerInventory.GetComponent<PlayerMovement>();
                PlayerCombat combat = playerInventory.GetComponent<PlayerCombat>();
                if (movement != null) movement.enabled = false;
                if (combat != null) combat.enabled = false;
            }
            ExtractionSucceeded?.Invoke();
            UpdateVisual();
        }

        // 조건 미충족은 어두운 빨강, 활성화와 카운트다운은 녹색, 성공은 청록색으로 표시합니다.
        // Renderer가 연결되지 않은 테스트 오브젝트에서도 로직이 작동하도록 null이면 시각 처리만 건너뜁니다.
        private void UpdateVisual()
        {
            if (visualRenderer == null || (hasRenderedState && lastRenderedState == State)) return;
            visualRenderer.material.color = State == ExtractionState.Locked
                ? new Color(0.45f, 0.08f, 0.08f)
                : State == ExtractionState.Succeeded
                    ? new Color(0.1f, 0.9f, 0.9f)
                    : new Color(0.1f, 0.8f, 0.3f);
            // 상태가 실제로 달라졌을 때만 Renderer 색을 다시 설정해 매 프레임 material 접근과 색상 쓰기를 반복하지 않습니다.
            lastRenderedState = State;
            hasRenderedState = true;
        }

        // 보유 수량이 요구 수량 이상일 때만 조건을 충족합니다.
        // 정적 순수 함수로 분리해 경계값을 EditMode 테스트에서 검증할 수 있습니다.
        public static bool HasRequiredQuantity(int ownedQuantity, int requiredQuantity)
        {
            return requiredQuantity > 0 && ownedQuantity >= requiredQuantity;
        }

        // valid가 false면 진행을 즉시 0으로 취소하고, true면 duration을 넘지 않도록 시간을 누적합니다.
        // 구역 이탈과 조건 상실 시 리셋되는 카운트다운 규칙을 Unity 실행 없이 테스트하기 위한 함수입니다.
        public static float AdvanceProgress(float current, float deltaTime, float duration, bool valid)
        {
            if (!valid) return 0f;
            return Mathf.Clamp(current + Mathf.Max(0f, deltaTime), 0f, Mathf.Max(0f, duration));
        }
    }
}
