namespace SquirrelBox;

public interface IInboxPayloadHasher
{
    string ComputeHash(object payload);
}
