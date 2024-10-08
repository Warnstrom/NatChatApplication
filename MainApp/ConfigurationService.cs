using Spectre.Console;
using Spectre.Console.Json;
public interface IConfigurationService
{
    Task EditConfigurationAsync();
}
public class ConfigurationService(IJsonFileController jsonFileController) : IConfigurationService
{
    public async Task EditConfigurationAsync()
    {
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
        { 10, "RedirectUri" }
    };

        await DisplayUpdatedJson();

        var prompt = new SelectionPrompt<string>()
            .Title("[grey]Select a configuration key to edit:[/]")
            .AddChoices(keys.Values.ToArray()) // Add the keys as choices
            .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for selected option
            .Mode(SelectionMode.Leaf) // Leaf mode for modern selection UX
            .WrapAround(false) // Disable wrap-around behavior
            .UseConverter(text => $"[dim white]»[/] [white]{text}[/]");

        // Get the selected option, specify the type explicitly
        string selectedOption = AnsiConsole.Prompt(prompt);

        if (selectedOption == "Exit")
        {
            return;
        }


        var currentValue = await jsonFileController.GetValueByKeyAsync<string>(selectedOption);
        var newValue = AnsiConsole.Ask<string>($"[white]Current value is: {currentValue}[/]\n[white]Enter new value for {selectedOption}:[/] ");

        await jsonFileController.UpdateAsync(selectedOption, newValue);

        await DisplayUpdatedJson();
    }

    private async Task DisplayUpdatedJson()
    {
        var jsonString = await jsonFileController.GetJsonStringAsync();
        var json = new JsonText(jsonString)
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
