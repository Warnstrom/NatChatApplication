using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text;
using TwitchLib.Api.Core.Exceptions;

namespace TwitchChatHueControls
{
    class Program
    {
        private static string SettingsFile = "";

        private static async Task Main(string[] args)
        {
            SettingsFile = args.FirstOrDefault() == "dev" ? "devmodesettings.json" : "appsettings.json";
            try
            {
                // Create a new ServiceCollection (IoC container) for dependency injection
                var serviceCollection = new ServiceCollection();

                // Register and configure the required services
                ConfigureServices(serviceCollection, args);

                // Build the service provider to resolve dependencies
                ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

                // Run the application by resolving the main App class and calling its RunAsync method
                await serviceProvider.GetRequiredService<App>().RunAsync();
            }
            catch (Exception ex)
            {
                // Error handling with Spectre.Console for better visual output
                // This part displays an ASCII art with a message when an exception occurs
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine("                    ____________________________");
                Console.WriteLine("                   / Oops, something went wrong. \\");
                Console.WriteLine("                   \\     Please try again :3     /");
                Console.WriteLine("                  / ‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾‾");
                Console.WriteLine("　　　　　   __  /");
                Console.WriteLine("　　　　 ／フ   フ");
                Console.WriteLine("　　　　|  .   .|");
                Console.WriteLine("　 　　／`ミ__xノ");
                Console.WriteLine("　 　 /　　 　 |");
                Console.WriteLine("　　 /　 ヽ　　ﾉ");
                Console.WriteLine(" 　 │　　 | | |");
                Console.WriteLine("／￣|　　 | | |");
                Console.WriteLine("| (￣ヽ_ヽ)_)__)");
                Console.WriteLine("＼二つ");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything); // Display the exception with Spectre.Console's enhanced formatting
            }
            finally
            {
                // Prompt user to press Enter to exit the application
                AnsiConsole.Markup("[bold yellow]Press [green]Enter[/] to exit.[/]");
                Console.ReadLine();
            }
        }

        // Method to configure the services needed by the application
        private static void ConfigureServices(IServiceCollection services, string[] args)
        {
            // Create and configure the ConfigurationBuilder to load settings from the specified JSON file
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(SettingsFile, optional: true, reloadOnChange: true);
            // Build the configuration object
            IConfigurationRoot configurationRoot = configurationBuilder.Build();
            // Register the required services and controllers65
            services.AddSingleton<IConfiguration>(configurationRoot);
            services.AddSingleton<IConfigurationRoot>(configurationRoot);
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<IJsonFileController>(sp => new JsonFileController(SettingsFile, configurationRoot));
            services.AddSingleton(new ArgsService(args));
            services.AddSingleton<TwitchLib.Api.TwitchAPI>();
            services.AddSingleton<TwitchEventSubListener>();
            services.AddSingleton<OBSWebSocketService>();
            services.AddScoped<ITwitchHttpClient, TwitchHttpClient>();
            services.AddSingleton<WebServer>();
            // Register the main application entry point
            services.AddTransient<App>();
        }
    }

    // The main application class, handling the core flow of the program
    public class App(IConfiguration configuration, IConfigurationService configurationEditor, IJsonFileController jsonController,
            TwitchLib.Api.TwitchAPI api, TwitchEventSubListener eventSubListener, WebServer webServer,
            ITwitchHttpClient twitchHttpClient, OBSWebSocketService OBSWebSocket)
    {
        // The main run method to start the app's functionality
        public async Task RunAsync()
        {
            await StartMenu();
        }

        // Method to handle the main menu flow
        private async Task StartMenu()
        {
            while (true)
            {
                // Render the start menu and get the user's choice
                byte choice = await RenderStartMenu();

                switch (choice)
                {
                    case 1:
                        await ConfigureTwitchTokens();
                        break;
                    case 2:
                        await ConfigureOBSWebSocketInfo();
                        break;
                    case 3:
                        AnsiConsole.Markup("[yellow]Opening app configuration for editing...[/]\n");
                        await configurationEditor.EditConfigurationAsync(); // Start the main application
                        break;
                    case 4:
                        AnsiConsole.Markup("[green]Starting the application...[/]\n");
                        await StartApp(); // Start the main application
                        break;
                    case 5:
                        AnsiConsole.Markup("[red]Exiting application...[/]\n");
                        Environment.Exit(0);
                        break;
                }

            }
        }



        // Method to render the start menu using Spectre.Console
        private async Task<byte> RenderStartMenu()
        {
            // Check if Twitch and OBS are configured
            bool twitchConfigured = await ValidateTwitchConfiguration();
            bool OBS_Configured = ValidateOBSConfiguration();

            // Style definitions for borders and text
            var borderStyle = new Style(foreground: Color.White, decoration: Decoration.Bold);

            // Create a visually appealing table for the start menu
            var table = new Table()
                .Title("[underline bold yellow]Welcome To Nat's Channel Application[/]")  // Title of the application
                .Border(TableBorder.Rounded)                                              // Rounded borders for a friendly look
                .BorderColor(Color.DeepSkyBlue4)                                           // Border color
                .BorderStyle(borderStyle)                                                  // Border style with bold text
                .AddColumn(new TableColumn("[bold gold3_1]Main Menu[/]").LeftAligned());   // Left aligned for clearer UX

            // Welcome message with additional tips
            table.AddRow("[bold cyan]This application helps you manage Twitch and OBS configurations.[/]");
            table.AddRow("[bold cyan]Select an option below to proceed.[/]");
            table.AddEmptyRow();  // Add a blank row for spacing

            // Configuration statuses with consistent color coding for statuses
            string twitchStatus = twitchConfigured ? "[green]Complete[/]" : "[red]Incomplete[/]";
            string OBS_Status = OBS_Configured ? "[green]Complete[/]" : "[red]Incomplete[/]";

            // Menu options with dynamic statuses and icons for better clarity
            table.AddRow($"[bold gold3_1]1.[/] [white]Connect to Twitch[/] ({twitchStatus})");
            table.AddRow($"[bold gold3_1]2.[/] [white]Connect to OBS[/] ({OBS_Status})");
            table.AddRow("[bold gold3_1]3.[/] [white]Edit App Configuration[/]");
            table.AddRow("[bold gold3_1]4.[/] [white]Start App[/]");
            table.AddRow("[bold gold3_1]5.[/] [white]Quit Application[/]");

            table.AddEmptyRow();  // Add a blank row for spacing

            // Instructions to guide the user
            table.AddRow("[bold aqua]Instructions:[/]");
            table.AddRow("[white]Use [bold yellow]arrow keys[/] to navigate and [bold yellow]Enter[/] to select.[/]");

            // Render the table with the updated options
            AnsiConsole.Write(table);

            // Prompt the user to select an option
            var prompt = new SelectionPrompt<int>()
                .Title("[grey]Select an option:[/]")
                .AddChoices(new[] {
            twitchConfigured ? -1 : 1, // Disable Twitch connect if already configured
            OBS_Configured ? -2 : 2,   // Disable OBS connect if already configured
            3, 4, 5 // App Configuration, Start, and Quit options are always enabled
                })
                .UseConverter(option => option switch
                {
                    1 => "[dim white]»[/] [white]Connect to Twitch[/]",
                    2 => "[dim white]»[/] [white]Connect to OBS[/]",
                    -1 => "[dim grey]»[/] [grey]Connect to Twitch (Configured)[/]",  // Disabled menu for Twitch
                    -2 => "[dim grey]»[/] [grey]Connect to OBS (Configured)[/]",     // Disabled menu for OBS
                    3 => "[dim white]»[/] [white]Edit App Configuration[/]",
                    4 => "[dim white]»[/] [white]Start App[/]",
                    5 => "[dim white]»[/] [white]Quit Application[/]",
                    _ => "[dim white]»[/] [white]Unknown Option[/]"
                })
                .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for selected option
                .Mode(SelectionMode.Leaf) // Leaf mode for modern selection UX
                .WrapAround(false)        // Disable wrap-around behavior
                .PageSize(5);             // Fit all options on one page

            // Get the selected option
            byte selectedOption = (byte)AnsiConsole.Prompt(prompt);

            // Provide immediate feedback based on user selection
            return selectedOption;
        }

        // Helper method for animated connection with spinner
        private async Task ConnectWithAnimationAsync(string serviceName, Func<Task<bool>> connectionAction)
        {
            await AnsiConsole.Status()
                .StartAsync($"[yellow]Connecting to {serviceName}...[/]", async ctx =>
                {
                    // Simulate a delay for the connection attempt
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(new Style(foreground: Color.LightSkyBlue1));

                    // Perform the connection action
                    bool success = await connectionAction.Invoke();

                    // Adjust the status based on success
                    if (success)
                    {
                        AnsiConsole.Markup($"[bold green]{serviceName} connected successfully![/]\n");
                    }
                    else
                    {
                        AnsiConsole.Markup($"[bold red]Failed to connect to {serviceName}. Check your configuration.[/]\n");
                    }
                });
        }


        // Method to validate Twitch configuration
        private async Task<bool> ValidateTwitchConfiguration()
        {
            string RefreshToken = configuration["RefreshToken"]; // Get the refresh token from the configuration

            if (string.IsNullOrEmpty(RefreshToken))
            {
                return false; // Return false if no refresh token is found
            }

            // Set the access token for the API and validate it
            api.Settings.AccessToken = configuration["AccessToken"];
            if (await api.Auth.ValidateAccessTokenAsync() != null)
            {
                return true;
            }
            else
            {
                try
                {
                    // Refresh the access token because it's invalid
                    api.Settings.ClientId = configuration["ClientId"];
                    AnsiConsole.Markup("[yellow]AccessToken is invalid, refreshing for a new token...[/]\n");
                    TwitchLib.Api.Auth.RefreshResponse refresh = await api.Auth.RefreshAuthTokenAsync(RefreshToken, configuration["ClientSecret"], configuration["ClientId"]);
                    api.Settings.AccessToken = refresh.AccessToken;
                    // Update the access token in the configuration file
                    await jsonController.UpdateAsync("AccessToken", refresh.AccessToken);
                    await twitchHttpClient.UpdateOAuthToken(refresh.AccessToken);
                    return true;
                }
                catch (BadRequestException ex)
                {
                    Console.WriteLine(ex.Message); // Log any exceptions during the refresh process
                    return false;
                }
            }
        }

        // Method to start the main application
        private async Task StartApp()
        {
            // Validate the Twitch configuration before proceeding
            bool twitchConfigured = await ValidateTwitchConfiguration();
            bool OBS_Configured = ValidateOBSConfiguration();

            if (!twitchConfigured)
            {
                AnsiConsole.Markup("[bold red]\nError: Twitch Configuration is incomplete.\n[/]");
                return;
            }
            else if (!OBS_Configured)
            {
                AnsiConsole.Markup("[bold red]\nError: OBS Configuration is incomplete.\n[/]");
                return;
            }
            else
            {
                if (OBSWebSocket._obs == null)
                {
                    OBSWebSocket.InitializeOBSWebsocket();
                }
                await EnsureOBSConnectionAsync();

                const string ws = "wss://eventsub.wss.twitch.tv/ws"; // Twitch EventSub websocket endpoint
                await eventSubListener.ValidateAndConnectAsync(new Uri(ws)); // Connect to the EventSub websocket
                await eventSubListener.ListenForEventsAsync(); // Start listening for events

            }
        }
        private bool ValidateOBSConfiguration()
        {
            string[] requiredKeys = ["OBS_IP", "OBS_PORT", "OBS_Password", "OBS_Scene", "OBS_MicName", "OBS_SourceName"];

            foreach (string key in requiredKeys)
            {
                if (string.IsNullOrWhiteSpace(configuration[key]))
                {
                    Console.WriteLine($"Configuration for {key} is missing or empty.");
                    return false;
                }
            }
            return true;
        }

        // Method to configure Twitch OAuth tokens
        private async Task<bool> ConfigureOBSWebSocketInfo()
        {
            OBSWebSocket.InitializeOBSWebsocket();

            await ConfigureOBSConnectionInfo();
            await EnsureOBSConnectionAsync();

            // Configure Scene, Source, and Mic Name if not set
            await ConfigureIfMissing("OBS_Scene", "Select a scene:", OBSWebSocket.GetScenes);
            await ConfigureIfMissing("OBS_SourceName", "Select a source:", OBSWebSocket.GetSceneSources);
            await ConfigureIfMissing("OBS_MicName", "Select an input source:", OBSWebSocket.GetAudioInput);

            bool obsConfigured = ValidateOBSConfiguration();
            return obsConfigured;
        }

        // Method to configure OBS connection information
        private async Task ConfigureOBSConnectionInfo()
        {
            string obsIp = await GetOrUpdateConfigValue("OBS_IP", "[yellow]Please enter the OBS Websocket IP Address:[/]");
            string obsPort = await GetOrUpdateConfigValue("OBS_Port", "[yellow]Please enter the OBS Port:[/]");
            string obsPassword = await GetOrUpdateConfigValue("OBS_Password", "[yellow]Please enter the OBS Websocket Password:[/]");
        }

        // Helper method to ensure OBS is connected
        private async Task EnsureOBSConnectionAsync(int maxRetries = 10, int delayInSeconds = 5)
        {
            int retryCount = 0;

            while (!OBSWebSocket.IsConnected() && retryCount < maxRetries)
            {
                AnsiConsole.Markup($"[yellow]Waiting for OBS to connect... Attempt {retryCount + 1}/{maxRetries}[/]\n");

                // Try to reconnect if OBS is not connected
                await OBSWebSocket.Connect();

                // Wait for the specified delay before trying again
                await Task.Delay(delayInSeconds * 1000);

                retryCount++;
            }

            // If still not connected after retries, notify the user and prompt for action
            if (!OBSWebSocket.IsConnected())
            {
                bool retry = AnsiConsole.Confirm("[red]OBS is still not connected. Do you want to keep trying?[/]", false);

                if (retry)
                {
                    // Recursively retry again with the same number of max retries
                    await EnsureOBSConnectionAsync(maxRetries, delayInSeconds);
                }
                else
                {
                    AnsiConsole.Markup("[bold red]Failed to connect to OBS after multiple attempts.[/]\n");
                }
            }
            else
            {
                AnsiConsole.Markup("[green]Successfully connected to OBS![/]\n");
            }
        }

        // Generic method to configure a value if it's missing
        private async Task ConfigureIfMissing(string configKey, string promptTitle, Func<List<string>> getChoicesFunc)
        {
            string configValue = configuration[configKey];

            if (string.IsNullOrEmpty(configValue))
            {
                List<string> choices = getChoicesFunc.Invoke();
                string selectedOption = null;
                if (choices.Count > 0)
                {
                    var prompt = new SelectionPrompt<string>()
                        .Title($"[grey]{promptTitle}[/]")
                        .AddChoices(choices)
                        .HighlightStyle(new Style(foreground: Color.LightSkyBlue1)) // Subtle blue highlight for selected option
                        .Mode(SelectionMode.Leaf) // Leaf mode for modern selection UX
                        .WrapAround(false)        // Disable wrap-around behavior
                        .PageSize(5);            // Fit all options on one page

                    // Get the selected option
                    selectedOption = AnsiConsole.Prompt(prompt);
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]Couldn't find ({configKey}) you can add/change this value manually later.[/]");
                    selectedOption = "";
                }
                await jsonController.UpdateAsync(configKey, selectedOption);
            }
        }

        // Helper method to retrieve or update a configuration value
        private async Task<string> GetOrUpdateConfigValue(string key, string prompt)
        {
            string value = configuration[key];
            if (string.IsNullOrEmpty(value))
            {
                value = AnsiConsole.Ask<string>(prompt);
                await jsonController.UpdateAsync(key, value);
            }
            return value;
        }


        // Method to configure Twitch OAuth tokens
        private async Task<bool> ConfigureTwitchTokens()
        {
            string clientId = configuration["ClientId"];
            string clientSecret = configuration["ClientSecret"];

            // Check if ClientId or ClientSecret is missing
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                AnsiConsole.Markup("[bold red]ClientId or ClientSecret not found in configuration file![/]\n");

                // Prompt user for missing ClientId
                if (string.IsNullOrEmpty(clientId))
                {
                    clientId = AnsiConsole.Ask<string>("[yellow]Please enter the ClientId:[/]");
                    await jsonController.UpdateAsync("ClientId", clientId); // Save to appsettings.json
                }

                // Prompt user for missing ClientSecret
                if (string.IsNullOrEmpty(clientSecret))
                {
                    clientSecret = AnsiConsole.Ask<string>("[yellow]Please enter the ClientSecret:[/]");
                    await jsonController.UpdateAsync("ClientSecret", clientSecret); // Save to appsettings.json
                }

                AnsiConsole.Markup("[green]ClientId and ClientSecret have been updated in appsettings.json.[/]\n");
            }
            // List of scopes the application will request
            List<string> scopes = ["channel:bot", "user:read:chat", "channel:read:redemptions", "user:write:chat"];
            string state = RandomStringGenerator.GenerateRandomString(); // Generate a random state for OAuth security
            api.Settings.ClientId = configuration["ClientId"];

            AnsiConsole.Markup($"Please authorize here:\n[link={GetAuthorizationCodeUrl(configuration["ClientId"], configuration["RedirectUri"], scopes, state)}]Authorization Link[/]\n");
            var linkAccessibility = AnsiConsole.Confirm("[yellow]If you are unable to click the link, would you like to see the raw URL?[/]");

            if (linkAccessibility)
            {
                // Provide the raw URL as fallback
                string rawLink = GetAuthorizationCodeUrl(configuration["ClientId"], configuration["RedirectUri"], scopes, state);
                AnsiConsole.Markup($"[bold green]Raw URL:[/] {rawLink}\n");
            }
            // Listen for the OAuth callback and retrieve the authorization code
            Authorization? auth = await webServer.ListenAsync(state);

            if (auth != null)
            {
                // Exchange the authorization code for access and refresh tokens
                TwitchLib.Api.Auth.AuthCodeResponse? resp = await api.Auth.GetAccessTokenFromCodeAsync(auth.Code, configuration["ClientSecret"], configuration["RedirectUri"]);
                api.Settings.AccessToken = resp.AccessToken;
                await jsonController.UpdateAsync("AccessToken", resp.AccessToken);
                await jsonController.UpdateAsync("RefreshToken", resp.RefreshToken);

                var user = (await api.Helix.Users.GetUsersAsync()).Users[0]; // Get user details from Twitch
                await jsonController.UpdateAsync("ChannelId", user.Id);

                // Display a success message with the user information
                AnsiConsole.Write(
                    new Panel($"[bold green]Authorization success![/]\n\n[bold aqua]User:[/] {user.DisplayName} (id: {user.Id})\n[bold aqua]Scopes:[/] :{string.Join(", ", resp.Scopes)}")
                    .BorderColor(Color.Green)
                );
                return true;
            }
            return false;
        }

        // Method to generate the authorization URL for OAuth
        private static string GetAuthorizationCodeUrl(string clientId, string redirectUri, List<string> scopes, string state)
        {
            var scopesStr = string.Join('+', scopes); // Join the requested scopes
            var encodedRedirectUri = System.Web.HttpUtility.UrlEncode(redirectUri); // URL-encode the redirect URI
            return "https://id.twitch.tv/oauth2/authorize?" +
                $"client_id={clientId}&" +
                $"force_verify=true&" +
                $"redirect_uri={encodedRedirectUri}&" +
                "response_type=code&" +
                $"scope={scopesStr}&" +
                $"state={state}";
        }
    }

    // A simple implementation of the XORShift32 PRNG
    public static class XorShift32
    {
        public static uint Next(uint seed)
        {
            uint x = seed;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            seed = x;
            return x;
        }
    }

    // A utility class to generate random
    public static class RandomStringGenerator
    {
        public static string GenerateRandomString(int length = 32)
        {
            const string chars = "7jXb2NEFp9M3hCRKZwBvLziPDSUq5Ixl4y1GtQJcr0HmkOnW6gsToA8fYdeVua";
            var stringBuilder = new StringBuilder(length);

            // Initialize XORShift with a seed (you can use any uint seed)

            for (int i = 0; i < length; i++)
            {
                // Generate a random number and map it to a character in the chars array
                uint randomValue = XorShift32.Next((uint)DateTime.Now.Ticks);
                stringBuilder.Append(chars[(int)(randomValue % chars.Length)]);
            }

            return stringBuilder.ToString();
        }
    }

}