namespace Darp.Luau.Internal.Async;

internal sealed class LuauAsyncWorkQueue
{
    private readonly Queue<Action> _queue = new();
    private bool _draining;

    public void Enqueue(Action item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_queue)
        {
            _queue.Enqueue(item);
            if (_draining)
                return;

            _draining = true;
        }

        Drain();
    }

    private void Drain()
    {
        while (true)
        {
            Action item;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    _draining = false;
                    return;
                }

                item = _queue.Dequeue();
            }

            item();
        }
    }
}
