using System.Collections.Concurrent;
using project2.Models;

namespace project2.Utils;

public class MessageBuffer
{
    private readonly ConcurrentQueue<Message> _buffer = new();

    public void Add(Message message)
    {
        _buffer.Enqueue(message);
    }

    public bool TryGet(out Message? message)
    {
        return _buffer.TryDequeue(out message);
    }

    public bool IsEmpty => _buffer.IsEmpty;

    public int Count => _buffer.Count;

    public IEnumerable<Message> GetAll() => _buffer.ToArray();
}