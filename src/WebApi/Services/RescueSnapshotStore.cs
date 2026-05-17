using System.Text.Json.Nodes;

namespace WebApi.Services;

public sealed class RescueSnapshotStore(TimeProvider time) : IRescueSnapshotStore
{
    private RescueSnapshot _current = RescueSnapshot.Empty;

    public RescueSnapshot Current => Volatile.Read(ref _current);

    public void SetSuccess(JsonNode data)
    {
        var next = new RescueSnapshot(data, time.GetUtcNow(), LastError: null, LastErrorAt: null);
        Volatile.Write(ref _current, next);
    }

    public void SetFailure(string error)
    {
        var now = time.GetUtcNow();
        RescueSnapshot previous, next;
        do
        {
            previous = Volatile.Read(ref _current);
            next = previous with { LastError = error, LastErrorAt = now };
        }
        while (Interlocked.CompareExchange(ref _current, next, previous) != previous);
    }
}