using System;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BarTenderPrinter
{
    public enum PrintJobLedgerState
    {
        Received,
        Submitting,
        Submitted,
        Failed,
        Uncertain
    }

    public enum PrintJobRegistrationOutcome
    {
        Created,
        Existing,
        Conflict
    }

    public sealed class PrintJobLedgerEntry
    {
        public string JobId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public string RequestHash { get; set; } = "";
        public PrintJobLedgerState State { get; set; }
        public string RequestJson { get; set; } = "";
        public string CompletionJson { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";

        public PrintJobCompletion ToCompletion(bool replay)
        {
            PrintJobCompletion completion = null;
            if (!string.IsNullOrWhiteSpace(CompletionJson))
            {
                try { completion = JsonSerializer.Deserialize<PrintJobCompletion>(CompletionJson); }
                catch (Exception ex) when (ex is JsonException || ex is NotSupportedException || ex is InvalidOperationException) { }
            }
            completion ??= new PrintJobCompletion
            {
                PrintResult = new PrintResult(PrintSubmissionState.Uncertain, "打印作业状态待核查", "LEDGER_STATE_UNCERTAIN"),
                CompletionStatus = "打印作业状态待核查",
                JobId = JobId,
                IdempotencyKey = IdempotencyKey
            };
            completion.IsIdempotentReplay = replay;
            completion.LedgerState = State.ToString();
            return completion;
        }
    }

    public sealed class PrintJobRegistration
    {
        public PrintJobRegistrationOutcome Outcome { get; set; }
        public PrintJobLedgerEntry Entry { get; set; }
    }

    public interface IPrintJobLedger
    {
        PrintJobRegistration Register(PrintJobRequest request, string requestHash);
        bool TryMarkSubmitting(string idempotencyKey);
        void Complete(string idempotencyKey, PrintJobCompletion completion);
        PrintJobLedgerEntry Get(string idempotencyKey);
    }

    public sealed class SqlitePrintJobLedger : IPrintJobLedger
    {
        private readonly string _databasePath;

        public SqlitePrintJobLedger(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("账本路径不能为空。", nameof(databasePath));
            _databasePath = databasePath;
            var directory = System.IO.Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory)) System.IO.Directory.CreateDirectory(directory);
            EnsureDatabase();
            RecoverInterruptedSubmissions();
        }

        public PrintJobRegistration Register(PrintJobRequest request, string requestHash)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw new ArgumentException("幂等键不能为空。", nameof(request));
            if (string.IsNullOrWhiteSpace(requestHash)) throw new ArgumentException("请求摘要不能为空。", nameof(requestHash));

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var now = DateTime.UtcNow.ToString("O");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO PrintJobs (IdempotencyKey, JobId, RequestHash, State, RequestJson, CompletionJson, CreatedAtUtc, UpdatedAtUtc) VALUES ($key,$job,$hash,'Received',$request,'',$now,$now)";
            insert.Parameters.AddWithValue("$key", request.IdempotencyKey);
            insert.Parameters.AddWithValue("$job", request.JobId);
            insert.Parameters.AddWithValue("$hash", requestHash);
            insert.Parameters.AddWithValue("$request", JsonSerializer.Serialize(request));
            insert.Parameters.AddWithValue("$now", now);
            var created = insert.ExecuteNonQuery() == 1;
            var entry = Read(connection, transaction, request.IdempotencyKey);
            transaction.Commit();
            return new PrintJobRegistration
            {
                Outcome = created || (entry?.State == PrintJobLedgerState.Received && string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal))
                    ? PrintJobRegistrationOutcome.Created
                    : string.Equals(entry?.RequestHash, requestHash, StringComparison.Ordinal)
                        ? PrintJobRegistrationOutcome.Existing
                        : PrintJobRegistrationOutcome.Conflict,
                Entry = entry
            };
        }

        public bool TryMarkSubmitting(string idempotencyKey)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PrintJobs SET State='Submitting', UpdatedAtUtc=$now WHERE IdempotencyKey=$key AND State='Received'";
            command.Parameters.AddWithValue("$key", idempotencyKey ?? "");
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            return command.ExecuteNonQuery() == 1;
        }

        public void Complete(string idempotencyKey, PrintJobCompletion completion)
        {
            if (completion?.PrintResult == null) throw new ArgumentNullException(nameof(completion));
            var state = completion.PrintResult.State switch
            {
                PrintSubmissionState.Submitted => PrintJobLedgerState.Submitted,
                PrintSubmissionState.Failed => PrintJobLedgerState.Failed,
                _ => PrintJobLedgerState.Uncertain
            };
            completion.LedgerState = state.ToString();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PrintJobs SET State=$state, CompletionJson=$completion, UpdatedAtUtc=$now WHERE IdempotencyKey=$key AND State='Submitting'";
            command.Parameters.AddWithValue("$state", state.ToString());
            command.Parameters.AddWithValue("$completion", JsonSerializer.Serialize(completion));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$key", idempotencyKey ?? "");
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("打印账本状态已变更，无法保存提交结果。");
        }

        public PrintJobLedgerEntry Get(string idempotencyKey)
        {
            using var connection = OpenConnection();
            return Read(connection, null, idempotencyKey);
        }

        private void EnsureDatabase()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS PrintJobs (IdempotencyKey TEXT PRIMARY KEY, JobId TEXT NOT NULL, RequestHash TEXT NOT NULL, State TEXT NOT NULL, RequestJson TEXT NOT NULL, CompletionJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL); CREATE UNIQUE INDEX IF NOT EXISTS UX_PrintJobs_JobId ON PrintJobs(JobId); CREATE INDEX IF NOT EXISTS IX_PrintJobs_State ON PrintJobs(State)";
            command.ExecuteNonQuery();
        }

        private void RecoverInterruptedSubmissions()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE PrintJobs SET State='Uncertain', UpdatedAtUtc=$now WHERE State='Submitting'";
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        private PrintJobLedgerEntry Read(SqliteConnection connection, SqliteTransaction transaction, string idempotencyKey)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT JobId, IdempotencyKey, RequestHash, State, RequestJson, CompletionJson, CreatedAtUtc, UpdatedAtUtc FROM PrintJobs WHERE IdempotencyKey=$key LIMIT 1";
            command.Parameters.AddWithValue("$key", idempotencyKey ?? "");
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new PrintJobLedgerEntry
            {
                JobId = reader.GetString(0),
                IdempotencyKey = reader.GetString(1),
                RequestHash = reader.GetString(2),
                State = Enum.Parse<PrintJobLedgerState>(reader.GetString(3), true),
                RequestJson = reader.GetString(4),
                CompletionJson = reader.GetString(5),
                CreatedAtUtc = reader.GetString(6),
                UpdatedAtUtc = reader.GetString(7)
            };
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath }.ToString());
            connection.Open();
            return connection;
        }
    }
}
