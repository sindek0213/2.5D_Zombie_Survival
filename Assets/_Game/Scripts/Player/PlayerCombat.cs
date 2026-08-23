using System;
using System.Collections;
using UnityEngine;
using ZombieSurvival.Combat;

namespace ZombieSurvival.Player
{
    // 플레이어 권총의 입력, 탄약 소비, 발사 간격, Raycast 명중과 재장전을 한곳에서 조정합니다.
    // Hitscan 방식은 실제 탄환 오브젝트를 날리지 않고 발사 순간 Ray로 명중 결과를 즉시 계산합니다.
    // PlayerAim이 계산한 수평 방향을 사용하며, 피해 대상은 IDamageable 인터페이스로 호출합니다.
    [RequireComponent(typeof(PlayerAim))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private Transform weaponSocket;
        [SerializeField, Min(0.01f)] private float damage = 34f;
        [SerializeField, Min(0.1f)] private float range = 30f;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.2f;
        [SerializeField, Min(0f)] private float reloadDuration = 1.1f;
        [SerializeField, Min(1)] private int magazineSize = 8;
        [SerializeField, Min(0)] private int startingReserve = 24;
        [SerializeField] private LayerMask hitMask = ~0;

        private PlayerAim playerAim;
        private float nextFireTime;

        public AmmunitionState Ammo { get; private set; }
        public bool IsReloading { get; private set; }

        // 탄약 UI가 발사와 재장전 결과를 즉시 반영할 때 사용하는 이벤트입니다.
        public event Action AmmoChanged;

        private void Awake()
        {
            playerAim = GetComponent<PlayerAim>();
            Ammo = new AmmunitionState(magazineSize, magazineSize, startingReserve);
        }

        private void Update()
        {
            // GetMouseButtonDown을 사용해 권총이 버튼을 누른 횟수만큼만 발사되게 합니다.
            if (Input.GetMouseButtonDown(0)) TryFire();
            if (Input.GetKeyDown(KeyCode.R)) TryStartReload();
        }

        // 외부 코드나 입력에서 발사를 요청할 때 호출합니다.
        // 탄약, 재장전 여부와 연사 쿨다운 중 하나라도 조건을 만족하지 않으면 false를 반환하고 아무것도 변경하지 않습니다.
        // 발사 가능하면 탄약을 먼저 한 발 소비한 뒤 총구 위치에서 조준 방향으로 Raycast를 수행합니다.
        public bool TryFire()
        {
            if (!CanFire(Ammo.InMagazine, IsReloading, Time.time, nextFireTime)) return false;

            Ammo.TryConsumeRound();
            nextFireTime = Time.time + fireInterval;
            AmmoChanged?.Invoke();

            Vector3 origin = weaponSocket != null
                ? weaponSocket.position
                : transform.position + Vector3.up;
            Vector3 direction = playerAim.AimDirection;

            if (TryGetFirstNonSelfHit(origin, direction, out RaycastHit hit))
            {
                // Collider의 부모까지 검색해 자식 충돌체를 가진 적도 피해를 받게 합니다.
                IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(damage, hit.point);
                Debug.DrawLine(origin, hit.point, Color.red, 0.35f);
            }
            else
            {
                Debug.DrawRay(origin, direction * range, Color.yellow, 0.35f);
            }

            return true;
        }

        // 단일 Physics.Raycast는 첫 Collider에서 멈추므로 왼쪽 사격 때 자신의 몸에 막힐 수 있습니다.
        // RaycastAll로 사거리 안의 후보를 모두 얻은 뒤 플레이어 루트와 자식 Collider를 건너뜁니다.
        // 남은 후보 중 distance가 가장 작은 것을 선택하므로 자기 몸 뒤에 있는 가장 가까운 벽이나 적이 정상 명중합니다.
        private bool TryGetFirstNonSelfHit(Vector3 origin, Vector3 direction, out RaycastHit validHit)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction,
                range,
                hitMask,
                QueryTriggerInteraction.Ignore);

            // RaycastAll의 결과 순서는 보장되지 않으므로 가장 가까운 유효 명중점을 직접 찾습니다.
            float nearestDistance = float.PositiveInfinity;
            validHit = default;
            bool foundHit = false;
            foreach (RaycastHit hit in hits)
            {
                // 플레이어 루트 또는 그 자식에 속한 Collider는 발사 대상에서 제외합니다.
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform))
                    continue;

                if (hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                validHit = hit;
                foundHit = true;
            }

            return foundHit;
        }

        // 이미 재장전 중이거나 예비탄이 없거나 탄창이 가득 찼다면 false를 반환합니다.
        // 조건을 통과하면 Coroutine을 시작하며, 실제 탄약 이동은 reloadDuration이 지난 뒤 처리됩니다.
        public bool TryStartReload()
        {
            if (IsReloading || Ammo.Reserve <= 0 || Ammo.InMagazine >= Ammo.MagazineSize) return false;
            StartCoroutine(ReloadRoutine());
            return true;
        }

        // 인벤토리 탄약 사용 결과를 권총 예비탄에 반영합니다.
        public bool AddReserveAmmo(int quantity)
        {
            if (quantity <= 0) return false;
            Ammo.AddReserve(quantity);
            AmmoChanged?.Invoke();
            return true;
        }

        private IEnumerator ReloadRoutine()
        {
            IsReloading = true;
            AmmoChanged?.Invoke();
            yield return new WaitForSeconds(reloadDuration);

            Ammo.Reload();
            IsReloading = false;
            AmmoChanged?.Invoke();
        }

        // loadedRounds는 탄창 수량, isReloading은 재장전 상태입니다.
        // currentTime이 nextAllowedTime 이상이어야 이전 발사의 쿨다운이 끝난 것입니다.
        // Unity 오브젝트 없이도 테스트할 수 있도록 상태 판정을 정적 순수 함수로 분리했습니다.
        public static bool CanFire(int loadedRounds, bool isReloading, float currentTime, float nextAllowedTime)
        {
            return loadedRounds > 0 && !isReloading && currentTime >= nextAllowedTime;
        }
    }
}
