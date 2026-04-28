using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Scheduler.Core.Models;

namespace Scheduler.Ui.Services;

public class SchedulerApiService
{
    private readonly HttpClient _httpClient;
    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? "Unknown";
    
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

        var handler = new HttpClientHandler { UseProxy = false };
        _httpClient = new HttpClient(handler) { BaseAddress = new System.Uri(url) };
    }

    public async Task<List<JobInfo>> GetAllJobsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<JobInfo>>("api/jobs") ?? new List<JobInfo>();
    }

    public async Task CreateJobAsync(ScheduleRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/jobs", request);
        await EnsureSuccessAsync(response);
    }

    public async Task TriggerJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/trigger", null);
        await EnsureSuccessAsync(response);
    }

    public async Task PauseJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/pause", null);
        await EnsureSuccessAsync(response);
    }

    public async Task ResumeJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/resume", null);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteJobAsync(string group, string name)
    {
        var response = await _httpClient.DeleteAsync($"api/jobs/{group}/{name}");
        await EnsureSuccessAsync(response);
    }

    public async Task InterruptJobAsync(string group, string name)
    {
        var response = await _httpClient.PostAsync($"api/jobs/{group}/{name}/interrupt", null);
        await EnsureSuccessAsync(response);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            body = response.ReasonPhrase ?? "API 回傳錯誤。";
        }

        throw new HttpRequestException($"API 錯誤 ({(int)response.StatusCode} {response.StatusCode}): {body}");
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

    public async Task<List<AuditLogEntry>> GetAuditLogsAsync()
    {
        try 
        {
            return await _httpClient.GetFromJsonAsync<List<AuditLogEntry>>("api/jobs/auditlogs") ?? new List<AuditLogEntry>();
        } 
        catch 
        { 
            return new List<AuditLogEntry>(); 
        }
    }
}
