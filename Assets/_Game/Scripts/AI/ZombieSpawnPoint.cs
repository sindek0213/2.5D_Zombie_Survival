using UnityEngine;

namespace ZombieSurvival.AI
{
    // 좀비가 나타날 수 있는 월드 위치를 표시하는 가벼운 마커입니다.
    // 빈 GameObject에 이 컴포넌트를 붙여 두면 ZombieSpawner가 그 Transform의 위치와 방향을 사용합니다.
    public sealed class ZombieSpawnPoint : MonoBehaviour
    {
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        // Scene 창에서 선택하지 않아도 스폰 위치를 확인할 수 있도록 작은 빨간 구와 방향선을 그립니다.
        // Gizmos는 에디터에서만 보이며 실제 게임 화면이나 물리 충돌에는 아무 영향이 없습니다.
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.2f, 0.15f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.45f);
            Gizmos.DrawLine(
                transform.position + Vector3.up * 0.2f,
                transform.position + Vector3.up * 0.2f + transform.forward * 1.2f);
        }
    }
}
