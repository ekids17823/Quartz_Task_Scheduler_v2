using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Scheduler.Core.Models;
using Xunit;

namespace Scheduler.Tests;

public class JobsApiIntegrationTests : IClassFixture<JobsApiIntegrationFixture>
{
    private readonly HttpClient _client;

    public JobsApiIntegrationTests(JobsApiIntegrationFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task CreateJob_ThenGetJobs_ReturnsCreatedJob()
    {
        var request = CreateIntervalRequest("CreateJob");

        var response = await _client.PostAsJsonAsync("/api/jobs", request);
        response.EnsureSuccessStatusCode();

        var jobs = await _client.GetFromJsonAsync<List<JobInfo>>("/api/jobs");

        var job = Assert.Single(jobs!, x => x.JobName == request.JobName && x.JobGroup == request.JobGroup);
        Assert.Equal("就緒", job.State);
        Assert.NotEmpty(job.Triggers);
        Assert.True(job.Triggers[0].NextFireTime.HasValue);
    }

    [Fact]
    public async Task DisabledJob_ManualTrigger_ReturnsConflict()
    {
        var request = CreateIntervalRequest("DisabledTrigger");
        (await _client.PostAsJsonAsync("/api/jobs", request)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/jobs/{request.JobGroup}/{request.JobName}/pause", null)).EnsureSuccessStatusCode();

        var response = await _client.PostAsync($"/api/jobs/{request.JobGroup}/{request.JobName}/trigger", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("排程已停用", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DisabledJob_Update_DoesNotAttachLiveTrigger()
    {
        var request = CreateIntervalRequest("DisabledUpdate");
        (await _client.PostAsJsonAsync("/api/jobs", request)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/jobs/{request.JobGroup}/{request.JobName}/pause", null)).EnsureSuccessStatusCode();

        request.Description = "updated while disabled";
        request.Triggers[0].RepeatInterval = 2;
        (await _client.PostAsJsonAsync("/api/jobs", request)).EnsureSuccessStatusCode();

        var jobs = await _client.GetFromJsonAsync<List<JobInfo>>("/api/jobs");
        var job = Assert.Single(jobs!, x => x.JobName == request.JobName);
        Assert.Equal("已停用", job.State);
        Assert.Null(job.Triggers[0].NextFireTime);
    }

    [Fact]
    public async Task ResumeDisabledJob_RebuildsFutureTrigger()
    {
        var request = CreateIntervalRequest("ResumeFuture");
        (await _client.PostAsJsonAsync("/api/jobs", request)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/jobs/{request.JobGroup}/{request.JobName}/pause", null)).EnsureSuccessStatusCode();

        var response = await _client.PostAsync($"/api/jobs/{request.JobGroup}/{request.JobName}/resume", null);
        response.EnsureSuccessStatusCode();

        var jobs = await _client.GetFromJsonAsync<List<JobInfo>>("/api/jobs");
        var job = Assert.Single(jobs!, x => x.JobName == request.JobName);
        Assert.Equal("就緒", job.State);
        Assert.True(job.Triggers[0].NextFireTime > DateTime.Now);
    }

    private static ScheduleRequest CreateIntervalRequest(string jobName)
    {
        return new ScheduleRequest
        {
            JobName = jobName,
            JobGroup = "ApiTests",
            Description = "integration test",
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            WorkingDirectory = "",
            MisfireActionFireAndProceed = false,
            ConcurrencyRule = "DoNotStart",
            IsHidden = true,
            Author = "test",
            Triggers = new List<TriggerDto>
            {
                new()
                {
                    TriggerName = $"{jobName}_trigger",
                    TriggerGroup = "ApiTests",
                    StartAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                    RepeatInterval = 1,
                    RepeatIntervalUnit = "Minute",
                    UiTabType = "Interval"
                }
            }
        };
    }

}

public sealed class JobsApiIntegrationFixture : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalCurrentDirectory;
    private readonly WebApplicationFactory<Program> _factory;

    public HttpClient Client { get; }

    public JobsApiIntegrationFixture()
    {
        _originalCurrentDirectory = Directory.GetCurrentDirectory();
        _tempDir = Path.Combine(Path.GetTempPath(), "QuartzTaskSchedulerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var repoRoot = FindRepoRoot();
        File.Copy(Path.Combine(repoRoot, "Scheduler.Api", "tables_sqlite.sql"), Path.Combine(_tempDir, "tables_sqlite.sql"));
        Directory.SetCurrentDirectory(_tempDir);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(_tempDir);
                builder.UseSetting("Kestrel:Endpoints:Http:Url", "http://localhost:0");
            });

        Client = _factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        Directory.SetCurrentDirectory(_originalCurrentDirectory);
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup; SQLite can briefly retain handles after host disposal.
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuartzTaskScheduler.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate QuartzTaskScheduler.slnx from test output directory.");
    }
}
