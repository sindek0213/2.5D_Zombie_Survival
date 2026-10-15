using UnityEngine;
using UnityEngine.SceneManagement;
using ZombieSurvival.Art;
using ZombieSurvival.Combat;

namespace ZombieSurvival.Player
{
    // 생성된 캐릭터 시트를 런타임에 8방향 × 4프레임으로 잘라 SpriteRenderer에 보여 주는 가벼운 애니메이터입니다.
    // Unity Animator Controller를 수십 개 직접 만들지 않고도 현재 프로토타입에서 이동과 총기 발사 그림을 바로 확인할 수 있게 합니다.
    // 나중에 정식 애니메이션 클립을 만들면 이 컴포넌트를 끄고 Animator로 교체할 수 있으며 이동·전투 계산에는 영향을 주지 않습니다.
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerAim))]
    public sealed class PlayerSpriteAnimator : MonoBehaviour
    {
        private const int DirectionCount = 8;
        private const int FramesPerDirection = 4;
        private const float GeneratedSheetReferenceHeight = 1774f;

        // 세 원본은 모두 8행이지만 이미지 생성 과정에서 행 간격이 일정하지 않게 배치되었습니다.
        // 전체 높이를 단순히 8등분하면 다음 방향의 머리나 이전 방향의 발이 섞이므로,
        // 각 그림 사이의 실제 빈 공간을 기준으로 측정한 위쪽 원점 Y 경계를 별도로 사용합니다.
        private static readonly int[] MovementRowEdges = { 0, 219, 425, 624, 838, 1053, 1265, 1468, 1693 };
        private static readonly int[] PistolRowEdges = { 0, 211, 424, 629, 826, 1045, 1255, 1464, 1688 };
        private static readonly int[] RifleRowEdges = { 0, 211, 419, 635, 852, 1052, 1259, 1462, 1674 };

        [Header("화면에 표시할 대상")]
        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private Material whiteBackgroundMaterial;

        [Header("생성된 8방향 시트")]
        [SerializeField] private Texture2D movementSheet;
        [SerializeField] private Texture2D pistolSheet;
        [SerializeField] private Texture2D rifleSheet;

        [Header("재생 설정")]
        [SerializeField, Min(1f)] private float pixelsPerUnit = 128f;
        [SerializeField, Min(1f)] private float movementFramesPerSecond = 8f;
        [SerializeField, Min(1f)] private float firingFramesPerSecond = 14f;

        private PlayerMovement movement;
        private PlayerAim aim;
        private PlayerCombat combat;
        private Sprite[,] movementFrames;
        private Sprite[,] pistolFrames;
        private Sprite[,] rifleFrames;
        private float movementClock;
        private float firingClock;
        private float firingTimeRemaining;
        private int lastDirectionRow;

        // 에디터 적용 메뉴가 프리팹을 저장하기 전에 플레이하더라도 현재 씬의 PlayerMovement에 이 표현 컴포넌트를 보충합니다.
        // 씬이 바뀔 때마다 다시 검사하므로 시작 화면→거점→레이드 순서에서도 새로 생성된 플레이어가 같은 원화를 사용합니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntimeInstaller()
        {
            SceneManager.sceneLoaded -= AttachToPlayersInLoadedScene;
            SceneManager.sceneLoaded += AttachToPlayersInLoadedScene;
        }

        private static void AttachToPlayersInLoadedScene(Scene scene, LoadSceneMode mode)
        {
            if (GeneratedArtResources.LoadTexture("Field/Movement_8Directions_Draft") == null) return;

            foreach (PlayerMovement playerMovement in Object.FindObjectsOfType<PlayerMovement>(true))
            {
                if (playerMovement.GetComponent<PlayerSpriteAnimator>() == null)
                    playerMovement.gameObject.AddComponent<PlayerSpriteAnimator>();
            }
        }

        // 에디터 적용 도구가 프리팹에 렌더러, 시트와 재질을 안전하게 연결할 때 사용합니다.
        // private 필드를 문자열로 직접 수정하지 않고 공개된 한 메서드로 모아 두면 나중에 필드 이름이 바뀌어도 적용 도구를 고치기 쉽습니다.
        public void Configure(
            SpriteRenderer renderer,
            Material whiteKeyMaterial,
            Texture2D movement,
            Texture2D pistol,
            Texture2D rifle)
        {
            targetRenderer = renderer;
            whiteBackgroundMaterial = whiteKeyMaterial;
            movementSheet = movement;
            pistolSheet = pistol;
            rifleSheet = rifle;
        }

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            aim = GetComponent<PlayerAim>();
            combat = GetComponent<PlayerCombat>();

            // 프리팹에 직접 참조가 저장되지 않은 경우 Resources 복사본을 사용합니다.
            // 에디터 적용 도구가 성공한 프로젝트에서는 이미 연결된 참조를 보존하므로 불필요한 다시 로드를 하지 않습니다.
            if (movementSheet == null)
                movementSheet = GeneratedArtResources.LoadTexture("Field/Movement_8Directions_Draft");
            if (pistolSheet == null)
                pistolSheet = GeneratedArtResources.LoadTexture("Field/Pistol_Draft");
            if (rifleSheet == null)
                rifleSheet = GeneratedArtResources.LoadTexture("Field/Rifle_Draft");
            if (whiteBackgroundMaterial == null)
                whiteBackgroundMaterial = GeneratedArtResources.GetSpriteWhiteKeyMaterial();

            // 프리팹 참조가 빠졌더라도 Player 아래의 첫 SpriteRenderer를 찾아 최소한 이동 그림은 표시합니다.
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<SpriteRenderer>();
            if (targetRenderer != null && whiteBackgroundMaterial != null)
                targetRenderer.sharedMaterial = whiteBackgroundMaterial;

            // 원본 PNG는 한 장의 큰 Texture이므로 각 방향과 동작 프레임을 Sprite 객체로 나눠 캐시에 저장합니다.
            // 매 프레임 Sprite.Create를 호출하지 않기 때문에 플레이 중에는 배열에서 이미 만든 그림 하나만 골라 사용합니다.
            movementFrames = CreateFrames(movementSheet);
            pistolFrames = CreateFrames(pistolSheet);
            rifleFrames = CreateFrames(rifleSheet);
        }

        private void OnEnable()
        {
            // 발사 판정 자체는 PlayerCombat이 담당하고, 이 컴포넌트는 성공한 발사 이벤트만 받아 짧은 시각 동작을 시작합니다.
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (combat != null) combat.ShotResolved += HandleShotResolved;
        }

        private void OnDisable()
        {
            if (combat != null) combat.ShotResolved -= HandleShotResolved;
            firingTimeRemaining = 0f;
        }

        private void LateUpdate()
        {
            if (targetRenderer == null || movementFrames == null) return;

            Vector3 velocity = movement != null ? movement.CurrentPlanarVelocity : Vector3.zero;
            bool isMoving = velocity.sqrMagnitude > 0.001f;
            bool isFiring = firingTimeRemaining > 0f;

            // 레이드에서는 걷는 방향과 관계없이 마우스 조준 방향을 우선해 캐릭터가 계속 커서를 바라보게 합니다.
            // 따라서 뒤로 움직이며 앞을 겨누는 식의 스트레이프 동작에서도 몸과 총구 그림이 마우스 반대쪽으로 돌아가지 않습니다.
            // 거점처럼 PlayerAim이 꺼진 장소에서는 이동 방향을 사용해야 자연스럽게 걸어 다니므로 enabled 상태까지 함께 확인합니다.
            Vector3 facingDirection = ResolveFacingDirection(
                aim != null && aim.enabled,
                aim != null ? aim.AimDirection : Vector3.zero,
                velocity,
                lastDirectionRow);
            // 구르기 시작 뒤 마우스를 옮겨도 몸 그림이 중간에 다른 방향으로 바뀌지 않도록 확정된 구르기 방향을 사용합니다.
            if (movement != null && movement.IsRolling) facingDirection = movement.RollDirection;
            lastDirectionRow = DirectionToRow(facingDirection, lastDirectionRow);

            Sprite[,] activeFrames = isFiring ? GetCurrentWeaponFrames() : movementFrames;
            if (activeFrames == null) activeFrames = movementFrames;

            int frameColumn;
            if (isFiring)
            {
                firingClock += Time.deltaTime;
                firingTimeRemaining = Mathf.Max(0f, firingTimeRemaining - Time.deltaTime);
                frameColumn = Mathf.FloorToInt(firingClock * firingFramesPerSecond) % FramesPerDirection;
            }
            else if (isMoving)
            {
                movementClock += Time.deltaTime;
                frameColumn = Mathf.FloorToInt(movementClock * movementFramesPerSecond) % FramesPerDirection;
            }
            else
            {
                // 멈췄을 때 첫 프레임으로 돌아가 발을 든 걷기 자세로 정지하지 않게 합니다.
                movementClock = 0f;
                frameColumn = 0;
            }

            Sprite frame = activeFrames[lastDirectionRow, frameColumn];
            if (frame != null) targetRenderer.sprite = frame;
        }

        private void HandleShotResolved(Vector3 origin, Vector3 endPoint, bool hitSomething)
        {
            // 총알이 맞았는지와 관계없이 실제 한 발이 발사된 경우에만 4프레임 발사 동작을 처음부터 재생합니다.
            // 자동 화기는 다음 발사 이벤트가 들어올 때 시간이 다시 채워져 연사 중 발사 시트가 끊기지 않습니다.
            firingClock = 0f;
            firingTimeRemaining = FramesPerDirection / Mathf.Max(1f, firingFramesPerSecond);
        }

        private Sprite[,] GetCurrentWeaponFrames()
        {
            string itemId = combat != null && combat.ActiveWeaponEntry != null
                ? combat.ActiveWeaponEntry.Item.ItemId
                : string.Empty;

            // 현재 생성된 필드 동작 시트는 권총과 소총용만 있으므로 정확히 일치하는 무기에만 사용합니다.
            // 산탄총과 기관단총은 잘못된 총기 그림을 보여 주지 않고 기본 이동 시트를 유지합니다.
            if (itemId == "weapon_pistol") return pistolFrames;
            if (itemId == "weapon_rifle") return rifleFrames;
            return movementFrames;
        }

        private Sprite[,] CreateFrames(Texture2D sheet)
        {
            if (sheet == null) return null;

            var frames = new Sprite[DirectionCount, FramesPerDirection];
            int[] measuredRowEdges = GetMeasuredRowEdges(sheet.name);
            // 기준 크기와 현재 Texture 크기의 비율을 사용하므로 Unity가 플랫폼에 맞춰 Texture를 줄여도
            // 측정해 둔 경계가 같은 상대 위치를 가리키며 Texture 바깥으로 나가지 않습니다.
            float rowScale = sheet.height / GeneratedSheetReferenceHeight;
            float referenceFrameWorldHeight = sheet.height
                                              / (float)DirectionCount
                                              / Mathf.Max(1f, pixelsPerUnit);
            for (int row = 0; row < DirectionCount; row++)
            for (int column = 0; column < FramesPerDirection; column++)
            {
                // 네 열은 일정하게 놓였으므로 비율로 나누되, 887처럼 정확히 4로 나뉘지 않는 너비는
                // 양쪽 경계를 각각 반올림하여 마지막 열에서도 픽셀이 빠지거나 겹치지 않게 합니다.
                int left = Mathf.RoundToInt(column * sheet.width / (float)FramesPerDirection);
                int right = Mathf.RoundToInt((column + 1) * sheet.width / (float)FramesPerDirection);
                int top = Mathf.Clamp(
                    Mathf.RoundToInt(measuredRowEdges[row] * rowScale),
                    0,
                    sheet.height - 1);
                int bottom = Mathf.Clamp(
                    Mathf.RoundToInt(measuredRowEdges[row + 1] * rowScale),
                    top + 1,
                    sheet.height);

                // PNG의 행은 위에서 아래로 배치되어 있지만 Unity Sprite Rect의 Y=0은 아래쪽입니다.
                // 따라서 현재 행의 아래 경계를 Texture 높이에서 빼서 올바른 방향 그림을 선택합니다.
                var rect = new Rect(left, sheet.height - bottom, right - left, bottom - top);
                // 행마다 잘라 낸 픽셀 높이는 조금 다르지만 게임 월드에서의 표시 높이는 같아야 합니다.
                // 행 높이에 맞춰 PPU를 보정하면 그림의 가로세로 비율을 유지하면서 방향 전환 때 크기가 튀지 않습니다.
                float rowPixelsPerUnit = rect.height / Mathf.Max(0.001f, referenceFrameWorldHeight);
                frames[row, column] = Sprite.Create(
                    sheet,
                    rect,
                    new Vector2(0.5f, 0.5f),
                    Mathf.Max(1f, rowPixelsPerUnit),
                    0,
                    SpriteMeshType.FullRect);
                frames[row, column].name = $"{sheet.name}_Direction{row}_Frame{column}";
            }
            return frames;
        }

        private static int[] GetMeasuredRowEdges(string sheetName)
        {
            // Texture 이름으로 원본 시트를 구분합니다. 새 시트를 연결하면 기존 측정값이 맞지 않을 수 있으므로
            // 알 수 없는 이름에는 기준 높이를 균등하게 8등분한 안전한 기본 경계를 돌려줍니다.
            if (!string.IsNullOrEmpty(sheetName) && sheetName.Contains("Pistol")) return PistolRowEdges;
            if (!string.IsNullOrEmpty(sheetName) && sheetName.Contains("Rifle")) return RifleRowEdges;
            if (!string.IsNullOrEmpty(sheetName) && sheetName.Contains("Movement")) return MovementRowEdges;
            return new[] { 0, 222, 444, 665, 887, 1109, 1330, 1552, 1774 };
        }

        private void OnDestroy()
        {
            // Sprite.Create로 만든 객체는 프로젝트 에셋이 아니라 이 컴포넌트가 소유한 런타임 객체입니다.
            // 플레이 모드를 반복하거나 씬을 바꿀 때 남지 않도록 세 시트에서 만든 Sprite를 명시적으로 정리합니다.
            DestroyFrames(movementFrames);
            DestroyFrames(pistolFrames);
            DestroyFrames(rifleFrames);
        }

        private static void DestroyFrames(Sprite[,] frames)
        {
            if (frames == null) return;
            foreach (Sprite frame in frames)
                if (frame != null) Destroy(frame);
        }

        // 조준 기능이 켜진 레이드에서는 이동 여부와 상관없이 마우스 방향을 반환합니다.
        // 조준 기능이 꺼진 거점에서는 이동 방향을 사용하며, 두 방향이 모두 0이면 마지막 방향 행에 해당하는 벡터를 반환합니다.
        // 이 계산을 별도 함수로 분리해 움직이는 중에도 마우스 방향이 우선되는 규칙을 EditMode 테스트로 확인할 수 있습니다.
        public static Vector3 ResolveFacingDirection(
            bool aimEnabled,
            Vector3 aimDirection,
            Vector3 movementVelocity,
            int fallbackRow = 0)
        {
            aimDirection.y = 0f;
            if (aimEnabled && aimDirection.sqrMagnitude > 0.0001f)
                return aimDirection.normalized;

            movementVelocity.y = 0f;
            if (movementVelocity.sqrMagnitude > 0.0001f)
                return movementVelocity.normalized;

            // 방향 벡터가 없는 정지 상태에서는 이전 행 번호를 다시 월드 방향으로 바꿔 같은 자세를 유지합니다.
            // DirectionToRow의 행 순서와 정확히 반대 변환이므로 45도 단위의 여덟 방향 모두 보존됩니다.
            int safeRow = Mathf.Clamp(fallbackRow, 0, DirectionCount - 1);
            float angle = -safeRow * 45f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle));
        }

        // 생성된 시트의 행 순서는 정면부터 시계 방향이 아니라 정면→왼쪽→등→오른쪽 순으로 배치되어 있습니다.
        // 아래 계산은 월드의 -Z를 정면 행 0, -X를 왼쪽 행 2, +Z를 등 행 4, +X를 오른쪽 행 6으로 변환합니다.
        public static int DirectionToRow(Vector3 direction, int fallbackRow = 0)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return Mathf.Clamp(fallbackRow, 0, DirectionCount - 1);

            float angle = Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
            int row = Mathf.RoundToInt(angle / 45f);
            return (row % DirectionCount + DirectionCount) % DirectionCount;
        }
    }
}
