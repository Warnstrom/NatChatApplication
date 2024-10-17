using Microsoft.Extensions.Configuration;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using Spectre.Console;
using System;
public interface IOBSWebSocketService
{
    Task Connect();        // Connect to the websocket server.
    void Disconnect();        // Disconnect to the websocket server.
    bool IsConnected();
    void UnmuteMicrophone();
    void MuteMicrophone();
    void UpdateSourceVisibility(string sourceName, bool visible);
    List<string> GetScenes();
    List<string> GetSceneSources();
    List<string> GetAudioInput();
}

// Implementation of the Twitch EventSub listener.
public class OBSWebSocketService(IConfiguration configuration) : IOBSWebSocketService
{
    public OBSWebsocket _obs;

    // Initialization method to set up the WebSocket and subscribe to events
    public void InitializeOBSWebsocket()
    {
        _obs = new OBSWebsocket();
        _obs.Connected += Obs_Connected;
        _obs.Disconnected += Obs_Disconnected;
    }

    public async Task Connect()
    {
        string obsIp = configuration["OBS_IP"];
        string obsPort = configuration["OBS_PORT"];
        string obsPassword = configuration["OBS_Password"];
        string obsUrl = $"ws://{obsIp}:{obsPort}";

        try
        {
            // Log the connection attempt
            AnsiConsole.Markup("\n[bold yellow]Attempting to connect to OBS WebSocket...[/]\n");

            // Asynchronous connection to OBS
            _obs.ConnectAsync(obsUrl, obsPassword);

        }
        catch (AuthFailureException)
        {
            // Log authentication failure
            AnsiConsole.Markup("\n[bold red]Error: Authentication failed when connecting to OBS WebSocket.[/]\n");
        }
        catch (ErrorResponseException ex)
        {
            // Log specific OBS error messages
            AnsiConsole.Markup($"\n[bold red]Error: Failed to connect to OBS. Details: {ex.Message}[/]\n");

        }
        catch (Exception ex)
        {
            // Log any unexpected errors
            AnsiConsole.Markup($"[bold red]Unexpected error: {ex.Message}[/]\n");
        }
    }

    public bool IsConnected()
    {
        return _obs.IsConnected;
    }

    public void Disconnect()
    {
        if (_obs.IsConnected)
        {
            _obs.Disconnect();
            AnsiConsole.Markup("[bold yellow]OBS WebSocket disconnected successfully.[/]\n");
        }
        else
        {
            AnsiConsole.Markup("[bold red]OBS WebSocket is not connected.[/]\n");
        }
    }

    // Event handler for successful connection
    private void Obs_Connected(object? sender, EventArgs e)
    {
        AnsiConsole.Markup("\n[bold green]Connected to OBS WebSocket![/]\n");
    }

    // Event handler for disconnection
    private void Obs_Disconnected(object? sender, ObsDisconnectionInfo e)
    {
        // Notify the user to check WebSocket settings in OBS
        AnsiConsole.Markup($"[bold yellow]Make sure OBS is open and WebSocket is enabled in OBS > Tools > WebSocket Server Settings[/]\n");

        // Handle specific OBS close codes
        switch (e.ObsCloseCode)
        {
            case ObsCloseCodes.AuthenticationFailed:
                AnsiConsole.Markup($"[bold red]Failed to connect: Incorrect WebSocket password. Please verify your password in OBS WebSocket Server settings.[/]\n");
                break;
            case ObsCloseCodes.MissingDataField:
                AnsiConsole.Markup($"[bold red]Connection error: Missing data in the WebSocket message. Please verify your OBS WebSocket version.[/]\n");
                break;
            case ObsCloseCodes.InvalidDataFieldType:
            case ObsCloseCodes.InvalidDataFieldValue:
                AnsiConsole.Markup($"[bold red]Connection error: Invalid data field or value in the WebSocket message. Ensure correct setup in OBS WebSocket.[/]\n");
                break;
            case ObsCloseCodes.UnknownOpCode:
                AnsiConsole.Markup($"[bold red]Connection error: Unknown operation code in WebSocket message. Check if the OBS WebSocket version is up to date.[/]\n");
                break;
            case ObsCloseCodes.SessionInvalidated:
                AnsiConsole.Markup($"[bold red]Connection error: WebSocket session invalidated. Please restart OBS and try reconnecting.[/]\n");
                break;
            case ObsCloseCodes.UnsupportedFeature:
                AnsiConsole.Markup($"[bold red]Connection error: Requested feature is unsupported due to hardware or software limitations.[/]\n");
                break;
            case ObsCloseCodes.UnknownReason:
                AnsiConsole.Markup($"[bold red]Unknown Error. Reason: Unknown reason (Code: {e.ObsCloseCode}).[/]\n");
                break;
            default:
                // Generic message for unrecognized close codes
                AnsiConsole.Markup($"[bold red]Unknown Error. Reason: Unknown error (Code: {e.ObsCloseCode}).[/]\n");
                break;
        }

        // Show additional exception message if available
        if (e.WebsocketDisconnectionInfo?.Exception != null)
        {
            AnsiConsole.Markup($"[bold red]Additional info: {e.WebsocketDisconnectionInfo.Exception.Message}[/]\n");
        }
    }

    public List<string> GetScenes()
    {
        if (_obs.IsConnected)
        {
            try
            {
                var sceneItems = _obs.GetSceneList();
                return sceneItems.Scenes.Select(x => x.Name).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error?!?!: {ex.Message}");
            }
        }
        return null;
    }

    private string GetPlatformSpecificInputCapture()
    {
        PlatformID platform = Environment.OSVersion.Platform;

        // Depending on the platform, return the correct input capture string
        return platform switch
        {
            PlatformID.Unix => "pulse_input_capture",     
            PlatformID.MacOSX => "coreaudio_input_capture",
            PlatformID.Win32NT => "wasapi_input_capture",  
            _ => throw new NotSupportedException("Platform not supported for input capture.")
        };
    }

    public List<string> GetAudioInput()
    {
        try
        {
            string inputCaptureType = GetPlatformSpecificInputCapture();

            List<string> sceneItems = _obs.GetInputKindList();

            List<string> audioInputs = _obs.GetInputList(inputCaptureType)
                .Select(x => x.InputName)
                .ToList();

            return audioInputs;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            return new List<string>();
        }
    }

    public List<string> GetSceneSources()
    {
        if (_obs.IsConnected)
        {
            try
            {
                // Get the scene item ID for the source
                List<OBSWebsocketDotNet.Types.SceneItemDetails> sceneItems = _obs.GetSceneItemList(configuration["OBS_Scene"]);
                List<string> sourcelist = sceneItems.Select(x => x.SourceName).ToList();
                return sourcelist;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error?!?!: {ex.Message}");
            }
        }
        return new List<string>();
    }


    // Mute the microphone source
    public void MuteMicrophone()
    {
        if (_obs.IsConnected)
        {
            try
            {
                _obs.SetInputMute(configuration["OBS_MicName"], true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error muting microphone: {ex.Message}");
            }
        }
    }

    // Unmute the microphone source
    public void UnmuteMicrophone()
    {
        if (_obs.IsConnected)
        {
            try
            {
                _obs.SetInputMute(configuration["OBS_MicName"], false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unmuting microphone: {ex.Message}");
            }
        }
    }
    public void UpdateSourceVisibility(string sourceName, bool visible)
    {
        if (_obs.IsConnected)
        {
            try
            {
                // Get the scene item ID for the source
                List<OBSWebsocketDotNet.Types.SceneItemDetails> sceneItems = _obs.GetSceneItemList(configuration["OBS_Scene"]);
                OBSWebsocketDotNet.Types.SceneItemDetails SourceId = sceneItems.Find(x => x.SourceName == sourceName);
                // Show the source by enabling it
                _obs.SetSceneItemEnabled(configuration["OBS_Scene"], SourceId.ItemId, visible);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error showing source: {ex.Message}");
            }
        }
    }

}
