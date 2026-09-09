namespace SquirrelBox;

public interface IInboxContextAccessor
{
    InboxContext Current { get; set; }

    void Prepare();
}
