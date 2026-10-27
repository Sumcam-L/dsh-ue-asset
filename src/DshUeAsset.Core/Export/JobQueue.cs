using DshUeAsset.Core.Models;

namespace DshUeAsset.Core.Export;

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class JobRecord
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int Total { get; set; }
    public int Completed { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? OutputDirectory { get; set; }
    public List<ExportFileView> Results { get; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public CancellationTokenSource Cancellation { get; } = new();

    public JobView ToView() => new(
        Id,
        Kind,
        Status.ToString().ToLowerInvariant(),
        Total,
        Completed,
        Total <= 0 ? 0 : (int)Math.Round(100.0 * Completed / Total),
        Message,
        Error,
        [.. Results],
        OutputDirectory,
        CreatedAt,
        UpdatedAt);
}

/// <summary>Per-session job queue. Progress reads never take the session parse gate.</summary>
public sealed class JobQueue
{
    private readonly object _lock = new();
    private readonly Dictionary<string, JobRecord> _jobs = new(StringComparer.Ordinal);
    private readonly string _owner;
    private readonly int _maxJobs;

    public JobQueue(string owner, int maxJobs = 16)
    {
        _owner = owner;
        _maxJobs = Math.Max(1, maxJobs);
    }

    public string Enqueue(string kind, int total, Func<JobRecord, CancellationToken, Task> work)
    {
        var job = new JobRecord
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Kind = kind,
            Total = total
        };

        lock (_lock)
        {
            PruneLocked();
            _jobs[job.Id] = job;
        }

        _ = Task.Run(async () =>
        {
            lock (_lock)
            {
                job.Status = JobStatus.Running;
                job.UpdatedAt = DateTimeOffset.Now;
            }

            try
            {
                await work(job, job.Cancellation.Token).ConfigureAwait(false);
                lock (_lock)
                {
                    job.Status = job.Cancellation.IsCancellationRequested ? JobStatus.Cancelled : JobStatus.Completed;
                    job.Completed = job.Total;
                    job.Message ??= job.Status == JobStatus.Cancelled ? "cancelled" : "done";
                    job.UpdatedAt = DateTimeOffset.Now;
                }
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    job.Status = JobStatus.Cancelled;
                    job.Message = "cancelled";
                    job.UpdatedAt = DateTimeOffset.Now;
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = ex.Message;
                    job.UpdatedAt = DateTimeOffset.Now;
                }
            }
        });

        return job.Id;
    }

    public JobView? Get(string jobId)
    {
        lock (_lock) return _jobs.TryGetValue(jobId, out var job) ? job.ToView() : null;
    }

    public IReadOnlyList<JobView> List()
    {
        lock (_lock)
            return _jobs.Values.OrderByDescending(j => j.CreatedAt).Select(j => j.ToView()).ToList();
    }

    public bool Cancel(string jobId)
    {
        lock (_lock)
        {
            if (!_jobs.TryGetValue(jobId, out var job)) return false;
            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled) return false;
            job.Cancellation.Cancel();
            if (job.Status == JobStatus.Queued)
            {
                job.Status = JobStatus.Cancelled;
                job.UpdatedAt = DateTimeOffset.Now;
            }
            return true;
        }
    }

    public int RunningCount
    {
        get { lock (_lock) return _jobs.Values.Count(j => j.Status is JobStatus.Queued or JobStatus.Running); }
    }

    private void PruneLocked()
    {
        if (_jobs.Count < _maxJobs) return;
        foreach (var stale in _jobs.Values
                     .Where(j => j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                     .OrderBy(j => j.UpdatedAt)
                     .Take(_jobs.Count - _maxJobs + 1)
                     .ToList())
        {
            _jobs.Remove(stale.Id);
        }
    }
}
