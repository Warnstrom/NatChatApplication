using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Spectre.Console;
using Microsoft.Extensions.Configuration;

// Classes representing the event payload structure for subscribing to events.
public class SubscribeEventPayload
{
    public string? type { get; set; }         // Type of event to subscribe to.
    public string? version { get; set; }      // Version of the event subscription.
    public Condition? condition { get; set; } // Conditions required for the event.
    public Transport? transport { get; set; } // Transport details for event subscription.
}

public class Condition
{
    public string? broadcaster_user_id { get; set; } // Twitch broadcaster's user ID.
    public string? user_id { get; set; }             // User ID of the person triggering the event (optional).
}

public class Transport
{
    public string? method { get; set; }     // Method used for transport (e.g., websocket).
    public string? session_id { get; set; } // Session ID for the websocket connection.
}

// Interface for the Twitch EventSub listener.
public interface ITwitchEventSubListener
{
    Task ValidateAndConnectAsync(Uri websocketUrl);        // Connect to the websocket server.
    Task ListenForEventsAsync();                           // Listen for incoming events.
}

// Implementation of the Twitch EventSub listener.
public class TwitchEventSubListener(IConfiguration configuration, TwitchLib.Api.TwitchAPI api,
IJsonFileController jsonFileController, ArgsService argsService, ITwitchHttpClient twitchHttpClient, OBSWebSocketService OBSWebSocket) : ITwitchEventSubListener
{
    private ClientWebSocket? _webSocket;                                         // Web socket for connecting to Twitch EventSub.
    // Method to connect to the Twitch EventSub websocket.
    public async Task ValidateAndConnectAsync(Uri websocketUrl)
    {
        // Check if the OAuth token is valid.
        if (await api.Auth.ValidateAccessTokenAsync() == null)
        {
            string refreshToken = configuration["RefreshToken"];
            if (!string.IsNullOrEmpty(refreshToken))
            {
                AnsiConsole.Markup("[yellow]AccessToken is invalid, refreshing for a new token...[/]\n");
                TwitchLib.Api.Auth.RefreshResponse? refresh = await api.Auth.RefreshAuthTokenAsync(refreshToken, configuration["ClientSecret"], configuration["ClientId"]);
                api.Settings.AccessToken = refresh.AccessToken;
                // Update the AccessToken in the configuration file.
                await jsonFileController.UpdateAsync("AccessToken", refresh.AccessToken);
                await twitchHttpClient.UpdateOAuthToken(refresh.AccessToken);
            }
        }
        // Initialize the web socket connection.
        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Client-Id", configuration["ClientId"]);
        _webSocket.Options.SetRequestHeader("Authorization", "Bearer " + $"oauth:{api.Settings.AccessToken}");
        _webSocket.Options.SetRequestHeader("Content-Type", "application/json");
        await _webSocket.ConnectAsync(websocketUrl, CancellationToken.None);
        AnsiConsole.MarkupLine("[bold green]Successfully connected to Twitch Redemption Service[/]");
    }

    // Subscribe to channel point reward redemptions.
    private async Task SubscribeToChannelPointRewardsAsync(string sessionId)
    {
        var eventPayload = new SubscribeEventPayload
        {
            type = "channel.channel_points_custom_reward_redemption.add", // Event type for channel points redemption.
            version = "1",
            condition = new Condition
            {
                broadcaster_user_id = configuration["ChannelId"]
            },
            transport = new Transport
            {
                method = "websocket",
                session_id = sessionId,
            }
        };

        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Subscribe to chat messages for local testing (not used in production).
    private async Task SubscribeToChannelChatMessageAsync(string sessionId)
    {
        var eventPayload = new SubscribeEventPayload
        {
            type = "channel.chat.message", // Event type for chat messages.
            version = "1",
            condition = new Condition
            {
                broadcaster_user_id = configuration["ChannelId"],
                user_id = configuration["ChannelId"]
            },
            transport = new Transport
            {
                method = "websocket",
                session_id = sessionId
            }
        };

        await SendMessageAsync(eventPayload); // Send the subscription request.
    }

    // Method to send a subscription request to the Twitch API.
    private async Task SendMessageAsync(SubscribeEventPayload eventPayload)
    {
        string payload = JsonConvert.SerializeObject(eventPayload);
        try
        {
            HttpResponseMessage response = await twitchHttpClient.PostAsync("AddSubscription", payload);
            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[bold green]Successfully subscribed to Twitch Redemption Service Event:[/] [bold yellow]{eventPayload.type}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[bold red]Failed to subscribe to Twitch Redemption Service Event:[/] [bold yellow]{eventPayload.type}[/]");
                AnsiConsole.MarkupLine($"[bold teal]Reason:[/] [bold white]{response.StatusCode}[/]");
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine("HTTP request exception: " + e.Message);
        }
    }

    // Method to listen for incoming events from the WebSocket connection.
    public async Task ListenForEventsAsync()
    {
        const int maxBufferSize = 1024; // Buffer size for incoming WebSocket messages.
        var buffer = new byte[maxBufferSize]; // Buffer for temporarily holding received data.
        var messageBuffer = new MemoryStream(); // MemoryStream to store the full message across multiple fragments.

        try
        {
            while (_webSocket.State == WebSocketState.Open) // Continue reading messages while the WebSocket is open.
            {
                // Receive message from the WebSocket.
                WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                // Check if the received message is a close request.
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Display the close status and description.
                    Console.WriteLine(result.CloseStatus);
                    Console.WriteLine(result.CloseStatusDescription);
                    await AttemptReconnectAsync(); // Attempt to reconnect after closing.
                }

                // Handle text messages received from the WebSocket.
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    await messageBuffer.WriteAsync(buffer, 0, result.Count); // Write the received data to the buffer.

                    // If this is the last fragment, process the full message.
                    if (result.EndOfMessage)
                    {
                        messageBuffer.Seek(0, SeekOrigin.Begin); // Reset the stream position.
                        var payloadJson = await ReadMessageAsync(messageBuffer); // Read the full message as a JSON string.
                        await HandleEventNotificationAsync(payloadJson); // Handle the parsed message.
                        messageBuffer.SetLength(0); // Clear the message buffer for the next message.
                    }
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // Handle cases where the WebSocket connection closes unexpectedly.
            AnsiConsole.MarkupLine($"[bold red]Twitch Redemption Service connection closed prematurely.[/]");
            AnsiConsole.MarkupLine($"[bold yellow]Reason: {ex.Message}[/]");

            await AttemptReconnectAsync(); // Attempt to reconnect after the error.
        }
        catch (Exception ex)
        {
            // Log any other exceptions that occur.
            Console.WriteLine($"An error occurred while listening for events: {ex.Message}");
            Console.WriteLine($"{ex}");
        }
        finally
        {
            messageBuffer.Dispose(); // Dispose of the message buffer when finished.
        }
    }

    // Method to attempt reconnection to the WebSocket if the connection is lost.
    private async Task AttemptReconnectAsync()
    {
        const int maxAttempts = 5; // Maximum number of reconnection attempts.
        const int delayBetweenAttemptsMs = 2500; // Delay between each reconnection attempt (in milliseconds).

        for (int attempt = 0; attempt <= maxAttempts; attempt++)
        {
            try
            {
                AnsiConsole.MarkupLine($"[bold yellow]Attempting to reconnect... (Attempt {attempt}/{maxAttempts})[/]");

                const string ws = "wss://eventsub.wss.twitch.tv/ws";
                await ValidateAndConnectAsync(new Uri(ws)); // Attempt connection.

                if (_webSocket.State == WebSocketState.Open)
                {
                    AnsiConsole.MarkupLine("[bold green]Reconnected successfully![/]");
                    await ListenForEventsAsync(); // Start listening for events again.
                    return;
                }
            }
            catch (Exception ex)
            {
                // Log any errors during the reconnection attempt.
                AnsiConsole.MarkupLine($"[bold red]Reconnection attempt failed: {ex.Message}[/]");
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayBetweenAttemptsMs); // Wait before attempting the next reconnection.
            }
        }

        // If all reconnection attempts fail, log an error message.
        AnsiConsole.MarkupLine("[bold red]Max reconnection attempts reached. Could not reconnect to WebSocket.[/]");
    }

    // Helper method to read the full message from the message buffer.
    private static async Task<string> ReadMessageAsync(Stream messageBuffer)
    {
        using var reader = new StreamReader(messageBuffer, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    // Method to handle event notifications received from the WebSocket.
    private async Task HandleEventNotificationAsync(string payloadJson)
    {
        var payload = JObject.Parse(payloadJson); // Parse the JSON payload.
        string MessageType = (string)payload["metadata"]["message_type"]; // Extract the message type.

        // Dictionary mapping message types to their respective handler methods.
        var handlers = new Dictionary<string, Func<JObject, Task>>
        {
            { "session_welcome", HandleSessionWelcomeAsync },
            { "session_keepalive", HandleKeepAliveAsync },
            { "session_reconnect", HandleReconnectAsync },
            { "notification", HandleNotificationAsync },
        };

        // Check if a handler exists for the received message type.
        if (handlers.TryGetValue(MessageType, out var handler))
        {
            await handler(payload); // Invoke the appropriate handler.
        }
        else
        {
            Console.WriteLine("Unhandled message type: " + MessageType); // Log unhandled message types.
        }
    }

    // Method to handle the "session_reconnect" message type.
    private async Task HandleReconnectAsync(JObject payload)
    {
        try
        {
            // If the WebSocket is open, close it gracefully before reconnecting.
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", CancellationToken.None);
                AnsiConsole.MarkupLine("[bold yellow]Disconnecting from Twitch Redemption Service[/]");
                DisposeWebSocket(); // Dispose of the existing WebSocket.
            }

            // Extract the reconnect URL from the payload.
            string reconnectUrl = (string)payload["payload"]["session"]["reconnect_url"];

            // Validate and connect using the new reconnect URL.
            if (Uri.TryCreate(reconnectUrl, UriKind.Absolute, out Uri? uri))
            {
                AnsiConsole.MarkupLine("[bold yellow]Reconnecting to Twitch Redemption Service[/]");
                await ValidateAndConnectAsync(uri);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error during reconnect: " + ex.Message); // Log errors during reconnection.
        }
    }

    // Method to handle the "session_welcome" message type.
    private async Task HandleSessionWelcomeAsync(JObject payload)
    {
        string sessionId = (string)payload["payload"]["session"]["id"]; // Extract the session ID.

        if (argsService.Args.FirstOrDefault() == "dev")
        {
            Console.WriteLine($"SessionID: {sessionId}"); // Log the session ID in development mode.
        }

        // Subscribe to various Twitch events using the session ID.
        await SubscribeToChannelPointRewardsAsync(sessionId);
        await SubscribeToChannelChatMessageAsync(sessionId);
    }

    // Method to handle notifications received from Twitch.
    private async Task HandleNotificationAsync(JObject payload)
    {
        try
        {
            // Extract the event type from the payload.
            string eventType = payload["payload"]["subscription"]["type"].ToString();

            // Determine how to handle the event based on its type.
            switch (eventType)
            {
                case "channel.channel_points_custom_reward_redemption.add":
                    await HandleCustomRewardRedemptionAsync(payload);
                    break;
                case "channel.chat.message":
                    if (argsService.Args.Length != 0 && argsService.Args[0] == "dev")
                    {
                        if (payload["payload"]["event"]["chatter_user_login"].ToString() == "noraschair" || payload["payload"]["event"]["chatter_user_login"].ToString() == "chayzeruh")
                        {
                            if (payload["payload"]["event"]["message"]["text"].ToString() == "test")
                            {
                                await HandleIRLVoiceBanRedeem(payload["payload"]["event"]["chatter_user_login"].ToString());
                            }
                            if (payload["payload"]["event"]["message"]["text"].ToString() == "mute")
                            {
                                OBSWebSocket.MuteMicrophone();
                            }
                            if (payload["payload"]["event"]["message"]["text"].ToString() == "unmute")
                            {
                                OBSWebSocket.UnmuteMicrophone();
                            }
                            if (payload["payload"]["event"]["message"]["text"].ToString() == "show")
                            {
                                OBSWebSocket.UpdateSourceVisibility(configuration["OBS_SourceName"], true);
                            }
                            if (payload["payload"]["event"]["message"]["text"].ToString() == "hide")
                            {
                                OBSWebSocket.UpdateSourceVisibility(configuration["OBS_SourceName"], false);
                            }
                        }
                    }
                    break;
                default:
                    Console.WriteLine("Unhandled event type: " + eventType); // Log unhandled event types.
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error handling notification: " + ex.Message); // Log errors while handling notifications.
        }
    }

    // Method to handle custom reward redemptions from Twitch.
    private async Task HandleCustomRewardRedemptionAsync(JObject payload)
    {
        string RewardTitle = payload["payload"]["event"]["reward"]["title"].ToString();
        string RedeemUsername = payload["payload"]["event"]["user_name"].ToString();

        switch (RewardTitle)
        {
            case "IRL voice ban":
                await HandleIRLVoiceBanRedeem(RedeemUsername);
                break;
            default:
                Console.WriteLine("Unknown command: " + RewardTitle); // Log unknown commands.
                break;
        }
    }
    private async Task HandleIRLVoiceBanRedeem(string RedeemUsername)
    {
        AnsiConsole.Markup($"[bold yellow]{RedeemUsername} requested the Voice Ban Redeem[/]\n");

        OBSWebSocket.MuteMicrophone();
        OBSWebSocket.UpdateSourceVisibility(configuration["OBS_SourceName"], true);

        AnsiConsole.Markup("[bold yellow]Voice ban active. Countdown: 2 minutes[/]\n");

        var countdownTime = TimeSpan.FromMinutes(2);

        await AnsiConsole.Status()
            .StartAsync("Waiting for 2 minutes...", async ctx =>
            {
                while (countdownTime.TotalSeconds > 0)
                {
                    ctx.Status = $"[bold yellow]Remaining time: {countdownTime.Minutes:D2}:{countdownTime.Seconds:D2}[/]";

                    await Task.Delay(1000);
                    countdownTime = countdownTime.Subtract(TimeSpan.FromSeconds(1));
                }
            });

        // Once the timer completes, unmute the microphone and hide the source
        OBSWebSocket.UnmuteMicrophone();
        OBSWebSocket.UpdateSourceVisibility(configuration["OBS_SourceName"], false);

        AnsiConsole.Markup("[bold yellow]Voice Ban Redeem request completed![/]\n");
    }


    // Handle the "session_keepalive" event type (currently does nothing).
    private Task HandleKeepAliveAsync(JObject payload)
    {
        return Task.CompletedTask;
    }

    // Dispose of the WebSocket when finished.
    private void DisposeWebSocket()
    {
        _webSocket?.Dispose();
        _webSocket = null;
    }
}