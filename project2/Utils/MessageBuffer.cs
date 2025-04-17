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

    public bool IsEmpty => _buffer.IsEmpty;

    public int Count => _buffer.Count;

    public IEnumerable<T> GetAll() => _buffer.ToArray();
}