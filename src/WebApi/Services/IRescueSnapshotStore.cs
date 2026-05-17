using System.Text.Json.Nodes;

namespace WebApi.Services;

public interface IRescueSnapshotStore
{
    RescueSnapshot Current { get; }

    void SetSuccess(JsonNode data);

    void SetFailure(string error);
}
