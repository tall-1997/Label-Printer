CREATE TABLE IF NOT EXISTS PrintJobs (
    IdempotencyKey TEXT PRIMARY KEY,
    JobId TEXT NOT NULL,
    RequestHash TEXT NOT NULL,
    State TEXT NOT NULL,
    RequestJson TEXT NOT NULL,
    CompletionJson TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_PrintJobs_JobId ON PrintJobs(JobId);
CREATE INDEX IF NOT EXISTS IX_PrintJobs_State ON PrintJobs(State);
