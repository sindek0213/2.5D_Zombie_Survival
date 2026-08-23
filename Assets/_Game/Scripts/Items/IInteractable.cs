namespace ZombieSurvival.Items
{
    // PlayerInteraction이 아이템, 문, 탈출 지점처럼 서로 다른 대상을 같은 방식으로 처리하기 위한 인터페이스입니다.
    // InteractionPrompt는 HUD 문구, CanInteract는 현재 가능 여부, Interact는 실제 실행 성공 여부를 제공합니다.
    // 새 상호작용 오브젝트는 이 인터페이스만 구현하면 PlayerInteraction 코드를 수정하지 않아도 탐색됩니다.
    public interface IInteractable
    {
        string InteractionPrompt { get; }
        bool CanInteract { get; }
        bool Interact();
    }
}
