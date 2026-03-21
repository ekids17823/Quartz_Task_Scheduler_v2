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
        string url = "http://localhost:5196/";
        var jsonPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (System.IO.File.Exists(jsonPath))
        {
            try 
            {
                var json = System.IO.File.ReadAllText(jsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("SchedulerApi", out var apiEl) && apiEl.TryGetProperty("BaseUrl", out var urlEl)) 
                {
                    string? parsedUrl = urlEl.GetString();
                    if (!string.IsNullOrWhiteSpace(parsedUrl)) 
                    {
                        url = parsedUrl;
                        if (!url.EndsWith("/")) url += "/";
                    }
                }
            } 
            catch { } // fallback to default
        }

        _httpClient = new HttpClient { BaseAddress = new System.Uri(url) };
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
