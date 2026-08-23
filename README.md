# 2.5D Zombie Survival

Unity 2022.3.62f2와 URP로 만든 3D 월드 기반 2.5D 탑다운 프로토타입입니다.

## 현재 구현 범위

- `Bootstrap`, `Hideout`, `RaidPrototype` 기본 씬
- 3D 바닥과 충돌 가능한 벽
- `CharacterController` 기반 WASD 이동(대각선 속도 정규화)
- 마우스 위치를 지면에 투영하는 조준
- 카메라를 향하는 2D 플레이어 스프라이트와 3D 캡슐 충돌체
- 고정 각도 카메라 추적
- 마우스 좌클릭 반자동 권총과 Raycast 명중 판정
- 8발 탄창, 24발 예비탄, R키 재장전
- 공통 피해 인터페이스와 체력 기반 연습 표적
- 현재 탄약과 재장전 상태 HUD
- 감지, 추적, 근접 공격, 사망 상태를 가진 기본 좀비 AI
- 플레이어 체력과 HUD 표시
- `ScriptableObject` 기반 탄약, 의료품, 식량, 퀘스트 아이템 정의
- E키 상호작용 루팅과 10×6 타르코프식 격자 인벤토리
- 아이템별 격자 크기, 중첩, 드래그 이동, R키 회전, 공간 검사
- 주무기, 보조무기, 방탄복, 전술 조끼, 가방 장비 슬롯
- 전술 조끼와 가방의 독립 내부 격자
- 중첩 분할, 의료품/탄약 사용, 월드 아이템 버리기
- 퀘스트 무전기 보유 조건과 탈출 지점 활성화
- 탈출 구역 진입, 3초 카운트다운, 이탈 취소와 성공 상태

레이드 결과 전달, 거점 창고와 영구 저장은 아직 구현하지 않았습니다.

## 실행

1. Unity Hub에서 저장소 루트를 Unity 2022.3.62f2로 엽니다.
2. `Assets/_Game/Scenes/RaidPrototype.unity`를 엽니다.
3. Play를 누릅니다.
4. WASD로 이동하고 마우스로 조준합니다. 청록색 조준 표시가 지면 위 커서 위치를 보여 줍니다.
5. 마우스 왼쪽 버튼으로 권총을 발사하고 R키로 재장전합니다. 녹색 좀비는 플레이어를 감지해 추적하고 근접 공격하며 3발 명중하면 사망합니다.
6. 색상 Cube 아이템 가까이에서 E키를 누르면 오른쪽 인벤토리에 획득 결과가 표시됩니다.
7. Tab으로 인벤토리를 열고 아이템을 드래그합니다. 선택한 아이템은 R키로 회전합니다.
8. Shift+클릭으로 중첩을 절반 분할하고, 우클릭으로 사용 또는 장착하며, Delete로 버립니다.
9. 파란색 무전기를 획득한 뒤 북쪽 탈출 지점에 3초간 머물면 탈출에 성공합니다. 구역을 벗어나면 시간이 초기화됩니다.

씬이 없거나 다시 만들려면 Unity 메뉴에서 `Tools > 2.5D Zombie Survival > Rebuild Prototype Content`를 실행합니다. 이 명령은 세 기본 씬과 플레이어 프리팹을 다시 생성하므로 해당 생성물의 수동 변경은 덮어씁니다.

## 검증

Unity 메뉴 `Window > General > Test Runner`의 EditMode 탭에서 테스트를 실행할 수 있습니다. 배치 검증 예시는 다음과 같습니다.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe' -batchmode -quit -projectPath . -runTests -testPlatform EditMode -testResults Logs\EditModeResults.xml
```
