using System.Text.Json;
using BarTenderPrinter.Application.Idempotency;
using Microsoft.Data.Sqlite;

namespace BarTenderPrinter.StationAgent;

public enum StationOperationState { Pending, Executing, Succeeded, Failed, Uncertain, PendingValidation }

public sealed record RegisterOperationRequest(string Kind, string BusinessId, string IdempotencyKey, JsonElement Payload);
public sealed record CompleteOperationRequest(long ExpectedVersion, StationOperationState State, JsonElement Result,
    string ActorId, string ReasonCode, string Note, bool EnqueueSynchronization = true);
public sealed record ResolveOperationRequest(long ExpectedVersion, StationOperationState State, string ActorId,
    string ReasonCode, string Note);
public sealed record StationOperation(string Id, string Kind, string BusinessId, string IdempotencyKey,
    string RequestHash, string RequestJson, StationOperationState State, string ResultJson, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record OutboxMessage(string Id, string OperationId, string IdempotencyKey, string PayloadJson,
    string State, int AttemptCount, DateTimeOffset NextAttemptAtUtc, long Version);

public sealed class StationOperationStore
{
    private readonly string _connectionString;

    public StationOperationStore(IConfiguration configuration)
    {
        var path = configuration["StationAgent:DatabasePath"]?.Trim();
        if (string.IsNullOrEmpty(path)) path = Path.Combine(AppContext.BaseDirectory, "station-agent.db");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Initialize();
        RecoverInterrupted();
    }

    public StationOperation Register(RegisterOperationRequest request)
    {
        var kind = Required(request.Kind, nameof(request.Kind));
        var businessId = Required(request.BusinessId, nameof(request.BusinessId));
        var key = Required(request.IdempotencyKey, nameof(request.IdempotencyKey));
        var json = request.Payload.GetRawText();
        var hash = new CanonicalRequestDigest().Compute(request.Payload);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var existing = Find(connection, transaction, kind, key);
        if (existing != null)
        {
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
                throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
            transaction.Commit();
            return existing;
        }
        var now = DateTimeOffset.UtcNow;
        var operation = new StationOperation(Guid.NewGuid().ToString("N"), kind, businessId, key, hash, json,
            StationOperationState.Pending, "", 0, now, now);
        Execute(connection, transaction, """
            INSERT INTO station_operations(id, kind, business_id, idempotency_key, request_hash, request_json,
                state, result_json, version, created_at_utc, updated_at_utc)
            VALUES($id,$kind,$business,$key,$hash,$json,$state,'',0,$created,$updated)
            """, operation);
        AddTransition(connection, transaction, operation.Id, "", "Pending", "system", "REGISTERED", "");
        transaction.Commit();
        return operation;
    }

    public StationOperation? Get(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM station_operations WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<StationOperation> List(StationOperationState? state, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = state == null
            ? "SELECT * FROM station_operations ORDER BY created_at_utc DESC LIMIT $limit"
            : "SELECT * FROM station_operations WHERE state=$state ORDER BY created_at_utc DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        if (state != null) command.Parameters.AddWithValue("$state", state.ToString());
        using var reader = command.ExecuteReader();
        var result = new List<StationOperation>();
        while (reader.Read()) result.Add(Read(reader));
        return result;
    }

    public StationOperation Start(string id, long expectedVersion) => Transition(id, expectedVersion,
        [StationOperationState.Pending], StationOperationState.Executing, "system", "EXECUTION_STARTED", "", null, false);

    public StationOperation Complete(string id, CompleteOperationRequest request)
    {
        if (request.State is not (StationOperationState.Succeeded or StationOperationState.Failed or StationOperationState.Uncertain))
            throw new ArgumentException("完成状态无效。", nameof(request.State));
        return Transition(id, request.ExpectedVersion, [StationOperationState.Executing], request.State,
            Required(request.ActorId, nameof(request.ActorId)), Required(request.ReasonCode, nameof(request.ReasonCode)),
            request.Note?.Trim() ?? "", request.Result.GetRawText(), request.EnqueueSynchronization);
    }

    public StationOperation Resolve(string id, ResolveOperationRequest request)
    {
        if (request.State is not (StationOperationState.Pending or StationOperationState.Succeeded or StationOperationState.Failed))
            throw new ArgumentException("人工决议状态无效。", nameof(request.State));
        return Transition(id, request.ExpectedVersion,
            [StationOperationState.Uncertain, StationOperationState.PendingValidation], request.State,
            Required(request.ActorId, nameof(request.ActorId)), Required(request.ReasonCode, nameof(request.ReasonCode)),
            Required(request.Note, nameof(request.Note)), null, request.State != StationOperationState.Pending);
    }

    public IReadOnlyList<OutboxMessage> ListOutbox(int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,operation_id,idempotency_key,payload_json,state,attempt_count,next_attempt_at_utc,version FROM outbox_messages ORDER BY next_attempt_at_utc LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        using var reader = command.ExecuteReader();
        var result = new List<OutboxMessage>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetInt32(5), DateTimeOffset.Parse(reader.GetString(6)), reader.GetInt64(7)));
        return result;
    }

    public OutboxMessage UpdateOutbox(string id, long expectedVersion, bool succeeded, string errorCode = "")
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var state = succeeded ? "Succeeded" : "Pending";
        var next = succeeded ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.AddSeconds(30);
        command.CommandText = "UPDATE outbox_messages SET state=$state,attempt_count=attempt_count+1,next_attempt_at_utc=$next,version=version+1 WHERE id=$id AND version=$version";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$next", next.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$version", expectedVersion);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("CONCURRENCY_CONFLICT");
        return ListOutbox(200).Single(message => message.Id == id);
    }

    private StationOperation Transition(string id, long expectedVersion, StationOperationState[] allowed,
        StationOperationState target, string actor, string reason, string note, string? result, bool enqueue)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var current = FindById(connection, transaction, id) ?? throw new KeyNotFoundException("OPERATION_NOT_FOUND");
        if (current.Version != expectedVersion) throw new InvalidOperationException("CONCURRENCY_CONFLICT");
        if (!allowed.Contains(current.State)) throw new InvalidOperationException("INVALID_STATE_TRANSITION");
        var now = DateTimeOffset.UtcNow;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE station_operations SET state=$state,result_json=COALESCE($result,result_json),version=version+1,updated_at_utc=$now WHERE id=$id AND version=$version";
        command.Parameters.AddWithValue("$state", target.ToString());
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$version", expectedVersion);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("CONCURRENCY_CONFLICT");
        AddTransition(connection, transaction, id, current.State.ToString(), target.ToString(), actor, reason, note);
        if (enqueue)
        {
            using var outbox = connection.CreateCommand();
            outbox.Transaction = transaction;
            outbox.CommandText = "INSERT OR IGNORE INTO outbox_messages(id,operation_id,idempotency_key,payload_json,state,attempt_count,next_attempt_at_utc,version) VALUES($id,$operation,$key,$payload,'Pending',0,$now,0)";
            outbox.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            outbox.Parameters.AddWithValue("$operation", id);
            outbox.Parameters.AddWithValue("$key", $"sync-{current.IdempotencyKey}");
            outbox.Parameters.AddWithValue("$payload", result ?? current.ResultJson);
            outbox.Parameters.AddWithValue("$now", now.ToString("O"));
            outbox.ExecuteNonQuery();
        }
        transaction.Commit();
        return Get(id)!;
    }

    private void RecoverInterrupted()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE station_operations SET state='Uncertain',version=version+1,updated_at_utc=$now WHERE state='Executing'";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS station_operations(id TEXT PRIMARY KEY,kind TEXT NOT NULL,business_id TEXT NOT NULL,idempotency_key TEXT NOT NULL,request_hash TEXT NOT NULL,request_json TEXT NOT NULL,state TEXT NOT NULL,result_json TEXT NOT NULL,version INTEGER NOT NULL,created_at_utc TEXT NOT NULL,updated_at_utc TEXT NOT NULL,UNIQUE(kind,idempotency_key));
            CREATE TABLE IF NOT EXISTS outbox_messages(id TEXT PRIMARY KEY,operation_id TEXT NOT NULL REFERENCES station_operations(id),idempotency_key TEXT NOT NULL UNIQUE,payload_json TEXT NOT NULL,state TEXT NOT NULL,attempt_count INTEGER NOT NULL,next_attempt_at_utc TEXT NOT NULL,version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS operation_transitions(id INTEGER PRIMARY KEY AUTOINCREMENT,operation_id TEXT NOT NULL REFERENCES station_operations(id),from_state TEXT NOT NULL,to_state TEXT NOT NULL,actor_id TEXT NOT NULL,reason_code TEXT NOT NULL,note TEXT NOT NULL,occurred_at_utc TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name}不能为空。", name) : value.Trim();
    private static StationOperation? Find(SqliteConnection c, SqliteTransaction t, string kind, string key) { using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT * FROM station_operations WHERE kind=$kind AND idempotency_key=$key";cmd.Parameters.AddWithValue("$kind",kind);cmd.Parameters.AddWithValue("$key",key);using var r=cmd.ExecuteReader();return r.Read()?Read(r):null; }
    private static StationOperation? FindById(SqliteConnection c, SqliteTransaction t, string id) { using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="SELECT * FROM station_operations WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();return r.Read()?Read(r):null; }
    private static StationOperation Read(SqliteDataReader r) => new(r["id"].ToString()!,r["kind"].ToString()!,r["business_id"].ToString()!,r["idempotency_key"].ToString()!,r["request_hash"].ToString()!,r["request_json"].ToString()!,Enum.Parse<StationOperationState>(r["state"].ToString()!),r["result_json"].ToString()!,Convert.ToInt64(r["version"]),DateTimeOffset.Parse(r["created_at_utc"].ToString()!),DateTimeOffset.Parse(r["updated_at_utc"].ToString()!));
    private static void Execute(SqliteConnection c, SqliteTransaction t, string sql, StationOperation o) { using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText=sql;cmd.Parameters.AddWithValue("$id",o.Id);cmd.Parameters.AddWithValue("$kind",o.Kind);cmd.Parameters.AddWithValue("$business",o.BusinessId);cmd.Parameters.AddWithValue("$key",o.IdempotencyKey);cmd.Parameters.AddWithValue("$hash",o.RequestHash);cmd.Parameters.AddWithValue("$json",o.RequestJson);cmd.Parameters.AddWithValue("$state",o.State.ToString());cmd.Parameters.AddWithValue("$created",o.CreatedAtUtc.ToString("O"));cmd.Parameters.AddWithValue("$updated",o.UpdatedAtUtc.ToString("O"));cmd.ExecuteNonQuery(); }
    private static void AddTransition(SqliteConnection c, SqliteTransaction t, string id, string from, string to, string actor, string reason, string note) { using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="INSERT INTO operation_transitions(operation_id,from_state,to_state,actor_id,reason_code,note,occurred_at_utc) VALUES($id,$from,$to,$actor,$reason,$note,$now)";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$from",from);cmd.Parameters.AddWithValue("$to",to);cmd.Parameters.AddWithValue("$actor",actor);cmd.Parameters.AddWithValue("$reason",reason);cmd.Parameters.AddWithValue("$note",note);cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));cmd.ExecuteNonQuery(); }
}
