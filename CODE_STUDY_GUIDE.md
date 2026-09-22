# 2.5D Zombie Survival 코드 학습 안내서

이 문서는 이전 개발 대화를 모르는 새 채팅에서도 프로젝트 코드를 처음부터 차근차근 설명할 수 있도록 만든 길잡이입니다.

코드 자체는 `Assets/_Game/Scripts`에 있으며, 이 문서는 다음 내용을 정리합니다.

- 현재 게임에 구현된 전체 흐름
- 폴더와 코드 파일 79개의 역할
- 서로 연결되는 코드의 호출 순서
- 초보자에게 맞춘 추천 학습 순서
- 다른 채팅에 그대로 붙여 넣을 수 있는 시작 문장

## 1. 프로젝트 한눈에 보기

- Unity 버전: `2022.3.62f2`
- 렌더 파이프라인: URP
- 장르: 3D 월드 위에서 움직이는 2.5D 탑다운 좀비 익스트랙션
- 시작 씬: `Assets/_Game/Scenes/Bootstrap.unity`
- 주요 씬: `Bootstrap -> Hideout -> RaidPrototype -> Hideout`
- 런타임 코드: `Assets/_Game/Scripts`
- EditMode 테스트: `Assets/_Game/Tests/EditMode`
- 현재 컴파일 검증: Runtime, Editor, EditMode와 PlayMode 테스트 어셈블리 경고 0개·오류 0개
- 2026-09-20 전체 Unity 검증: EditMode 테스트 200개와 PlayMode 테스트 3개 통과, 실패·건너뜀 0개
- 현재 자동 테스트 구성: EditMode 200개·PlayMode 3개 시나리오

현재 코드는 프로토타입 단계입니다. 시작 화면, 거점, 전투 정보, 피격, 탈출, 결과와 인벤토리는 모두 수정 가능한 uGUI `Canvas`와 프리팹 구조를 사용합니다. 총성과 피격음은 아직 외부 음원 없이 코드로 생성한 임시 효과음입니다.

### 게임 한 판의 전체 흐름

```text
Bootstrap
  -> 저장된 창고와 출격 준비 목록을 JSON에서 불러옴
  -> Hideout 진입

Hideout
  -> WASD로 물리 거점을 이동하고 E키로 창고 또는 임무 보드를 엶
  -> 임무를 하나 선택하거나 12×10 창고와 10×6 출격 격자를 정리
  -> 출격 버튼을 누름
  -> RaidPrototype 진입

RaidPrototype
  -> 출격 준비 아이템을 플레이어 인벤토리에 복원
  -> 빈 생존 자리를 채우며 시간에 따라 빠른형·튼튼한형 좀비 해금
  -> 이동, 조준, 전투, 시체·필드 상자 루팅, 인벤토리 조작
  -> 퀘스트 아이템·처치 수·최소 생존 시간을 충족해 탈출
  -> 플레이어 사망 시 실패하며 시간 경과만으로는 종료되지 않음

Raid 종료
  -> 현재 인벤토리를 Unity 오브젝트가 아닌 스냅샷 데이터로 복사
  -> 사망이라면 손실 수량만 남기고 소지품·장비·컨테이너 내부를 즉시 전부 제거
  -> 결과를 한 번용 메모리 저장소에 기록
  -> Hideout으로 이동

Hideout 복귀
  -> 탈출 성공 아이템을 창고에 한 번 지급
  -> 사망 결과는 보상을 지급하지 않음
  -> 받은 임무에 실제 좀비 처치 수를 누적
  -> 창고·시설 레벨·임무 진행도를 JSON으로 저장
```

## 2. 코드를 읽기 전에 알아야 할 여섯 가지 데이터 종류

이 프로젝트에서 가장 헷갈리기 쉬운 부분은 비슷해 보이는 아이템 데이터가 여러 형태로 존재한다는 점입니다.

### 2.1 정적 정의: ItemDefinition

`ItemDefinition`은 “9mm 탄약이라는 아이템 종류는 어떤 성질을 가졌는가?”를 저장합니다.

- 아이템 고유 ID와 표시 이름
- 최대 중첩 수량
- 최대 7×7 범위의 실제 점유 모양
- 장착 가능한 장비 슬롯
- 가방 내부 격자 크기
- 탄약이라면 9mm, 5.56mm 또는 12게이지 중 어떤 탄종인지
- 의료품과 음식이라면 한 번 사용할 때 회복할 체력 또는 포만감
- 일반·고급·희귀·퀘스트 중 화면에 표시할 희귀도
- 사용 가능 여부

Unity 프로젝트에 에셋으로 저장되는 `ScriptableObject`이며, 게임을 실행할 때마다 바뀌는 현재 수량이나 위치는 저장하지 않습니다.

### 2.2 실행 중 인스턴스: InventoryEntry

`InventoryEntry`는 “현재 인벤토리에 있는 9mm 탄약 17발 묶음”처럼 실제 한 묶음의 상태입니다.

- 어떤 `ItemDefinition`을 사용하는지
- 현재 수량
- 격자 좌표
- 회전 여부
- 컨테이너라면 내부 격자
- 무기라면 현재 탄창에 남아 있는 실제 장탄 수
- 방탄복이라면 현재 남은 내구도
- 테스트 레이드가 무료로 지급한 장비인지 나타내는 실행 중 표시

같은 `ItemDefinition`을 사용하는 탄약 묶음이 여러 개 존재할 수 있으므로 정의와 인스턴스를 구분해야 합니다.

### 2.3 저장용 복사본: RaidItemSnapshot

`RaidItemSnapshot`은 씬이 바뀌거나 게임을 종료해도 복원할 수 있도록 `InventoryEntry`를 평범한 직렬화 데이터로 복사한 것입니다.

Unity의 `ScriptableObject` 참조 대신 문자열 `itemId`를 저장합니다. 무기의 탄창과 방탄복의 내구도도 함께 저장합니다. 복원할 때 `ItemCatalog`가 이 ID를 다시 `ItemDefinition`으로 바꿉니다.

### 2.4 씬 사이 메모리 상태: RaidResultStore와 HideoutStateStore

- `RaidResultStore`: 방금 끝난 레이드 결과를 Hideout에 한 번 전달합니다.
- `HideoutStateStore`: 현재 창고와 다음 출격 준비 목록을 실행 중 메모리에 보관합니다.

둘 다 `static` 저장소이므로 씬이 바뀌어도 남아 있지만, 프로그램을 종료하면 사라집니다.

### 2.5 디스크 저장: HideoutSaveService

`HideoutSaveService`는 창고, 출격 준비, 시설 레벨, 임무 진행도와 임무별 완료 횟수를 `hideout_save.json`에 저장하고 다시 불러옵니다. 기본 위치는 `Application.persistentDataPath` 아래입니다. 현재 저장 형식은 5번 버전이며, 컨테이너 계층을 `parentIndex`로 평탄화해 저장합니다. 이전 1·2·3·4번 파일도 계속 불러올 수 있습니다.

### 2.6 필드 전리품 정의: LootTableDefinition

`LootTableDefinition`은 “생활 보급 상자에서 어떤 아이템이 어느 정도 확률과 수량으로 나오는가?”를 저장합니다. 상자 안의 실제 아이템은 첫 수색 때 `FieldLootSource`가 생성하며, 생성 완료 상태를 기억해 같은 상자를 다시 열어도 복제되지 않습니다.

```text
ItemDefinition 에셋
       ↓ ItemCatalog가 ID로 찾음
InventoryEntry 실행 상태
       ↓ RaidResultFactory가 복사
RaidItemSnapshot 저장 가능 데이터
       ↓ Store가 씬 사이 전달
HideoutSaveService가 JSON 파일로 보존
```

## 3. 코드 설계에서 반복되는 규칙

### 3.1 UI는 상태를 직접 소유하지 않는다

`InventoryHud`, `AmmoHud`, `ExtractionHud` 같은 HUD 코드는 화면을 그리고 사용자 명령을 전달합니다. 아이템 수량, 탄약, 탈출 진행도 같은 진짜 상태는 각각의 담당 코드가 소유합니다.

예를 들어 인벤토리 드래그는 다음 순서로 실행됩니다.

```text
InventoryHud가 마우스 입력을 받음
  -> PlayerInventory.TryMove를 요청
  -> InventoryMoveResolver가 전체 이동 계획을 계산
  -> 계획 전체가 가능할 때만 PlayerInventory가 실제 상태를 변경
  -> InventoryChanged 이벤트 발생
  -> HUD가 변경된 상태를 다시 그림
```

### 3.2 계산과 실제 변경을 분리한다

인벤토리 교환과 자동 정리는 먼저 가상 계획을 만듭니다. 한 아이템이라도 놓을 수 없으면 실제 목록과 좌표를 전혀 바꾸지 않습니다. 이 방식 때문에 여러 아이템이 겹치는 이동도 중간 상태를 남기지 않고 원래 위치로 돌아갈 수 있습니다. 드래그 미리보기도 이 계획 계산을 그대로 호출하므로 초록색·청록색·노란색으로 보였던 결과와 실제 드롭 결과가 달라지지 않습니다.

### 3.3 이벤트로 결과를 알린다

- `PlayerCombat.ShotResolved`: 발사 시작점, 끝점, 명중 여부 전달
- `Health.Damaged`: 실제 적용 피해량과 피격 위치 전달
- `Health.Died`: 사망 알림
- `PlayerInventory.InventoryChanged`: 인벤토리 변경 알림
- `ExtractionController.ExtractionSucceeded`: 탈출 성공 알림

효과음, 피격 점멸, 결과 처리 코드는 원본 전투 로직에 섞이지 않고 이 이벤트를 구독합니다.

### 3.4 여러 원인이 조작을 잠글 수 있다

인벤토리를 열었을 때뿐 아니라 레이드가 끝났을 때도 플레이어 조작을 막아야 합니다. `PlayerControlGate`는 잠금을 요청한 객체들을 모아 두었다가 마지막 요청자가 잠금을 풀었을 때만 이동·조준·전투·상호작용을 모두 다시 켭니다. 잠금 소유자가 예외적으로 먼저 파괴된 경우에는 HashSet에 남은 Unity의 가짜 null 참조를 제거하고, 실제 소유자가 하나도 없으면 원래 조작 상태를 복원합니다.

### 3.5 테스트 가능한 계산은 static 함수로 분리한다

대각선 속도 계산, 마우스 광선의 평면 투영, 좀비 상태 결정, 스폰 난이도와 가중치 선택, 탈출 수량 조건처럼 GameObject 없이 확인할 수 있는 계산은 정적 함수로 분리되어 있습니다. 그래서 EditMode 테스트에서 씬 전체를 실행하지 않고도 경계값을 검사할 수 있습니다.

## 4. 전체 코드 파일 지도

아래 목록은 현재 코드의 기준 목록입니다. `.meta` 파일은 Unity가 에셋 연결용 GUID를 저장하는 파일이므로 첫 학습에서는 읽지 않아도 됩니다.

### 4.1 AI

- [`ZombieBrain.cs`](Assets/_Game/Scripts/AI/ZombieBrain.cs): 좀비의 `Idle`, `Chase`, `Attack`, `Dead` 상태를 결정하고 이동과 공격 코드에 명령합니다. `Health.Died`를 받으면 죽은 상태로 바뀝니다.
- [`RaidNavigationSurface.cs`](Assets/_Game/Scripts/AI/RaidNavigationSurface.cs): `RaidMap` 자식의 바닥·벽 Collider로 실행 시 NavMesh를 만듭니다. 랜덤 맵은 배치를 마친 뒤 공개 재생성 메서드를 호출할 수 있습니다.
- [`ZombieMovement.cs`](Assets/_Game/Scripts/AI/ZombieMovement.cs): `NavMeshAgent`가 계산한 경로·다른 좀비 회피 방향에 중력과 총기 밀림을 더해 `CharacterController`로 실제 이동합니다. 상태 판단은 하지 않습니다.
- [`ZombieAttack.cs`](Assets/_Game/Scripts/AI/ZombieAttack.cs): 공격 간격을 검사한 뒤 대상의 `IDamageable`에 근접 피해를 전달합니다.
- [`ZombieLootDrop.cs`](Assets/_Game/Scripts/AI/ZombieLootDrop.cs): `Health.Died`를 구독하고 좀비별 확률·수량 목록을 계산해 사망 위치의 시체 격자에 넣습니다.
- [`ZombieArchetype.cs`](Assets/_Game/Scripts/AI/ZombieArchetype.cs): 일반·빠른·튼튼한 좀비의 체력, 속도, 공격력, 표시색, 출현 가중치·해금 시간과 전리품을 저장하는 `ScriptableObject`입니다.
- [`ZombieSpawnPoint.cs`](Assets/_Game/Scripts/AI/ZombieSpawnPoint.cs): Scene 창에서 좀비가 나타날 위치와 방향을 표시하는 마커입니다.
- [`ZombieSpawner.cs`](Assets/_Game/Scripts/AI/ZombieSpawner.cs): 살아 있는 좀비 수를 추적하고, 플레이어에게서 멀고 막히지 않은 지점에 시간별 난이도와 가중치에 맞는 좀비를 생성합니다.

### 4.2 Camera

- [`CameraFollow.cs`](Assets/_Game/Scripts/Camera/CameraFollow.cs): 플레이어 위치를 부드럽게 추적하고 우클릭 정밀 조준 중에는 카메라 FOV 또는 Orthographic Size를 줄여 확대합니다.
- [`SpriteBillboard.cs`](Assets/_Game/Scripts/Camera/SpriteBillboard.cs): 2D 캐릭터 스프라이트가 3D 공간에서 항상 카메라를 바라보게 합니다.

### 4.3 Combat

- [`IDamageable.cs`](Assets/_Game/Scripts/Combat/IDamageable.cs): 피해를 받을 수 있는 객체와 체력 적용 전에 피해를 조정하는 객체가 따라야 하는 최소 약속입니다.
- [`IKnockbackReceiver.cs`](Assets/_Game/Scripts/Combat/IKnockbackReceiver.cs): 총알이나 폭발에 밀릴 수 있는 대상이 외부 수평 속도를 받기 위한 약속입니다.
- [`Health.cs`](Assets/_Game/Scripts/Combat/Health.cs): 최대 체력, 현재 체력, 피해, 회복과 사망을 담당하고 `Damaged`, `HealthChanged`, `Died` 이벤트를 보냅니다.
- [`ArmorProtection.cs`](Assets/_Game/Scripts/Combat/ArmorProtection.cs): 장착 방탄복의 감소율만큼 피해를 내구도로 흡수하고 내구도가 0이면 방어 효과를 제거합니다.
- [`AmmunitionState.cs`](Assets/_Game/Scripts/Combat/AmmunitionState.cs): 현재 무기의 탄창과 인벤토리 예비탄을 HUD에 보여 주기 위한 순수 C# 상태 객체입니다. 실제 탄창은 `InventoryEntry`, 실제 예비탄은 `PlayerInventory`가 소유하며 이 객체는 표시 동기화와 장전 수량 계산을 담당합니다.
- [`WeaponDefinition.cs`](Assets/_Game/Scripts/Combat/WeaponDefinition.cs): 무기 이름, 발사 방식, 피해량, 사거리, 발사·재장전 시간, 탄창, 탄종, 산탄 확산, 명중 오차와 적 밀림을 저장하는 `ScriptableObject`입니다.
- [`HitscanRaycaster.cs`](Assets/_Game/Scripts/Combat/HitscanRaycaster.cs): 재사용 배열로 발사 선상의 충돌 후보를 모으고 플레이어 자신의 Collider를 제외한 가장 가까운 명중점을 찾는 일반 C# 도우미입니다. Player에 붙이는 컴포넌트는 아닙니다.
- [`DamageFlash.cs`](Assets/_Game/Scripts/Combat/DamageFlash.cs): `Health.Damaged`를 구독해 피격된 스프라이트 색을 잠깐 바꿨다가 원래 색으로 되돌립니다.
- [`WeaponFireFeedback.cs`](Assets/_Game/Scripts/Combat/WeaponFireFeedback.cs): `ShotResolved`를 구독해 총구 빛과 짧은 탄도선을 표시합니다.
- [`PrototypeSoundFactory.cs`](Assets/_Game/Scripts/Combat/PrototypeSoundFactory.cs): 외부 음원 파일 없이 발사·피격·사망 파형을 계산해 임시 `AudioClip`을 만듭니다.
- [`WeaponAudioFeedback.cs`](Assets/_Game/Scripts/Combat/WeaponAudioFeedback.cs): 발사 이벤트를 받아 총소리를 재생합니다.
- [`HealthAudioFeedback.cs`](Assets/_Game/Scripts/Combat/HealthAudioFeedback.cs): 피해와 사망 이벤트를 받아 플레이어 또는 좀비 역할에 맞는 임시 소리를 재생합니다.

### 4.4 Player

- [`PlayerMovement.cs`](Assets/_Game/Scripts/Player/PlayerMovement.cs): WASD 입력을 XZ 이동으로 바꾸고 대각선 속도를 정규화하며 Shift 달리기 요청과 중력을 적용합니다.
- [`PlayerSurvival.cs`](Assets/_Game/Scripts/Player/PlayerSurvival.cs): 레이드별 포만감과 기력, 음식 회복, 낮은 포만감 이동 페널티, 달리기 소모·회복·탈진 규칙을 계산합니다.
- [`PlayerAim.cs`](Assets/_Game/Scripts/Player/PlayerAim.cs): 카메라에서 마우스 방향으로 광선을 쏘고 지면 또는 무기 높이 평면과 만나는 점을 구해 수평 조준 방향을 만듭니다.
- [`PlayerSpriteAnimator.cs`](Assets/_Game/Scripts/Player/PlayerSpriteAnimator.cs): 레이드에서는 이동 방향보다 마우스 조준 방향을 우선해 8방향 캐릭터 그림을 고르고, 거점에서는 이동 방향을 사용합니다.
- [`PlayerCombat.cs`](Assets/_Game/Scripts/Player/PlayerCombat.cs): 장착된 주무기·보조무기 선택, 무기별 탄창 소비, 인벤토리 탄약 재장전, 발사 간격과 산탄 펠릿 흐름을 담당합니다. 실제 물리 후보 검색은 `HitscanRaycaster`에 맡깁니다.
- [`PlayerInteraction.cs`](Assets/_Game/Scripts/Player/PlayerInteraction.cs): 플레이어 주변의 가장 가까운 `IInteractable`을 찾아 안내 문구를 표시하고 E키 입력을 전달합니다.
- [`PlayerControlGate.cs`](Assets/_Game/Scripts/Player/PlayerControlGate.cs): 인벤토리 또는 레이드 종료처럼 여러 잠금 사유를 안전하게 합쳐 월드 조작을 켜고 끕니다.

### 4.5 Items

- [`ItemDefinition.cs`](Assets/_Game/Scripts/Items/ItemDefinition.cs): 아이템 종류의 고정 정보를 가진 `ScriptableObject`입니다. 최대 7×7 모양, 중첩, 희귀도, 장비 슬롯, 컨테이너, 탄종, 방탄복 수치와 의료품·음식 회복량을 정의합니다.
- [`ItemCatalog.cs`](Assets/_Game/Scripts/Items/ItemCatalog.cs): 저장된 문자열 `itemId`를 실제 `ItemDefinition` 에셋으로 찾아 주는 목록입니다.
- [`IInteractable.cs`](Assets/_Game/Scripts/Items/IInteractable.cs): 상호작용 가능 여부, 화면 안내 문구와 `Interact` 명령을 통일하는 인터페이스입니다.
- [`WorldItem.cs`](Assets/_Game/Scripts/Items/WorldItem.cs): 레이드 바닥의 단일 아이템입니다. Trigger Collider라 이동을 막지 않으며, 상호작용해 전부 들어간 경우 자신을 제거합니다.
- [`WorldLootContainer.cs`](Assets/_Game/Scripts/Items/WorldLootContainer.cs): 시체와 아이템 상자의 외부 전리품 격자입니다. 시체는 통과 가능하고 상자는 단단한 장애물이 되도록 충돌 규칙을 구분합니다.
- [`LootTableDefinition.cs`](Assets/_Game/Scripts/Items/LootTableDefinition.cs): 상자 종류별 추첨 횟수와 아이템 가중치·수량 범위를 저장하고 테스트 가능한 선택 계산을 제공합니다.
- [`FieldLootSource.cs`](Assets/_Game/Scripts/Items/FieldLootSource.cs): 첫 수색 때 전리품 표를 한 번만 굴려 `WorldLootContainer` 격자에 실제 아이템을 넣습니다.

### 4.6 Inventory

- [`InventoryEntry.cs`](Assets/_Game/Scripts/Inventory/InventoryEntry.cs): 한 아이템 묶음의 수량, 위치, 회전, 내부 컨테이너, 무기 탄창, 방탄복 내구도와 테스트 지급 여부를 보관합니다.
- [`PlayerInventory.cs`](Assets/_Game/Scripts/Inventory/PlayerInventory.cs): 10×6 루트 격자와 장비 슬롯을 소유하는 인벤토리 조정자입니다. 추가·이동·회전·원자적 장비 교환·분할·빠른 이동·시간이 걸리는 사용·안전한 버리기·자동 정리, 탄약 소비와 레이드 실패 시 전체 제거 명령의 공개 입구입니다. 장비 교환 도중 기존 장비를 돌려놓을 공간이 없으면 어떤 상태도 바꾸지 않습니다.
- [`InventoryGridContent.cs`](Assets/_Game/Scripts/Inventory/InventoryGridContent.cs): 플레이어·시체·상자가 공유하는 아이템 추가 계획과 중첩 처리, 재귀 수량 조회와 탄약 소비를 담당합니다. 공간 검사가 모두 끝난 뒤에만 실제 수량과 목록을 변경합니다.
- [`InventoryGridUtility.cs`](Assets/_Game/Scripts/Inventory/InventoryGridUtility.cs): 경계, 실제 점유 모양, 충돌, 빈자리와 가장 가까운 자리 탐색을 담당하는 순수 격자 계산 모음입니다.
- [`InventoryMoveResolver.cs`](Assets/_Game/Scripts/Inventory/InventoryMoveResolver.cs): 드래그 위치에 따라 단순 이동, 중첩 합치기 또는 교환 계획을 만듭니다. 여러 아이템이 밀려나야 하면 큰 모양부터 가까운 안전한 자리를 찾습니다.
- [`InventoryAutoSorter.cs`](Assets/_Game/Scripts/Inventory/InventoryAutoSorter.cs): 같은 중첩 아이템을 가상으로 합친 뒤 배치 제약이 큰 모양과 큰 아이템부터 점수 기반으로 정리합니다. 계산이 전부 성공해야 계획을 반환합니다.
- [`InventoryGridOperations.cs`](Assets/_Game/Scripts/Inventory/InventoryGridOperations.cs): 플레이어 인벤토리와 거점 창고가 같은 이동·교환·회전·분할·자동 정리 적용 규칙을 공유하게 합니다. 실제 이동과 미리보기가 같은 해결기를 사용하며, 컨테이너를 자기 내부나 하위 컨테이너로 넣는 순환 구조도 막습니다.

### 4.7 Extraction

- [`ExtractionRequirement.cs`](Assets/_Game/Scripts/Extraction/ExtractionRequirement.cs): 탈출에 필요한 아이템 ID, 표시 이름과 수량을 저장하는 `ScriptableObject`입니다.
- [`ExtractionController.cs`](Assets/_Game/Scripts/Extraction/ExtractionController.cs): 요구 아이템과 `RaidSession`의 처치·최소 시간 조건, 구역 진입 여부를 함께 검사합니다.

### 4.8 Core와 레이드 수명 주기

- [`GameSceneNavigator.cs`](Assets/_Game/Scripts/Core/GameSceneNavigator.cs): 세 씬 이름을 한곳에 정의하고 Build Settings에 들어 있는 씬만 안전하게 불러오며, 이전 씬의 일시정지 시간 배율이 다음 씬으로 넘어가지 않게 복구합니다.
- [`BootstrapController.cs`](Assets/_Game/Scripts/Core/BootstrapController.cs): 새 실행의 임시 레이드 결과를 비우고 저장 데이터를 불러온 다음 Hideout으로 이동합니다.
- [`RaidLoadoutApplier.cs`](Assets/_Game/Scripts/Core/RaidLoadoutApplier.cs): Hideout에서 선택한 출격 준비 스냅샷을 레이드 시작 시 실제 `InventoryEntry`로 복원합니다. 복원 실패 항목은 창고로 돌려보내고, 준비한 권총이 없으면 보조무기 슬롯에 테스트 권총을 실제 장착합니다.
- [`RaidRules.cs`](Assets/_Game/Scripts/Core/RaidRules.cs): 요구 좀비 처치 수와 최소 탈출 시간을 맵과 분리된 에셋으로 저장합니다. 레이드 제한 시간은 없습니다.
- [`RaidMapLayout.cs`](Assets/_Game/Scripts/Core/RaidMapLayout.cs): 36×36 레이드 맵의 경계, 시작·탈출 지점, 다섯 구역 중심·표시 이름과 좀비 스폰 좌표를 한곳에서 관리합니다.
- [`RaidSession.cs`](Assets/_Game/Scripts/Core/RaidSession.cs): 탈출과 사망을 감시하고 처치 수와 경과 시간을 계산하며 레이드가 단 한 번만 끝나도록 조정합니다. 결과 확정 뒤 플레이어와 좀비 행동을 멈추고, 사망했을 때 손실 수량을 기록한 직후 모든 레이드 아이템을 비웁니다.
- [`RaidResult.cs`](Assets/_Game/Scripts/Core/RaidResult.cs): 정상 탈출과 플레이어 사망 종료 이유, 아이템·격자·장비 스냅샷 자료형 및 결과를 씬 사이 전달하는 `RaidResultStore`를 정의합니다.
- [`RaidResultFactory.cs`](Assets/_Game/Scripts/Core/RaidResultFactory.cs): 탈출 성공이면 현재 인벤토리·장비를 깊게 복사하고, 사망이면 분실 수량만 기록한 `RaidResult`를 만듭니다.
- [`RaidSnapshotUtility.cs`](Assets/_Game/Scripts/Core/RaidSnapshotUtility.cs): 중첩 컨테이너까지 스냅샷을 깊게 복사하고 `ItemCatalog`를 이용해 실제 인벤토리로 복원합니다.

### 4.9 Hideout과 저장

- [`HideoutStateStore.cs`](Assets/_Game/Scripts/Hideout/HideoutStateStore.cs): 창고와 출격 준비 목록을 메모리에 보관합니다. 성공 결과를 한 번만 창고에 반영하고 두 목록 사이 이동을 처리합니다.
- [`HideoutSaveService.cs`](Assets/_Game/Scripts/Hideout/HideoutSaveService.cs): 버전 5 평탄화 JSON을 임시 파일에 먼저 쓴 뒤 교체하고, 1·2·3·4번 파일도 호환해서 읽습니다. 시설 레벨, 공통 임무 진행도와 완료 횟수도 함께 보존합니다.
- [`HideoutController.cs`](Assets/_Game/Scripts/Hideout/HideoutController.cs): 두 거점 격자를 복원하고 임의의 두 격자 사이 이동·분할·보상·시설 강화·제작과 저장을 조정합니다. 이 공통 이동 입구 덕분에 창고와 출격 준비뿐 아니라 가방·조끼 내부도 같은 규칙으로 다룹니다. 창고 확장과 제작법 해금도 시설 레벨에 맞춰 적용합니다.
- [`HideoutFacilityDefinition.cs`](Assets/_Game/Scripts/Hideout/HideoutFacilityDefinition.cs): 시설 이름·종류·레벨 효과와 단계별 실제 아이템 강화 비용을 정의하고, 실행 중 레벨 저장소를 함께 제공합니다.
- [`HideoutFacility.cs`](Assets/_Game/Scripts/Hideout/HideoutFacility.cs): 모든 월드 시설을 공통 E키 대상으로 만들고 강화 레벨에 따라 색과 높이를 갱신합니다.
- [`HideoutWorldController.cs`](Assets/_Game/Scripts/Hideout/HideoutWorldController.cs): 거점 카메라를 따라가게 하고 시설별 Canvas를 하나씩 열며, 화면이 열린 동안 플레이어 조작을 잠급니다.
- [`HideoutMissionDefinition.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionDefinition.cs): 처치·아이템 회수·성공 탈출 목표, 반복 가능 여부와 실제 아이템 보상을 정의합니다.
- [`HideoutMissionStateStore.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionStateStore.cs): 현재 임무의 공통 진행도와 임무별 완료 횟수를 보관하고 레이드 결과를 목표 종류에 맞게 계산합니다.
- [`HideoutMissionRewardService.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionRewardService.cs): 모든 보상이 들어갈 공간을 임시 격자에서 먼저 검사한 뒤 실제 창고에 전량 지급합니다.
- [`HideoutMissionBoard.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionBoard.cs): 보드 레벨에 맞는 임무 선택·수락·보상 수령·포기를 처리하고 변경 직후 저장합니다.
- [`HideoutRecipeDefinition.cs`](Assets/_Game/Scripts/Hideout/HideoutRecipeDefinition.cs): 시설 종류·해금 레벨·재료·결과 아이템을 가진 제작법 데이터입니다.
- [`HideoutCraftingService.cs`](Assets/_Game/Scripts/Hideout/HideoutCraftingService.cs): 발전기 할인, 재료 전량 소비와 결과 아이템 공간을 시뮬레이션한 뒤 제작을 적용합니다.

### 4.10 UI

모든 런타임 화면은 uGUI `Canvas`를 사용합니다. 각 HUD는 게임 상태를 직접 바꾸지 않고 연결된 컨트롤러의 상태를 읽거나 버튼 명령만 전달하므로, 프리팹의 이미지·글꼴·배치를 바꿔도 인벤토리·전투·탈출 규칙은 그대로 유지됩니다.

- [`BootstrapHud.cs`](Assets/_Game/Scripts/UI/BootstrapHud.cs): 저장 불러오기 상태와 Hideout 자동 진입 안내를 표시합니다.
- [`HideoutResultHud.cs`](Assets/_Game/Scripts/UI/HideoutResultHud.cs): 직전 결과와 두 격자를 표시하고 드래그·회전·Ctrl 빠른 이동·Shift 분할·자동 정리·출격 명령을 전달합니다. 가방이나 조끼를 열면 부모 격자와 내부 격자를 나란히 관리하며, 거점에서는 우클릭 빠른 이동을 사용하지 않습니다.
- [`HideoutMissionBoardHud.cs`](Assets/_Game/Scripts/UI/HideoutMissionBoardHud.cs): 선택 임무, 목표, 누적 진행도와 수락·포기 버튼을 표시합니다.
- [`HideoutFacilityUpgradeHud.cs`](Assets/_Game/Scripts/UI/HideoutFacilityUpgradeHud.cs): 현재 시설 효과, 보유/필요 강화 재료와 공개 제작법을 표시하고 강화·제작 명령을 전달합니다.
- [`HideoutWorldHud.cs`](Assets/_Game/Scripts/UI/HideoutWorldHud.cs): 거점 이동 안내와 현재 가까운 시설의 E키 사용 문구를 표시합니다.
- [`AmmoHud.cs`](Assets/_Game/Scripts/UI/AmmoHud.cs): 현재 구역, 탄창, 예비탄, 재장전, 체력·방탄·포만감·기력과 이동 페널티를 표시합니다.
- [`InventoryHud.cs`](Assets/_Game/Scripts/UI/InventoryHud.cs): Canvas 인벤토리의 Tab 열기, 선택, 드래그, R 회전, Shift 분할, Ctrl 빠른 이동, 우클릭 사용·장착, 장비 슬롯 교환, 두 번 확인하는 Delete 버리기와 자동 정리를 조정합니다. 사용 진행률과 모든 주요 실패 이유를 보여 주고, 열린 동안 월드 조작을 잠그며 페이드·크기 전환을 재생합니다.
- 시체나 아이템 상자를 E키로 수색하면 `PlayerInteraction.LootContainerOpened` 이벤트를 받아 보조 격자에 외부 전리품을 표시합니다. 레이드에서는 전리품·가방·조끼 탭을 눌러 필요한 보조 격자를 언제든 다시 선택할 수 있습니다.
- [`IInventoryGridViewOwner.cs`](Assets/_Game/Scripts/UI/IInventoryGridViewOwner.cs): 레이드 인벤토리와 거점이 같은 격자 프리팹의 클릭·드래그 명령을 받을 수 있게 하는 공통 약속입니다.
- [`InventoryDragSession.cs`](Assets/_Game/Scripts/UI/InventoryDragSession.cs): 레이드와 거점 HUD가 공유하는 드래그 시작·미리보기·드롭 좌표·취소 상태입니다. Canvas에 붙이지 않는 일반 C# 객체이며 실제 이동 가능 여부는 판단하지 않습니다.
- [`InventoryGridView.cs`](Assets/_Game/Scripts/UI/InventoryGridView.cs): 플레이어·가방·조끼·전리품·거점의 `GridData`를 빈 칸과 아이템 프리팹으로 다시 그리고 화면 좌표를 격자 좌표로 변환합니다. 드래그 중 이동은 초록색, 중첩은 청록색, 교환은 노란색, 불가능은 빨간색 반투명 칸으로 그립니다.
- [`InventoryItemView.cs`](Assets/_Game/Scripts/UI/InventoryItemView.cs): 실제 점유 모양만 Image로 만들며 Ctrl·Shift 상태가 포함된 클릭과 드래그 이벤트를 현재 격자 화면 소유자에게 전달합니다.
- [`ItemDetailFormatter.cs`](Assets/_Game/Scripts/UI/ItemDetailFormatter.cs): 아이템 상세, 방탄복 상태와 같은 슬롯 무기의 좋음·나쁨 수치 비교 문구를 만듭니다.
- [`EquipmentSlotView.cs`](Assets/_Game/Scripts/UI/EquipmentSlotView.cs): 장비 슬롯 한 칸의 이름, 장착 아이템, 현재 사용 무기와 테스트 지급 여부를 표시합니다. 장착 아이템을 격자로 드래그해 해제하거나 격자 아이템을 슬롯에 놓아 안전하게 교환하는 입력도 전달합니다.
- [`ExtractionHud.cs`](Assets/_Game/Scripts/UI/ExtractionHud.cs): 탈출 조건, 활성화 여부, 남은 시간과 성공 상태를 표시합니다.
- [`PlayerDamageHud.cs`](Assets/_Game/Scripts/UI/PlayerDamageHud.cs): 플레이어가 피해를 받으면 붉은 화면 오버레이를 잠깐 표시합니다.
- [`RaidResultHud.cs`](Assets/_Game/Scripts/UI/RaidResultHud.cs): 레이드 경과 시간·처치 조건을 실시간 표시하고 탈출과 사망 결과를 구분합니다.

### 4.11 Editor 전용 도구

이 폴더 코드는 빌드된 게임에서 실행되는 게임 규칙이 아니라 Unity 에디터 안에서 개발을 돕는 도구입니다.

- [`ItemDefinitionEditor.cs`](Assets/_Game/Scripts/Editor/ItemDefinitionEditor.cs): `ItemDefinition` 인스펙터에 최대 7×7 셀 편집기를 추가합니다. 셀을 눌러 실제 점유 모양을 직접 그릴 수 있습니다.
- [`PrototypeContentBuilder.cs`](Assets/_Game/Scripts/Editor/PrototypeContentBuilder.cs): 아이템 정의, 카탈로그, 모든 Canvas UI 프리팹, 36×36 다섯 구역 레이드 맵, 세 씬과 Build Settings를 한꺼번에 초기 상태로 되돌리는 수동 에디터 도구입니다. 누락 검사를 자동 실행하지 않으며 전체 테스트 콘텐츠를 다시 만들 때만 사용합니다.

### 4.12 어셈블리 정의

- [`ZombieSurvival.Runtime.asmdef`](Assets/_Game/Scripts/ZombieSurvival.Runtime.asmdef): 런타임 코드를 하나의 컴파일 단위로 묶습니다.
- [`ZombieSurvival.Editor.asmdef`](Assets/_Game/Scripts/Editor/ZombieSurvival.Editor.asmdef): Editor 전용 코드가 런타임 빌드에 들어가지 않도록 분리합니다.
- [`ZombieSurvival.EditModeTests.asmdef`](Assets/_Game/Tests/EditMode/ZombieSurvival.EditModeTests.asmdef): 테스트 코드가 런타임 어셈블리를 참조하도록 구성합니다.
- [`ZombieSurvival.PlayModeTests.asmdef`](Assets/_Game/Tests/PlayMode/ZombieSurvival.PlayModeTests.asmdef): 실제 생명주기와 씬 연결을 검사하는 PlayMode 테스트를 분리합니다.

## 5. 중요한 기능의 실제 호출 흐름

### 5.1 이동과 조준

```text
PlayerMovement.Update
  -> WASD 입력 읽기
  -> Shift와 이동 입력이 함께 있으면 PlayerSurvival에 달리기 요청
  -> 기력을 소모하거나 회복하고 탈진 여부에 따라 달리기 배율 결정
  -> CalculatePlanarVelocity로 대각선 속도 정규화
  -> 중력 추가
  -> CharacterController.Move

PlayerAim.Update
  -> 카메라에서 마우스 방향 Ray 생성
  -> 무기 높이의 수평 평면에 투영
  -> 플레이어에서 투영점까지 XZ 방향 계산
  -> visual과 weaponSocket의 수평 방향 갱신
```

총알을 카메라의 기울어진 방향 그대로 발사하지 않고 무기 높이의 수평면으로 투영하기 때문에, 현재 발사는 바닥 쪽으로 기울어지지 않습니다.

### 5.2 발사부터 피격까지

```text
PlayerCombat.Update
  -> 1키는 장착 주무기, 2키는 장착 보조무기 선택
  -> 선택한 InventoryEntry의 탄창 수와 인벤토리의 맞는 탄종 수량을 HUD 상태에 동기화
  -> WeaponDefinition에서 반자동·자동 입력 방식, 명중 오차와 전투 수치 읽기
  -> 왼쪽 마우스 입력
  -> CanFire로 탄약·재장전·발사 간격 확인
  -> InventoryEntry.TryConsumeMagazineRound로 해당 총기의 탄창에서 1발 소비
  -> 펠릿 수가 1이면 조준 정면, 여러 개면 전체 확산 각도에 같은 간격으로 방향 계산
  -> 각 펠릿 방향으로 Physics.RaycastAll
  -> 플레이어 자신의 Collider 제외
  -> 펠릿마다 가장 가까운 유효 IDamageable에 TakeDamage
  -> IKnockbackReceiver를 구현한 적에는 무기별 밀림 전달
  -> 펠릿 수와 관계없이 방아쇠 한 번당 ShotResolved 이벤트 한 번

ShotResolved
  -> WeaponFireFeedback: 탄도선과 총구 빛
  -> WeaponAudioFeedback: 총소리

Health.TakeDamage
  -> Damaged: 색상 점멸, 피격음, 화면 피격 표시
  -> 체력이 0이면 Died: AI 사망, 레이드 종료 또는 사망음
```

장비 슬롯에 유효한 무기가 없으면 `Ammo`가 만들어지지 않아 발사와 재장전이 모두 거부됩니다. 실제 테스트 레이드는 `RaidLoadoutApplier`가 보조무기 슬롯에 권총 `InventoryEntry`를 장착해 시작합니다. 탄창은 각 무기의 `InventoryEntry`에 저장되므로 두 발을 사용한 소총에서 권총으로 바꿨다가 돌아와도 소총 탄창이 다시 가득 차지 않습니다.

```text
R키 재장전
  -> 현재 무기가 필요로 하는 탄종과 빈 탄창 수량 계산
  -> PlayerInventory가 루트·가방·전술 조끼 안에서 맞는 탄종만 검색
  -> 재장전 시간이 끝난 뒤 필요한 양만 탄약 묶음에서 소비
  -> InventoryEntry의 탄창에 실제 소비한 수량만 추가
  -> HUD의 탄창과 남은 인벤토리 탄약을 다시 동기화
```

권총과 기관단총은 9mm, 소총은 5.56mm, 산탄총은 12게이지를 사용합니다. 예를 들어 권총 탄창에 3발이 남고 인벤토리에 9mm가 2발뿐이면, 재장전 후 탄창은 5발이 되고 9mm 묶음은 사라집니다. 인벤토리에 다른 탄종이 많이 있어도 권총 재장전에는 사용되지 않습니다. 산탄총은 일곱 펠릿을 계산해도 탄창에서는 12게이지 쉘 한 발만 소비합니다.

테스트 지급 권총에는 `IsRaidProvided`가 표시됩니다. 이 권총은 버릴 수 없고 `RaidResultFactory`가 성공 보상과 사망 손실 수량에서 제외하므로 레이드를 반복해도 창고에 무료 권총이 계속 복제되지 않습니다. 레이드가 실패하면 다른 장비와 함께 실제 인벤토리에서는 제거되지만, 무료로 받은 물건이라 결과 화면의 손실 개수에는 세지 않습니다.

### 5.3 월드 아이템 획득

```text
PlayerInteraction
  -> 주변 Collider 검색
  -> 가장 가까운 IInteractable 선택
  -> E키 입력
  -> WorldItem.Interact
  -> PlayerInventory.TryAdd
  -> 기존 중첩에 먼저 채우고 새 묶음의 빈자리 탐색
  -> 전체 수량이 들어갈 수 있을 때만 상태 변경
  -> 성공하면 WorldItem 제거
```

### 5.4 아이템 드래그와 교환

```text
InventoryHud
  -> InventoryItemView에서 드래그 시작 이벤트를 받음
  -> InventoryGridView가 놓은 셀을 계산
  -> PlayerInventory.TryMove
  -> InventoryMoveResolver.TryResolve

겹침 없음: Move 계획
같은 중첩 아이템 하나: Merge 계획
다른 아이템 또는 여러 아이템: Swap 계획

Swap 계획
  -> 드래그한 아이템은 사용자가 놓은 자리에 유지
  -> 밀려나는 아이템을 실제 점유 칸 수가 큰 순서로 정렬
  -> 원래 자리 근처의 가장 가까운 안전한 위치 탐색
  -> 모두 성공하면 PlayerInventory가 한꺼번에 적용
  -> 하나라도 실패하면 아무것도 변경하지 않음
```

### 5.5 자동 정리

```text
InventoryHud의 자동 정리 버튼
  -> PlayerInventory.TryAutoSort
  -> InventoryAutoSorter.TryCreatePlan
  -> 같은 아이템 중첩을 가상 통합
  -> 후보 자리가 적고 실제 점유 칸이 큰 아이템 우선
  -> 각 위치와 회전 상태의 배치 점수 비교
  -> 모든 유닛을 배치할 수 있을 때만 계획 반환
  -> PlayerInventory가 수량·위치·회전을 한 번에 반영
```

컨테이너 내부가 있는 아이템은 데이터 손실을 막기 위해 다른 중첩과 합치지 않고 독립된 유닛으로 유지합니다.

### 5.6 인벤토리 전환 효과와 조작 잠금

```text
소모품 우클릭
  -> PlayerInventory.GetUseFailureReason으로 체력·포만감과 소유 상태 확인
  -> 치료 1.5초 또는 음식 1.2초 진행률 표시
  -> 완료 시에만 실제 효과 적용과 수량 1개 소비
  -> 창을 닫으면 진행 중 사용 취소

Tab 입력
  -> InventoryHud.SetOpen(true)
  -> PlayerControlGate.AcquireLock(InventoryHud)
  -> PlayerMovement, PlayerAim, PlayerCombat, PlayerInteraction 비활성화
  -> InventoryPanel 활성화
  -> CanvasGroup 알파 0→1, 패널 크기 0.94→1로 전환

Tab 입력 또는 HUD 비활성화
  -> PlayerControlGate.ReleaseLock(InventoryHud)
  -> 다른 잠금 요청자가 없을 때만 네 조작 컴포넌트 재활성화
  -> CanvasGroup 알파 1→0, 패널 크기 1→0.94로 전환한 뒤 패널 비활성화
```

조작 잠금은 `Time.timeScale`을 멈추지 않습니다. 전환 효과도 `Time.unscaledDeltaTime`을 사용하므로 다른 시스템이 시간을 멈추더라도 끝까지 재생됩니다.

### 5.7 탈출부터 창고 저장까지

```text
ExtractionController
  -> PlayerInventory.CountItem으로 요구 수량 확인
  -> RaidSession에서 처치 수와 최소 생존 시간 확인
  -> 탈출 구역 안에 있으면 카운트다운
  -> 조건이 깨지거나 구역을 나가면 진행도 초기화
  -> 성공 시 ExtractionSucceeded

RaidSession
  -> 경과 시간과 좀비 처치 수 계산
  -> 시간 경과만으로는 레이드를 종료하지 않음
  -> 종료 중복 방지
  -> 플레이어 조작 잠금
  -> RaidResultFactory.Create
  -> RaidResultStore.Set
  -> 결과 표시 후 Hideout 씬 로드

HideoutStateStore.ApplyPendingRaidResult
  -> 탈출 성공 아이템을 창고에 깊은 복사
  -> 사망 결과는 창고에 추가하지 않음
  -> RaidResultStore.Clear로 중복 지급 방지

HideoutController
  -> 창고와 출격 준비 스냅샷을 실제 두 격자로 복원
  -> 이동·교환·회전·자동 정리를 InventoryGridOperations에 요청
  -> 성공한 변경만 다시 스냅샷으로 변환
  -> HideoutSaveService.SaveDefault
```

## 6. 추천 학습 순서

한 번에 전체 파일을 설명받기보다 아래 순서대로 한 수업에 1~2개 파일만 읽는 것이 좋습니다.

### 0단계: C# 객체와 Unity 컴포넌트의 차이

읽을 파일:

1. [`AmmunitionState.cs`](Assets/_Game/Scripts/Combat/AmmunitionState.cs)
2. [`PlayerCombat.cs`](Assets/_Game/Scripts/Player/PlayerCombat.cs)의 필드와 `Awake`만

목표: `new`로 만드는 순수 C# 객체와 GameObject에 붙는 `MonoBehaviour`의 차이를 이해합니다.

### 1단계: 인터페이스와 이벤트

읽을 파일:

1. [`IDamageable.cs`](Assets/_Game/Scripts/Combat/IDamageable.cs)
2. [`Health.cs`](Assets/_Game/Scripts/Combat/Health.cs)

목표: 공격자가 대상의 구체적인 종류를 몰라도 피해를 주는 방법과 이벤트 구독 방식을 이해합니다.

### 2단계: 플레이어 이동, 카메라와 조준

읽을 파일:

1. [`PlayerMovement.cs`](Assets/_Game/Scripts/Player/PlayerMovement.cs)
2. [`PlayerSurvival.cs`](Assets/_Game/Scripts/Player/PlayerSurvival.cs)
3. [`CameraFollow.cs`](Assets/_Game/Scripts/Camera/CameraFollow.cs)
4. [`PlayerAim.cs`](Assets/_Game/Scripts/Player/PlayerAim.cs)
5. [`SpriteBillboard.cs`](Assets/_Game/Scripts/Camera/SpriteBillboard.cs)

목표: `Update`, `LateUpdate`, 시간에 따른 수치 감소, 이동 배율, 벡터, Ray와 평면 투영을 이해합니다.

### 3단계: 전투와 피드백

읽을 파일:

1. [`WeaponDefinition.cs`](Assets/_Game/Scripts/Combat/WeaponDefinition.cs)
2. [`PlayerCombat.cs`](Assets/_Game/Scripts/Player/PlayerCombat.cs)
3. [`WeaponFireFeedback.cs`](Assets/_Game/Scripts/Combat/WeaponFireFeedback.cs)
4. [`DamageFlash.cs`](Assets/_Game/Scripts/Combat/DamageFlash.cs)
5. [`WeaponAudioFeedback.cs`](Assets/_Game/Scripts/Combat/WeaponAudioFeedback.cs)
6. [`HealthAudioFeedback.cs`](Assets/_Game/Scripts/Combat/HealthAudioFeedback.cs)
7. [`PrototypeSoundFactory.cs`](Assets/_Game/Scripts/Combat/PrototypeSoundFactory.cs)

목표: 정적인 무기 수치, 무기마다 변하는 탄약 상태와 시청각 표현을 서로 분리하는 이유를 이해합니다.

### 4단계: 좀비 AI

읽을 파일:

1. [`ZombieBrain.cs`](Assets/_Game/Scripts/AI/ZombieBrain.cs)
2. [`ZombieMovement.cs`](Assets/_Game/Scripts/AI/ZombieMovement.cs)
3. [`ZombieAttack.cs`](Assets/_Game/Scripts/AI/ZombieAttack.cs)
4. [`ZombieArchetype.cs`](Assets/_Game/Scripts/AI/ZombieArchetype.cs)
5. [`ZombieSpawnPoint.cs`](Assets/_Game/Scripts/AI/ZombieSpawnPoint.cs)
6. [`ZombieSpawner.cs`](Assets/_Game/Scripts/AI/ZombieSpawner.cs)
7. [`ZombieLootDrop.cs`](Assets/_Game/Scripts/AI/ZombieLootDrop.cs)

목표: 상태 결정, 이동 실행, 공격 실행을 나눈 간단한 상태 머신을 이해합니다.

### 5단계: 아이템 정의와 상호작용

읽을 파일:

1. [`ItemDefinition.cs`](Assets/_Game/Scripts/Items/ItemDefinition.cs)
2. [`ItemCatalog.cs`](Assets/_Game/Scripts/Items/ItemCatalog.cs)
3. [`IInteractable.cs`](Assets/_Game/Scripts/Items/IInteractable.cs)
4. [`WorldItem.cs`](Assets/_Game/Scripts/Items/WorldItem.cs)
5. [`LootTableDefinition.cs`](Assets/_Game/Scripts/Items/LootTableDefinition.cs)
6. [`FieldLootSource.cs`](Assets/_Game/Scripts/Items/FieldLootSource.cs)
7. [`WorldLootContainer.cs`](Assets/_Game/Scripts/Items/WorldLootContainer.cs)
8. [`PlayerInteraction.cs`](Assets/_Game/Scripts/Player/PlayerInteraction.cs)

목표: `ScriptableObject`, ID 기반 조회와 상호작용 인터페이스를 이해합니다.

### 6단계: 격자 인벤토리의 기초

읽을 파일:

1. [`InventoryEntry.cs`](Assets/_Game/Scripts/Inventory/InventoryEntry.cs)
2. [`InventoryGridUtility.cs`](Assets/_Game/Scripts/Inventory/InventoryGridUtility.cs)
3. [`PlayerInventory.cs`](Assets/_Game/Scripts/Inventory/PlayerInventory.cs)의 `GridData`, `TryAdd`, `CanAdd`

목표: 직사각형 크기와 실제 점유 셀이 다른 아이템, 회전 좌표와 충돌 검사를 이해합니다.

### 7단계: 이동, 교환과 자동 정리

읽을 파일:

1. [`InventoryMoveResolver.cs`](Assets/_Game/Scripts/Inventory/InventoryMoveResolver.cs)
2. [`InventoryAutoSorter.cs`](Assets/_Game/Scripts/Inventory/InventoryAutoSorter.cs)
3. [`PlayerInventory.cs`](Assets/_Game/Scripts/Inventory/PlayerInventory.cs)의 나머지 명령

목표: 먼저 계획하고 성공했을 때만 적용하는 원자적 변경과 배치 우선순위 알고리즘을 이해합니다.

### 8단계: 인벤토리 화면과 조작 잠금

읽을 파일:

1. [`InventoryHud.cs`](Assets/_Game/Scripts/UI/InventoryHud.cs)
2. [`InventoryGridView.cs`](Assets/_Game/Scripts/UI/InventoryGridView.cs)
3. [`InventoryItemView.cs`](Assets/_Game/Scripts/UI/InventoryItemView.cs)
4. [`EquipmentSlotView.cs`](Assets/_Game/Scripts/UI/EquipmentSlotView.cs)
5. [`PlayerControlGate.cs`](Assets/_Game/Scripts/Player/PlayerControlGate.cs)

목표: 화면 입력과 실제 데이터 명령을 분리하는 방법, 여러 잠금 소유자를 처리하는 이유, `CanvasGroup`과 코루틴으로 시간 정지와 무관한 화면 전환을 만드는 방법을 이해합니다.

### 9단계: 탈출과 레이드 종료

읽을 파일:

1. [`ExtractionRequirement.cs`](Assets/_Game/Scripts/Extraction/ExtractionRequirement.cs)
2. [`ExtractionController.cs`](Assets/_Game/Scripts/Extraction/ExtractionController.cs)
3. [`RaidSession.cs`](Assets/_Game/Scripts/Core/RaidSession.cs)
4. [`ExtractionHud.cs`](Assets/_Game/Scripts/UI/ExtractionHud.cs)
5. [`RaidResultHud.cs`](Assets/_Game/Scripts/UI/RaidResultHud.cs)

목표: 조건 검사, 카운트다운 상태와 종료 중복 방지 흐름을 이해합니다.

### 10단계: 스냅샷과 씬 사이 데이터 전달

읽을 파일:

1. [`RaidResult.cs`](Assets/_Game/Scripts/Core/RaidResult.cs)
2. [`RaidResultFactory.cs`](Assets/_Game/Scripts/Core/RaidResultFactory.cs)
3. [`RaidSnapshotUtility.cs`](Assets/_Game/Scripts/Core/RaidSnapshotUtility.cs)
4. [`RaidLoadoutApplier.cs`](Assets/_Game/Scripts/Core/RaidLoadoutApplier.cs)

목표: Unity 참조를 저장용 데이터로 복사하는 이유와 컨테이너 깊은 복사를 이해합니다.

### 11단계: Hideout과 JSON 저장

읽을 파일:

1. [`HideoutStateStore.cs`](Assets/_Game/Scripts/Hideout/HideoutStateStore.cs)
2. [`HideoutSaveService.cs`](Assets/_Game/Scripts/Hideout/HideoutSaveService.cs)
3. [`HideoutController.cs`](Assets/_Game/Scripts/Hideout/HideoutController.cs)
4. [`HideoutFacilityDefinition.cs`](Assets/_Game/Scripts/Hideout/HideoutFacilityDefinition.cs)
5. [`HideoutFacility.cs`](Assets/_Game/Scripts/Hideout/HideoutFacility.cs)
6. [`HideoutWorldController.cs`](Assets/_Game/Scripts/Hideout/HideoutWorldController.cs)
7. [`HideoutMissionDefinition.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionDefinition.cs)
8. [`HideoutMissionStateStore.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionStateStore.cs)
9. [`HideoutMissionBoard.cs`](Assets/_Game/Scripts/Hideout/HideoutMissionBoard.cs)
10. [`HideoutResultHud.cs`](Assets/_Game/Scripts/UI/HideoutResultHud.cs)
11. [`HideoutMissionBoardHud.cs`](Assets/_Game/Scripts/UI/HideoutMissionBoardHud.cs)
12. [`HideoutWorldHud.cs`](Assets/_Game/Scripts/UI/HideoutWorldHud.cs)
13. [`BootstrapController.cs`](Assets/_Game/Scripts/Core/BootstrapController.cs)
14. [`GameSceneNavigator.cs`](Assets/_Game/Scripts/Core/GameSceneNavigator.cs)

목표: 메모리 상태, 씬 이동, 디스크 저장과 한 번만 적용되는 결과 처리의 차이를 이해합니다.

### 12단계: 에디터 자동화와 테스트

읽을 파일:

1. [`ItemDefinitionEditor.cs`](Assets/_Game/Scripts/Editor/ItemDefinitionEditor.cs)
2. [`PrototypeContentBuilder.cs`](Assets/_Game/Scripts/Editor/PrototypeContentBuilder.cs)
3. `Assets/_Game/Tests/EditMode` 아래 테스트 파일

목표: 실제 게임 코드와 개발 도구를 분리하는 법, 버그 재발을 테스트로 막는 법을 이해합니다.


## 7. 테스트 파일 지도

- [`CombatTests.cs`](Assets/_Game/Tests/EditMode/CombatTests.cs): 탄약, 발사 조건, 무장 해제 발사 차단, 장착 무기 수치 적용, 무기 전환 후 탄창 유지, 탄종별 소비, 산탄 확산 각도와 한 쉘 소비, 부족한 탄약의 부분 재장전, 잘못된 무기 데이터 거부, 피해 이벤트, 발사 결과, 다중 조작 잠금과 파괴된 잠금 소유자 정리, 인벤토리 잠금과 Canvas 드롭 좌표, 임시 음원, 상호작용·탄약·탈출 표시 문구와 인벤토리 전환 곡선을 검사합니다.
- [`MovementAndAimTests.cs`](Assets/_Game/Tests/EditMode/MovementAndAimTests.cs): 대각선 이동 정규화와 Ray의 지면·무기 높이 평면 투영을 검사합니다.
- [`SurvivalTests.cs`](Assets/_Game/Tests/EditMode/SurvivalTests.cs): 포만감 이동 속도, 기력 소모·회복, 소모품 효과·소비 방지·실패 이유와 HUD 문구를 검사합니다.
- [`RaidMapLayoutTests.cs`](Assets/_Game/Tests/EditMode/RaidMapLayoutTests.cs): 시작·탈출·구역·스폰 위치와 대표 좌표의 현재 구역 표시 이름을 검사합니다.
- [`ZombieAITests.cs`](Assets/_Game/Tests/EditMode/ZombieAITests.cs): NavMesh 목적지 갱신 간격·이동 임계값·회피 우선순위, 상태 전환과 스폰 난이도·생존 제한·생성 간격·해금·가중치 선택을 검사합니다.
- [`InventoryTests.cs`](Assets/_Game/Tests/EditMode/InventoryTests.cs): 중첩, 공간 부족 롤백, 회전, 장착, 분할, 사용자 정의 모양, 소총·탄약 교환, 다중 겹침, 자동 정리, 미리보기와 실제 계획 일치, 상태를 보존하는 장비 교환, 자기 컨테이너 이동 금지와 버리기 실패 이유를 검사합니다.
- [`ExtractionTests.cs`](Assets/_Game/Tests/EditMode/ExtractionTests.cs): 요구 수량의 경계값과 카운트다운 진행·취소를 검사합니다.
- [`RaidResultTests.cs`](Assets/_Game/Tests/EditMode/RaidResultTests.cs): 성공 결과의 인벤토리 보존, 사망 결과의 보상 차단, 루트·장비·컨테이너 내부 전체 제거와 테스트 지급 권총의 보상·손실 제외를 검사합니다.
- [`HideoutTests.cs`](Assets/_Game/Tests/EditMode/HideoutTests.cs): 보상 1회 지급, 창고·출격 이동, 컨테이너 복원, 임무 세 목표·완료 기록·보상 공간 검사, 시설 효과·제작 할인과 버전 5 저장을 검사합니다.
- [`RoadmapFeatureTests.cs`](Assets/_Game/Tests/EditMode/RoadmapFeatureTests.cs): 방어구, 좀비 드롭, 필드 전리품 추첨과 중복 생성 방지, 레이드 조건, 두 격자 이동 롤백, 현재 버전 5 저장과 버전 1·2·3 호환, 상세 설명 문구를 검사합니다.
- [`ItemRarityTests.cs`](Assets/_Game/Tests/EditMode/ItemRarityTests.cs): 네 희귀도의 한글 이름과 인벤토리 아이템 색상 구분을 검사합니다.
- [`RoadmapPlayModeTests.cs`](Assets/_Game/Tests/PlayMode/RoadmapPlayModeTests.cs): 실제 방어구 피해 적용, 좀비 사망 드롭과 세 게임 씬의 핵심 시스템 연결을 실행 중에 검사합니다.

Unity에서 `Window > General > Test Runner`를 열고 EditMode와 PlayMode 탭의 테스트를 실행할 수 있습니다. 2026-09-20 기준 EditMode 200개와 PlayMode 3개가 모두 통과했으며 실패하거나 건너뛴 테스트는 없습니다. 플레이어 테스트 빌드는 사용량을 아끼고 최종 이미지 리소스를 반영한 화면까지 함께 확인하기 위해 마지막 안정화 단계로 미뤘습니다.

## 8. 새 채팅에서 설명받을 때 지켜 달라고 할 규칙

새 채팅에는 다음 규칙을 함께 전달하는 것이 좋습니다.

1. 이 안내서를 먼저 끝까지 읽고 실제 파일을 직접 열어 확인합니다.
2. 한 답변에서 너무 많은 파일을 설명하지 말고 한 개념 또는 한 파일씩 진행합니다.
3. 초보자가 이해할 수 있는 한국어를 사용하고 어려운 용어는 처음 등장할 때 뜻을 풉니다.
4. 각 파일은 “왜 존재하는가 → 필드 → Unity 생명 주기 → 메서드 → 다른 코드와의 연결 → 실제 예시” 순서로 설명합니다.
5. 코드에 적힌 `//` 주석도 빠뜨리지 말고, 주석의 뜻을 더 쉬운 말로 풀어 줍니다.
6. 좌표, 수량과 상태 변화는 구체적인 숫자 예시를 들어 설명합니다.
7. 설명만 요청했을 때는 코드를 수정하지 않습니다.
8. 한 수업이 끝나면 핵심 요약과 짧은 확인 문제를 내고, 사용자가 이해했다고 한 뒤 다음 수업으로 넘어갑니다.
9. 문서 설명과 실제 코드가 다르면 실제 코드를 기준으로 하고 차이를 알려 줍니다.
10. 새 기능을 만들 때도 기존 방식처럼 자세한 한국어 `//` 주석을 작성합니다.

## 9. 다른 채팅에 복사할 시작 문장

아래 내용을 새 채팅에 그대로 붙여 넣으면 됩니다.

```text
C:\Users\User\Desktop\2.5D_Zombie_Survival\CODE_STUDY_GUIDE.md를 먼저 끝까지 읽고,
그 문서에 연결된 실제 C# 파일도 직접 확인해 줘.

나는 Unity와 C# 코드를 천천히 배우고 싶어. 한 번에 전체를 짧게 요약하지 말고
CODE_STUDY_GUIDE.md의 추천 학습 순서대로 한 수업에 한 개념 또는 1~2개 파일만 설명해 줘.

각 수업은 다음 순서를 지켜 줘.
1. 이 코드가 왜 필요한지
2. 클래스와 필드가 무엇을 저장하는지
3. Unity가 언제 이 코드를 호출하는지
4. 각 메서드가 위에서 아래로 무엇을 하는지
5. 다른 파일이 이 코드를 어떻게 호출하는지
6. 실제 숫자나 플레이 상황을 이용한 예시
7. 핵심 요약과 짧은 확인 문제

어려운 용어는 처음 나올 때 쉬운 한국어로 설명하고, 코드 속 자세한 // 주석도 함께 풀어 줘.
내가 이해했다고 하기 전에는 다음 단계로 너무 빨리 넘어가지 말아 줘.
설명만 하는 동안에는 코드를 수정하지 말고, 실제 코드와 안내서가 다르면 실제 코드를 기준으로 알려 줘.

첫 수업은 Assets/_Game/Scripts/Combat/AmmunitionState.cs부터 시작해 줘.
```

## 10. 안내서 유지 규칙

앞으로 새 시스템이나 새 코드 파일이 추가되면 다음 세 곳을 함께 갱신하는 것이 좋습니다.

1. 이 문서의 전체 코드 파일 지도
2. 중요한 기능의 실제 호출 흐름
3. 추천 학습 순서와 테스트 파일 지도

이렇게 유지하면 다른 채팅에서도 프로젝트가 어느 단계까지 왔는지 다시 추측하지 않고 실제 코드를 기준으로 수업을 이어 갈 수 있습니다.
