using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Scheduler.Core.Models;

namespace Scheduler.Ui.Services;

public class SchedulerApiService
{
    private readonly HttpClient _httpClient;
    
    public SchedulerApiService()
    {
        // 預設 ASP.NET Core API dev server 的 URL (從您的啟動日誌中得知為 5196)
        _httpClient = new HttpClient { BaseAddress = new System.Uri("http://localhost:5196/") };
    }

    public async Task<List<JobInfo>> GetAllJobsAsync()
    {
        try 
        {
            return await _httpClient.GetFromJsonAsync<List<JobInfo>>("api/jobs") ?? new List<JobInfo>();
        } 
        catch 
        { 
            return new List<JobInfo>(); 
        }
    }

    public async Task CreateJobAsync(ScheduleRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/jobs", request);
        response.EnsureSuccessStatusCode();
    }

    public async Task TriggerJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/trigger", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task PauseJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/pause", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResumeJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/resume", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteJobAsync(string group, string name)
    {
        var response = await _httpClient.DeleteAsync($"api/jobs/{group}/{name}");
        response.EnsureSuccessStatusCode();
    }

    public async Task InterruptJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/interrupt", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<JobLogEntry>> GetJobLogsAsync(string group, string name)
    {
        try 
        {
            return await _httpClient.GetFromJsonAsync<List<JobLogEntry>>($"api/jobs/{group}/{name}/logs") ?? new List<JobLogEntry>();
        } 
        catch 
        { 
            return new List<JobLogEntry>(); 
        }
    }
}
