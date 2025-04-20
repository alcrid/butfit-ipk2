namespace project2.Interfaces;

public interface IMessageBuffer<T>
{
    void Add(T message);
    bool TryGet(out T? message);
    T? Peek();
    bool IsEmpty { get; }
}
