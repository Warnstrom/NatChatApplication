using System.Text;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

public interface ITwitchHttpClient
{
    Task<HttpResponseMessage> PostAsync(string type, string message);
    Task<string> GetAsync(string type, string query);
    Task UpdateOAuthToken(string newToken = "");
}

public class TwitchHttpClient : ITwitchHttpClient
{
    private readonly Dictionary<string, string> TwitchTypeToUrlMap = new()
    {
        { "AddSubscription", "https://api.twitch.tv/helix/eventsub/subscriptions" },
        { "ChatMessage", "https://api.twitch.tv/helix/chat/messages" },
        { "SearchChannels", "https://api.twitch.tv/helix/search/channels" },
        { "Streams", "https://api.twitch.tv/helix/streams" },
    };

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private string _oauthToken;
    private readonly IConfiguration _configuration;
    private readonly ArgsService _argsService;
    private readonly TwitchLib.Api.TwitchAPI _api;

    public TwitchHttpClient(IConfiguration configuration, TwitchLib.Api.TwitchAPI api, ArgsService argsService)
    {
        _configuration = configuration;
        _api = api;
        _argsService = argsService;
        _clientId = configuration["ClientId"];
        _oauthToken = configuration["AccessToken"];
        _httpClient = new();
        _httpClient.DefaultRequestHeaders.Add("Client-ID", _clientId);
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + _oauthToken);
    }

    public async Task UpdateOAuthToken(string newToken = "")
    {
        var response = await _api.Auth.RefreshAuthTokenAsync(_configuration["RefreshToken"], _configuration["ClientSecret"]);
        _oauthToken = string.IsNullOrEmpty(newToken) ? response.AccessToken : newToken;
        // Update the Authorization header dynamically when the token changes
        if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
        }
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + _oauthToken);
    }

    private void LogRequestHeaders()
    {
        Console.WriteLine("Logging HTTP Request Headers:");

        foreach (var header in _httpClient.DefaultRequestHeaders)
        {
            Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }
    }

    public async Task<HttpResponseMessage> PostAsync(string type, string message)
    {
        try
        {
            if (_argsService.Args.Length != 0 && _argsService.Args[0] == "dev")
            {
                LogRequestHeaders();
                Console.WriteLine(message);
            }

            if (TwitchTypeToUrlMap.TryGetValue(type, out string url))
            {
                HttpResponseMessage response = await _httpClient.PostAsync(url, new StringContent(message, Encoding.UTF8, "application/json"));

                // Handle token expiration scenario
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    AnsiConsole.MarkupLine("[bold yellow]OAuth token is invalid or expired. Attempting to refresh...[/]");

                    // Refresh the token
                    await UpdateOAuthToken();

                    // Retry the request with the new token
                    response = await _httpClient.PostAsync(url, new StringContent(message, Encoding.UTF8, "application/json"));
                }

                return response;
            }
            else
            {
                throw new ArgumentException($"The type '{type}' is not valid.");
            }
        }
        catch (HttpRequestException e)
        {
            AnsiConsole.MarkupLine($"{e}");
            AnsiConsole.MarkupLine($"HTTP request exception: {e.Message}");
            throw;
        }
    }

    // Query format: ?query=a_seagull&live_only=true etc.
    public async Task<string> GetAsync(string type, string query)
{
    if (TwitchTypeToUrlMap.TryGetValue(type, out string url))
    {
        HttpResponseMessage response = await _httpClient.GetAsync($"{url}{query}");

        response.EnsureSuccessStatusCode();

        string content = await response.Content.ReadAsStringAsync();

        return content;
    }
    else
    {
        throw new ArgumentException($"The type '{type}' is not valid.");
    }
}

}
