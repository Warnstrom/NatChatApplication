using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using Spectre.Console.Json;
public interface IConfigurationService
{
    Task EditConfigurationAsync();
}
public class ConfigurationService(IJsonFileController jsonFileController, ITwitchHttpClient twitchHttpClient, IConfigurationRoot configuration) : IConfigurationService
{
    private readonly HashSet<string> sensitiveKeys = new HashSet<string>
            {
            "OBS_IP", "OBS_Port", "OBS_Password", "AccessToken",
            "RefreshToken", "ClientSecret", "ClientId"
            };

    public async Task EditConfigurationAsync()
    {
        bool StreamOnline = await twitchHttpClient.CheckIfStreamIsOnline();

        var keys = new Dictionary<int, string>
{
    { 0, "Exit" },
    { 1, "AccessToken" },
    { 2, "RefreshToken" },
    { 3, "ChannelId" },
    { 4, "ClientSecret" },
    { 5, "ClientId" },
    { 7, "OBS_IP" },
    { 8, "OBS_Port" },
    { 9, "OBS_Password" },
    { 10, "OBS_MicName" },
    { 11, "OBS_Scene" },
    { 12, "OBS_SourceName" },
    { 13, "RedirectUri" }
};

        await ShowConfigurationWithStreamStatusCheck(StreamOnline);

        // Create a prompt for the user to select a configuration key to edit
        var prompt = new SelectionPrompt<string>()
            .Title("[grey]Select a configuration key to edit:[/]")
            .AddChoices(keys.Values.ToArray()) // Add the keys as choices
            .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for selected option
            .Mode(SelectionMode.Leaf) // Leaf mode for modern selection UX
            .WrapAround(true)
            .UseConverter(text => $"[dim white]»[/] [white]{text}[/]");

        // Get the selected value from the prompt
        string selectedKey = AnsiConsole.Prompt(prompt);

        // Check if the selected key is "Exit"
        if (selectedKey == "Exit")
        {
            // Exit the method early
            return;
        }

        // Proceed with the rest of the code if the user didn't choose to exit
        string? newValue = null;

        // Check if the selected key is in the list of sensitive keys
        if (sensitiveKeys.Contains(selectedKey) && StreamOnline)
        {
            newValue = AnsiConsole.Prompt(
                new TextPrompt<string>($"[yellow]Enter the new value for {selectedKey}:[/]")
                    .Secret());
        }
        else
        {
            newValue = AnsiConsole.Prompt(
                new TextPrompt<string>($"[yellow]Enter the new value for {selectedKey}:[/]")
                    .AllowEmpty());
        }

        await jsonFileController.UpdateAsync(selectedKey, newValue);

        await ShowConfigurationWithStreamStatusCheck(StreamOnline);
    }

    // Method to check if the stream is online and display JSON with masked sensitive data
    private async Task ShowConfigurationWithStreamStatusCheck(bool isLive)
    {
        if (isLive)
        {
            AnsiConsole.Markup("[bold green]Stream is currently live. Displaying masked configuration data...[/]\n");
            await DisplayUpdatedJson(maskSensitiveInfo: true);
        }
        else
        {
            AnsiConsole.Markup("[bold red]Stream is offline. Displaying full configuration data...[/]\n");
            await DisplayUpdatedJson(maskSensitiveInfo: false);
        }
    }


    private async Task DisplayUpdatedJson(bool maskSensitiveInfo = false)
    {
        var jsonString = await jsonFileController.GetJsonStringAsync();

        // Deserialize the JSON string into a dictionary for easy manipulation
        var jsonConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);

        // Mask sensitive information if the flag is true
        if (maskSensitiveInfo)
        {

            foreach (var key in sensitiveKeys)
            {
                if (jsonConfig.ContainsKey(key))
                {
                    jsonConfig[key] = new string('*', 10); // Mask with 10 asterisks
                }
            }
        }

        // Convert the dictionary back to JSON for display
        var maskedJsonString = JsonSerializer.Serialize(jsonConfig, new JsonSerializerOptions { WriteIndented = true });

        var json = new JsonText(maskedJsonString)
            .BracesColor(Color.White)
            .BracketColor(Color.Green)
            .ColonColor(Color.Blue)
            .CommaColor(Color.White)
            .StringColor(Color.White)
            .NumberColor(Color.Blue)
            .BooleanColor(Color.Red)
            .NullColor(Color.Green);

        AnsiConsole.Write(
            new Panel(json)
                .Header("Configuration File")
                .Collapse()
                .RoundedBorder()
                .BorderColor(Color.Green));
    }

}
