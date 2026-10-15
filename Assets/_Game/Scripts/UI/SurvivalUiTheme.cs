using UnityEngine;

namespace ZombieSurvival.UI
{
    // 게임의 모든 화면이 같은 분위기를 사용하도록 색상 규칙을 한곳에 모아 둔 공용 팔레트입니다.
    // UI 스크립트마다 임의의 Color 값을 직접 쓰지 않고 이 값을 사용하면, 나중에 전체 색감을 바꿀 때 이 파일만 수정하면 됩니다.
    public static class SurvivalUiTheme
    {
        // 화면 뒤를 어둡게 덮는 색입니다. 완전한 검정보다 아주 약한 올리브색을 넣어 생존 장비 화면의 분위기를 유지합니다.
        public static readonly Color ScreenBackdrop = new Color(0.025f, 0.028f, 0.023f, 0.94f);

        // 생성한 금속·천 질감에 곱해지는 색입니다. 원본 그림의 디테일은 남기면서 글자가 묻히지 않을 정도로 어둡게 만듭니다.
        public static readonly Color TexturedPanelTint = new Color(0.58f, 0.61f, 0.52f, 0.98f);

        // 목록, 팝업, 격자 뒤처럼 질감보다 내용 가독성이 중요한 내부 패널에 사용하는 두 단계의 어두운 색입니다.
        public static readonly Color PanelDeep = new Color(0.055f, 0.062f, 0.052f, 0.97f);
        public static readonly Color PanelRaised = new Color(0.105f, 0.112f, 0.092f, 0.98f);

        // 테두리는 밝은 흰색 대신 낡은 황동에 가까운 색을 사용해 패널의 경계만 조용히 구분합니다.
        public static readonly Color Border = new Color(0.43f, 0.40f, 0.29f, 0.92f);
        public static readonly Color BorderDim = new Color(0.25f, 0.25f, 0.19f, 0.82f);

        // 주황색은 선택, 실행 가능한 주요 버튼, 현재 사용 중인 항목처럼 플레이어가 바로 봐야 하는 정보에만 사용합니다.
        public static readonly Color Accent = new Color(0.88f, 0.43f, 0.105f, 1f);
        public static readonly Color AccentSoft = new Color(0.58f, 0.29f, 0.09f, 0.96f);

        // 본문 글자는 순백색보다 눈부심이 적은 따뜻한 아이보리색을 사용하고, 보조 설명은 채도를 낮춰 정보 우선순위를 나눕니다.
        public static readonly Color TextPrimary = new Color(0.90f, 0.88f, 0.79f, 1f);
        public static readonly Color TextMuted = new Color(0.62f, 0.63f, 0.54f, 1f);

        // 성공·경고·실패 메시지는 기본 강조색과 구분하되 전체 팔레트에서 튀지 않도록 채도를 조금 낮춘 색을 사용합니다.
        public static readonly Color Success = new Color(0.45f, 0.72f, 0.38f, 1f);
        public static readonly Color Warning = new Color(0.94f, 0.67f, 0.22f, 1f);
        public static readonly Color Danger = new Color(0.78f, 0.24f, 0.17f, 1f);
        public static readonly Color Info = new Color(0.47f, 0.68f, 0.72f, 1f);

        // 버튼은 평소에는 배경에 섞이고, 중요한 행동만 주황색 기운을 더해 기능의 우선순위를 드러냅니다.
        public static readonly Color ButtonNormal = new Color(0.105f, 0.115f, 0.095f, 1f);
        public static readonly Color ButtonPrimary = new Color(0.24f, 0.145f, 0.065f, 1f);
        public static readonly Color ButtonDanger = new Color(0.22f, 0.075f, 0.055f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.22f, 0.22f, 0.19f, 0.58f);

        // 격자와 아이템의 기본색은 아이콘보다 앞에 나오지 않도록 어둡게 유지하고, 선택 중인 항목만 주황색으로 올립니다.
        public static readonly Color GridBackground = new Color(0.025f, 0.030f, 0.025f, 0.96f);
        public static readonly Color GridCell = new Color(0.09f, 0.10f, 0.08f, 0.98f);
        public static readonly Color ItemCommon = new Color(0.20f, 0.22f, 0.17f, 1f);
        public static readonly Color ItemSelected = new Color(0.76f, 0.37f, 0.09f, 1f);
        public static readonly Color ItemDragging = new Color(0.91f, 0.55f, 0.18f, 0.78f);

        // 장비 슬롯은 비어 있음, 장착됨, 현재 사용하는 무기 상태를 명도와 강조색의 차이로 구분합니다.
        public static readonly Color EquipmentEmpty = new Color(0.09f, 0.10f, 0.085f, 0.98f);
        public static readonly Color EquipmentOccupied = new Color(0.20f, 0.25f, 0.17f, 0.98f);
        public static readonly Color EquipmentActive = new Color(0.55f, 0.28f, 0.08f, 0.99f);

        // 아이템 등급색은 기존 의미를 유지하되 전체 UI의 낮은 채도와 어울리도록 조정한 값입니다.
        public static readonly Color RarityUncommon = new Color(0.30f, 0.55f, 0.27f, 1f);
        public static readonly Color RarityRare = new Color(0.28f, 0.43f, 0.68f, 1f);
        public static readonly Color RarityQuest = new Color(0.82f, 0.50f, 0.13f, 1f);
    }
}
