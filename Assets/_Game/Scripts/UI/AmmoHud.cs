using UnityEngine;
using ZombieSurvival.Combat;
using ZombieSurvival.Player;

namespace ZombieSurvival.UI
{
    // 별도 Canvas 프리팹 없이 OnGUI로 탄약, 재장전 상태, 체력과 기본 조작법을 표시합니다.
    // 전투나 체력 값을 직접 변경하지 않고 PlayerCombat과 Health의 공개 상태만 읽으므로 게임 로직과 UI가 분리됩니다.
    // 최종 UI를 uGUI나 UI Toolkit으로 교체해도 전투 시스템 코드는 그대로 유지할 수 있습니다.
    public sealed class AmmoHud : MonoBehaviour
    {
        [SerializeField] private PlayerCombat combat;
        [SerializeField] private Health playerHealth;
        private GUIStyle ammoStyle;
        private GUIStyle guideStyle;

        // 씬 생성 코드가 플레이어의 전투 컴포넌트를 연결할 때 사용합니다.
        public void SetCombat(PlayerCombat value) => combat = value;

        // 플레이어 체력 컴포넌트를 HUD에 연결합니다.
        public void SetPlayerHealth(Health value) => playerHealth = value;

        private void Awake()
        {
        // Awake에서 글자 크기, 정렬과 색상을 한 번 만들어 OnGUI가 호출될 때마다 새 스타일을 생성하지 않게 합니다.
        // ammoStyle은 오른쪽 정렬 숫자와 체력에, guideStyle은 왼쪽 위 조작 안내에 사용합니다.
            ammoStyle = new GUIStyle { fontSize = 28, alignment = TextAnchor.UpperRight };
            ammoStyle.normal.textColor = Color.white;
            guideStyle = new GUIStyle { fontSize = 16 };
            guideStyle.normal.textColor = new Color(0.75f, 0.85f, 0.85f);
        }

        private void OnGUI()
        {
            if (combat == null || combat.Ammo == null) return;

            string ammoText = combat.IsReloading
                ? "RELOADING..."
                : $"{combat.Ammo.InMagazine} / {combat.Ammo.Reserve}";
            GUI.Label(new Rect(Screen.width - 260f, 24f, 230f, 45f), ammoText, ammoStyle);
            GUI.Label(new Rect(24f, 24f, 440f, 30f), "WASD 이동  |  좌클릭 발사  |  R 재장전", guideStyle);

            // Health 참조가 연결된 경우에만 표시해 HUD를 체력 없는 테스트 씬에서도 재사용할 수 있습니다.
            // CeilToInt를 사용하므로 소수 피해가 도입돼도 화면에는 읽기 쉬운 정수 형태로 표시됩니다.
            if (playerHealth != null)
            {
                string healthText = $"HP {Mathf.CeilToInt(playerHealth.CurrentHealth)} / {Mathf.CeilToInt(playerHealth.MaxHealth)}";
                GUI.Label(new Rect(24f, Screen.height - 58f, 300f, 40f), healthText, ammoStyle);
            }
        }
    }
}
