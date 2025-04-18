using System.Collections.Concurrent;
using project2.Models;

namespace project2.Utils;

public class MessageBuffer<T> where T : Message
{
    private readonly ConcurrentQueue<T> _buffer = new();

    public void Add(T message)
    {
        _buffer.Enqueue(message);
    }

    public bool TryGet(out T? message)
    {
        return _buffer.TryDequeue(out message);
    }

    public T? Peek() => _buffer.TryPeek(out var result) ? result : null;

    public bool IsEmpty => _buffer.IsEmpty;
}