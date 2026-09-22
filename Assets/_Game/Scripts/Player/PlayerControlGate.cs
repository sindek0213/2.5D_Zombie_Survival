using System.Collections.Generic;
using UnityEngine;

namespace ZombieSurvival.Player
{
    // 인벤토리, 탈출 성공과 레이드 종료처럼 서로 다른 시스템이 플레이어 조작을 안전하게 잠그도록 한곳에서 관리합니다.
    // 잠금을 요청한 모든 소유자가 해제할 때까지 이동·조준·전투·상호작용을 계속 막아 한 시스템이 다른 잠금을 실수로 풀지 못하게 합니다.
    public sealed class PlayerControlGate : MonoBehaviour
    {
        [SerializeField] private PlayerMovement movement;
        [SerializeField] private PlayerAim aim;
        [SerializeField] private PlayerCombat combat;
        [SerializeField] private PlayerInteraction interaction;

        private readonly HashSet<Object> lockOwners = new HashSet<Object>();
        private bool movementWasEnabled;
        private bool aimWasEnabled;
        private bool combatWasEnabled;
        private bool interactionWasEnabled;

        // 잠금 소유자가 Destroy됐는데 OnDisable에서 해제하지 못한 예외 상황도 있으므로,
        // 외부에서 상태를 읽을 때 먼저 사라진 Unity 오브젝트를 정리해 영구 잠금이 남지 않게 합니다.
        public bool IsLocked
        {
            get
            {
                RemoveDestroyedOwners();
                return lockOwners.Count > 0;
            }
        }

        public int ActiveLockCount
        {
            get
            {
                RemoveDestroyedOwners();
                return lockOwners.Count;
            }
        }

        private void Awake() => ResolveControls();

        // owner는 잠금을 요청한 InventoryHud나 RaidSession 자신입니다.
        // 같은 owner가 두 번 요청하면 잠금 수를 중복 증가시키지 않으며 최초 잠금에서만 원래 enabled 상태를 저장합니다.
        public bool AcquireLock(Object owner)
        {
            // Unity에서 Destroy된 Object는 C# 참조가 HashSet 안에 남아 있어도 owner == null로 비교됩니다.
            // 새 잠금을 추가하기 전에 그런 항목을 제거해야 기존 유령 잠금을 두 번째 소유자로 잘못 세지 않습니다.
            RemoveDestroyedOwners();
            if (owner == null || !lockOwners.Add(owner)) return false;
            if (lockOwners.Count > 1) return true;

            ResolveControls();
            CaptureOriginalStates();
            SetGameplayControlsEnabled(false);
            return true;
        }

        // owner 자신의 잠금만 제거하고 다른 시스템의 잠금이 남아 있으면 조작은 계속 비활성 상태를 유지합니다.
        // 마지막 잠금이 제거될 때 최초 잠금 직전 상태를 복원하므로 원래부터 꺼져 있던 컴포넌트는 켜지지 않습니다.
        public bool ReleaseLock(Object owner)
        {
            // 다른 소유자를 해제하는 순간에도 먼저 파괴된 소유자를 치워,
            // 실제 마지막 잠금이 풀렸는데 HashSet 개수 때문에 조작이 계속 꺼져 있는 문제를 막습니다.
            RemoveDestroyedOwners();
            if (owner == null || !lockOwners.Remove(owner)) return false;
            if (lockOwners.Count > 0) return true;

            RestoreOriginalStates();
            return true;
        }

        private void ResolveControls()
        {
            if (movement == null) movement = GetComponent<PlayerMovement>();
            if (aim == null) aim = GetComponent<PlayerAim>();
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (interaction == null) interaction = GetComponent<PlayerInteraction>();
        }

        private void CaptureOriginalStates()
        {
            movementWasEnabled = movement != null && movement.enabled;
            aimWasEnabled = aim != null && aim.enabled;
            combatWasEnabled = combat != null && combat.enabled;
            interactionWasEnabled = interaction != null && interaction.enabled;
        }

        private void SetGameplayControlsEnabled(bool value)
        {
            if (movement != null) movement.enabled = value;
            if (aim != null) aim.enabled = value;
            if (combat != null) combat.enabled = value;
            if (interaction != null) interaction.enabled = value;
        }

        private void RestoreOriginalStates()
        {
            if (movement != null) movement.enabled = movementWasEnabled;
            if (aim != null) aim.enabled = aimWasEnabled;
            if (combat != null) combat.enabled = combatWasEnabled;
            if (interaction != null) interaction.enabled = interactionWasEnabled;
        }

        // HashSet은 UnityEngine.Object의 가짜 null 상태를 자동으로 제거하지 않습니다.
        // 잠금이 하나라도 있던 상태에서 파괴된 소유자들을 지운 결과 0개가 됐다면,
        // 정상적인 마지막 ReleaseLock과 똑같이 최초 잠금 직전의 컴포넌트 상태를 복원합니다.
        private void RemoveDestroyedOwners()
        {
            if (lockOwners.Count == 0) return;

            int previousCount = lockOwners.Count;
            lockOwners.RemoveWhere(owner => owner == null);
            if (previousCount > 0 && lockOwners.Count == 0) RestoreOriginalStates();
        }
    }
}
