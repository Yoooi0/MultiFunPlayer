using MaterialDesignThemes.Wpf.Converters;
using MultiFunPlayer.Common;
using MultiFunPlayer.Shortcut;
using MultiFunPlayer.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Stylet;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MultiFunPlayer.OutputTarget.ViewModels;

// The Handy streaming output for firmware version 4 and above (using API v3)
[DisplayName("The Handy FW4")]
internal sealed class TheHandyV3OutputTarget(int instanceIndex, IEventAggregator eventAggregator, IDeviceAxisValueProvider valueProvider)
    : AsyncAbstractOutputTarget(instanceIndex, eventAggregator, valueProvider)
{
    HttpClient httpClient;

    const string baseApiUrl = "https://www.handyfeeling.com/api/handy-rest/v3/";

    long clientServerTimeOffset = 100;
    int streamId = 100;
    bool isPlaying = false;
    bool hasInitedStart = false;
    int tailPointStreamIndex = 0;
    bool successfullyConnected = false;
    bool hasAdjustedDiscrepancyTime = false;
    bool shouldRestart = false;
    int millisecondsOffset = 900; // Send points 900 ms ahead of time because we need to maintain a buffer. This causes a latency of 900ms that must be compensated for in the L0 offset setting.
    int numberOfBatchedPoints => (int)Math.Ceiling(millisecondsOffset / 500.0);
    int[] previousPoints = { 0, 100, 2 };
    int[] previousCurrentPoints = { -1, -1, -1, -1, -1 };
    Queue<HspPoint> buffer = new Queue<HspPoint>();
    DateTime startTime = DateTime.UtcNow;
    DateTime previousPointTime = DateTime.UtcNow;
    DateTime lastBufferPushTime = DateTime.UtcNow;
    int previousTime = 0;

    double position = 0.5;
    double currentPosition = 0.5;
    double speed = 1;
    System.Timers.Timer outputTimer;

    public string ConnectionKey { get; set; } = null;
    public DeviceAxis SourceAxis { get; set; } = null;

    public override ConnectionStatus Status { get; protected set; }
    public bool IsConnected => Status == ConnectionStatus.Connected;
    public bool IsDisconnected => Status == ConnectionStatus.Disconnected;
    public bool IsConnectBusy => Status is ConnectionStatus.Connecting or ConnectionStatus.Disconnecting;
    public bool CanToggleConnect => !IsConnectBusy && SourceAxis != null;

    string apiAuthToken = "Y3aHfWBRFhB~x_26oUVZaO1HL_eEW3IV";    // You should probably change this to a key you control, or make it configurable. You can set up their own key on https://user.handyfeeling.com/

    protected override IUpdateContext RegisterUpdateContext(DeviceAxisUpdateType updateType) => updateType switch
    {
        DeviceAxisUpdateType.PolledUpdate => new AsyncPolledUpdateContext(),
        _ => null,
    };

    public void OnSourceAxisChanged()
    {
        if (Status != ConnectionStatus.Connected || SourceAxis == null)
            return;

        EventAggregator.Publish(new SyncRequestMessage(SourceAxis));
    }

    protected override ValueTask<bool> OnConnectingAsync(ConnectionType connectionType)
    {
        if (connectionType != ConnectionType.AutoConnect)
            Logger.Info("Connecting to {0} at \"{1}\" [Type: {2}]", Identifier, ConnectionKey, connectionType);

        if (string.IsNullOrWhiteSpace(ConnectionKey))
            throw new OutputTargetException("Invalid connection key");
        if (SourceAxis == null)
            throw new OutputTargetException("Source axis not selected");

        return ValueTask.FromResult(true);
    }



    protected override async Task RunAsync(ConnectionType connectionType, CancellationToken token)
    {
        httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(6);

        //using var client = NetUtils.CreateHttpClient();

        try
        {
            await Start();

            outputTimer = new System.Timers.Timer();
            outputTimer.Elapsed += OutputTimer_Tick;
            outputTimer.Interval = 100;
            outputTimer.Start();

        }
        catch (Exception e) when (connectionType != ConnectionType.AutoConnect)
        {
            Logger.Error(e, "Error when connecting to {0}", Name);
            _ = DialogHelper.ShowErrorAsync(e, $"Error when connecting to {Name}", "RootDialog");
            return;
        }
        catch
        {
            return;
        }

        try
        {
            Status = ConnectionStatus.Connected;
            EventAggregator.Publish(new SyncRequestMessage());

            await PolledUpdateAsync(SourceAxis, () => !token.IsCancellationRequested, async (_, snapshot, elapsed) =>
            {
                Logger.Debug("Begin PolledUpdate [Index From: {0}, Index To: {1}, Duration: {2}, Elapsed: {3}]", snapshot.IndexFrom, snapshot.IndexTo, snapshot.Duration, elapsed);
                if (snapshot.KeyframeFrom == null || snapshot.KeyframeTo == null)
                    return;

                if (!AxisSettings[SourceAxis].Enabled)
                    return;

                // Interpolate toward target position. Sending the position to The Handy is periodically done in a separate thread (OutputTimer_Tick()).
                // Simply sending the output to The Handy right now doesn't work well, in my experience the streaming protocol needs a steady supply of points to function correctly even if the position hasn't changed.
                // There is probably a better way of doing this, but I don't know the software well enough to implement it. 
                if (snapshot.Duration > 0)
                    speed = (snapshot.KeyframeTo.Value - snapshot.KeyframeFrom.Value) / snapshot.Duration;
                if (Math.Abs(speed) < 0.6) // Need a minimum speed (because if speed is too low, Handy movement becomes jagged).
                    speed = (speed > 0) ? 0.6 : -0.6;
                position = snapshot.KeyframeTo.Value;  

                //Debug.WriteLine($"UPDATE {currentPosition} {speed} {position}");
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Logger.Error(e, $"{Identifier} failed with exception");
            _ = DialogHelper.ShowErrorAsync(e, $"{Identifier} failed with exception", "RootDialog");
        }

        outputTimer.Stop();
        outputTimer = null;
    }

    private void OutputTimer_Tick(object sender, EventArgs e)
    {
        if (currentPosition != position) // Interpolate toward target position.
        {
            var delta = speed * (outputTimer.Interval / 1000);
            if (speed > 0)
            {
                if (currentPosition + delta > position)
                    currentPosition = position;
                else
                    currentPosition += speed * (outputTimer.Interval / 1000);
            }
            else if (speed < 0)
            {
                if (currentPosition + delta < position)
                    currentPosition = position;
                else
                    currentPosition += speed * (outputTimer.Interval / 1000);
            }
        }
        InputPosition(currentPosition);
        //Debug.WriteLine($"tick {currentPosition} {speed} {position}");
    }

    private static Uri ApiUri(string path) => new($"https://www.handyfeeling.com/api/handy/v2/{path}");
    private async Task<JObject> ApiReadResponseAsync(HttpResponseMessage message, CancellationToken token)
    {
        var response = JObject.Parse(await message.Content.ReadAsStringAsync(token));

        Logger.Trace("{0} api response [Content: {1}]", Identifier, response.ToString(Formatting.None));
        if (response.TryGetObject(out var error, "error"))
            throw new OutputTargetException($"Api call failed: {error.ToString(Formatting.None)}");

        return response;
    }

    private async Task<JObject> ApiGetAsync(HttpClient client, string path, CancellationToken token)
    {
        var uri = ApiUri(path);
        Logger.Trace("{0} api get [URI: {1}]", Identifier, uri);

        var result = await client.GetAsync(uri, token);
        result.EnsureSuccessStatusCode();
        return await ApiReadResponseAsync(result, token);
    }

    private async Task<JObject> ApiPutAsync(HttpClient client, string path, string content, CancellationToken token)
    {
        var uri = ApiUri(path);
        Logger.Trace("{0} api put [URI: {1}, Content: {2}]", Identifier, uri, content);

        var result = await client.PutAsync(ApiUri(path), new StringContent(content, Encoding.UTF8, "application/json"), token);
        result.EnsureSuccessStatusCode();
        return await ApiReadResponseAsync(result, token);
    }

    public override void HandleSettings(JObject settings, SettingsAction action)
    {
        base.HandleSettings(settings, action);

        if (action == SettingsAction.Saving)
        {
            settings[nameof(SourceAxis)] = SourceAxis != null ? JToken.FromObject(SourceAxis) : null;

            settings[nameof(ConnectionKey)] = ProtectedStringUtils.Protect(ConnectionKey,
                e => Logger.Warn(e, "Failed to encrypt \"{0}\"", nameof(ConnectionKey)));
        }
        else if (action == SettingsAction.Loading)
        {
            if (settings.TryGetValue<DeviceAxis>(nameof(SourceAxis), out var sourceAxis))
                SourceAxis = sourceAxis;

            if (settings.TryGetValue<string>(nameof(ConnectionKey), out var encryptedConnectionKey))
                ConnectionKey = ProtectedStringUtils.Unprotect(encryptedConnectionKey,
                    e => Logger.Warn(e, "Failed to decrypt \"{0}\"", nameof(ConnectionKey)));
        }
    }

    public override void RegisterActions(IShortcutManager s)
    {
        base.RegisterActions(s);

        #region ConnectionKey
        s.RegisterAction<string>($"{Identifier}::ConnectionKey::Set", s => s.WithLabel("Connection key"), connectionKey => ConnectionKey = connectionKey);
        #endregion

        #region SourceAxis
        s.RegisterAction<DeviceAxis>($"{Identifier}::SourceAxis::Set", s => s.WithLabel("Source axis").WithItemsSource(DeviceAxis.All), axis => SourceAxis = axis);
        #endregion
    }

    public override void UnregisterActions(IShortcutManager s)
    {
        base.UnregisterActions(s);
        s.UnregisterAction($"{Identifier}::ConnectionKey::Set");
        s.UnregisterAction($"{Identifier}::SourceAxis::Set");
    }

    private async Task Start()
    {
        if (string.IsNullOrEmpty(ConnectionKey))
        {
            Logger.Error("Please enter a connection key before connecting.");
            return;
        }

        try
        {
            clientServerTimeOffset = await GetClientServerTimeOffset();

            successfullyConnected = false;
            buffer.Clear();
            //streamId += 1;
            tailPointStreamIndex = 0;
            for (int i = 0; i < 5; i++)
            {
                previousCurrentPoints[i] = -1;
            }

            try
            {

                await Stop();

                using (var request = new HttpRequestMessage(new HttpMethod("PUT"), baseApiUrl + "hsp/setup"))
                {
                    request.Headers.TryAddWithoutValidation("accept", "application/json");
                    request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                    //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                    var contentRaw = new
                    {
                        streamId = streamId++
                    };
                    request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(contentRaw));
                    request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");

                    var response = await httpClient.SendAsync(request);

                    if (response != null && response.IsSuccessStatusCode)
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        Logger.Debug("Stream setup command response: " + responseText);

                        // Todo check for error code

                        successfullyConnected = true;
                        //hasStopped = false;
                        //ErrorMessage = null;
                        //TriggerStatusChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                //ErrorMessage = "Failed to connect.";
                //TriggerStatusChanged();
            }
            finally
            {
                //criticalMessageLock.ReleaseMutex();
            }

            //TriggerStatusChanged();
        }
        catch (Exception e)
        {
            Logger.Error("Something went wrong when connecting: " + e.Message);
            //ErrorMessage = "Something went wrong when connecting: " + e.Message;
            //TriggerStatusChanged();
        }
    }

    // Receive points from input device
    private async void InputPosition(double position)
    {
        if (!successfullyConnected)//&& !hasStopped)
            return;

        var now = DateTime.UtcNow;

        var flush = false;
        if (shouldRestart)
        {
            shouldRestart = false;

            // Flush
            using (var request = new HttpRequestMessage(new HttpMethod("PUT"), baseApiUrl + "hsp/flush"))
            {
                request.Headers.TryAddWithoutValidation("accept", "application/json");
                request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                var contentRaw = new
                {
                };
                request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(contentRaw));
                request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");

                try
                {
                    //Debug.WriteLine("Putting points: " + points.Count + " time: " + startTime.ToLongTimeString());
                    var response = await httpClient.SendAsync(request);
                    Logger.Debug("Flushing: " + response?.StatusCode);

                    if (response != null && response.IsSuccessStatusCode)
                    {
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Flushing failed: " + ex.Message);
                }
            }
            // Rewind
            isPlaying = false;
        }

        if (!hasInitedStart)
        {
            lastBufferPushTime = now;
            startTime = now;
            hasInitedStart = true;
            flush = true;
        }

        // Record point in buffer
        buffer.Enqueue(new HspPoint() { Position = position, Time = now });

        if (buffer.Count >= numberOfBatchedPoints) //now >= lastBufferPushTime )//+ Processor.FilterTime)
        {
            try
            {
                // Push buffer to stream if enough time has passed
                lastBufferPushTime = now;
                var points = new List<object>();
                do
                {
                    var point = buffer.Dequeue();
                    var x = Math.Clamp((int)Math.Round(point.Position * 100), 0, 100);

                    // Add current point
                    points.Add(new
                    {
                        t = (int)(point.Time - startTime).TotalMilliseconds + millisecondsOffset,
                        x = x
                    });
                    tailPointStreamIndex++;

                    previousPoints[2] = previousPoints[1];
                    previousPoints[1] = previousPoints[0];
                    previousPoints[0] = x;
                    previousPointTime = point.Time;
                }
                while (buffer.Any());


                if (points.Count > 0)
                {
                    //var benchmarkTime = DateTime.UtcNow;
                    using (var request = new HttpRequestMessage(new HttpMethod("PUT"), baseApiUrl + "hsp/add"))
                    {
                        request.Headers.TryAddWithoutValidation("accept", "application/json");
                        request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                        //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                        var contentRaw = new
                        {
                            points = points.ToArray(),
                            flush = flush,
                            tailPointStreamIndex = tailPointStreamIndex,
                        };
                        request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(contentRaw));
                        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");

                        try
                        {
                            //Debug.WriteLine("Putting points: " + points.Count + " time: " + startTime.ToLongTimeString());
                            var response = await httpClient.SendAsync(request);

                            //Debug.WriteLine("API roundtrip latency: " + (DateTime.UtcNow - benchmarkTime).TotalMilliseconds);

                            if (response != null && response.IsSuccessStatusCode)
                            {
                                var responseText = await response.Content.ReadAsStringAsync();
                                var responseJson = System.Text.Json.JsonSerializer.Deserialize<PutPointsApiResponse>(responseText);
                                if (responseJson != null)
                                {
                                    if (responseJson.result != null)
                                    {

                                        var isStalled = true;
                                        for (int i = 0; i < 4; i++)
                                        {
                                            if (previousCurrentPoints[i] != previousCurrentPoints[i + 1] && previousCurrentPoints[i] > 0)
                                                isStalled = false;
                                            previousCurrentPoints[i] = previousCurrentPoints[i + 1];
                                        }
                                        if (previousCurrentPoints[4] != responseJson.result.current_point)
                                            isStalled = false;
                                        previousCurrentPoints[4] = responseJson.result.current_point;
                                        previousTime = responseJson.result.current_time;

                                        if (isStalled)
                                        {
                                            Logger.Warn("Stalled");
                                            //var update = ErrorMessage == null;
                                            //ErrorMessage = "Stalled! Try increasing latency\nand/or reconnecting.";
                                            //if (update)
                                            //    TriggerStatusChanged();
                                        }
                                        else
                                        {
                                            //var update = ErrorMessage != null;
                                            //ErrorMessage = null;
                                            //if (update)
                                            //TriggerStatusChanged();
                                        }

                                        if ((responseJson.result.max_points - responseJson.result.points) < 100)
                                        {
                                            // Flush and reset point buffer because it gets buggy when reaching max points
                                            shouldRestart = true;
                                        }
                                    }
                                    else if (responseJson.error != null)
                                    {
                                        Logger.Warn("Failed: " + responseJson.error.message);
                                        if (responseJson.error.code == 1001)
                                        {
                                            Logger.Error("The Handy has too old firmware, please update to FW 4 or newer.");
                                            await DisconnectAsync();
                                            return;
                                        }
                                        if (responseJson.error.name == "DeviceNotConnected")
                                        {
                                            Logger.Error("The Handy with this connection code is not online.");
                                            await DisconnectAsync();
                                            return;
                                        }
                                    }
                                    Logger.Debug("Point put response: " + responseJson ?? "null");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex is not TaskCanceledException)
                            {
                                Logger.Warn("Failed: " + ex.Message);
                                //ErrorMessage = "Failed to send positions";
                                //TriggerStatusChanged();
                            }
                        }
                    }
                }


                if ((!isPlaying))// && (DateTime.UtcNow - startTime) > TimeSpan.FromSeconds(1)))
                {
                    isPlaying = true;
                    using (var request = new HttpRequestMessage(new HttpMethod("PUT"), baseApiUrl + "hsp/play"))
                    {
                        request.Headers.TryAddWithoutValidation("accept", "application/json");
                        request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                        //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                        var contentRaw = new
                        {
                            startTime = (long)(now - startTime).TotalMilliseconds, //-(int)(DateTime.UtcNow - benchmarkTime).TotalMilliseconds,
                            serverTime = ((DateTimeOffset)DateTimeOffset.UtcNow.ToUniversalTime()).ToUnixTimeMilliseconds() + clientServerTimeOffset,
                            playbackRate = 1.0,
                            loop = false,
                        };
                        request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(contentRaw));
                        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");

                        var response = await httpClient.SendAsync(request);

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            Logger.Debug("START PLAYING " + startTime.ToLongTimeString());
                            //if (!hasStopped)
                            {
                                //successfullyConnected = true;
                                Logger.Debug(await response.Content.ReadAsStringAsync());
                                //isPlaying = true;
                            }
                        }
                        else
                        {
                            isPlaying = false;
                            //ErrorMessage = "Failed to start playing...\nTry again.";
                            //TriggerStatusChanged();
                            Logger.Error("Failed to start playing: " + response?.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to send/process points to The Handy: " + ex.ToString());
            }

        }
    }

    private async Task Stop()
    {
        successfullyConnected = false;
        isPlaying = false;
        //hasStopped = true;
        hasInitedStart = false;
        hasAdjustedDiscrepancyTime = false;
        //httpClient.CancelPendingRequests();
        //ErrorMessage = null;
        //TriggerStatusChanged();

        try
        {
            // Repeat to make sure there is no race between incoming points
            for (int i = 0; i < 2; i++)
            {
                using (var request = new HttpRequestMessage(new HttpMethod("PUT"), baseApiUrl + "hsp/stop"))
                {
                    request.Headers.TryAddWithoutValidation("accept", "application/json");
                    request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                    //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                    await httpClient.SendAsync(request);

                }
                using (var request = new HttpRequestMessage(new HttpMethod("PUT"), baseApiUrl + "hsp/flush"))
                {
                    request.Headers.TryAddWithoutValidation("accept", "application/json");
                    request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                    //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                    await httpClient.SendAsync(request);

                }
                await Task.Delay(100);
            }
        }
        catch
        {
            return;
        }
    }

    // https://ohdoki.notion.site/Handy-API-v3-ea6c47749f854fbcabcc40c729ea6df4 chapter "Synchronized protocols"
    private async Task<long> GetClientServerTimeOffset()
    {
        const int numSamples = 30;
        int timeout = 10;
        var stopwatch = new Stopwatch();
        long offsetTimeSum = 0;

        for (int i = 0; i < numSamples; i++)
        {
            using (var request = new HttpRequestMessage(new HttpMethod("GET"), baseApiUrl + "servertime"))
            {
                request.Headers.TryAddWithoutValidation("accept", "application/json");
                request.Headers.TryAddWithoutValidation("X-Connection-Key", ConnectionKey);
                //request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiAuthToken);

                /*var contentRaw = new
                {
                    ck = ConnectionKey
                };
                request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(contentRaw));
                request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");*/

                stopwatch.Restart();
                var response = await httpClient.SendAsync(request);

                if (response == null)
                {
                    if (timeout-- <= 0)
                        throw new Exception("Failed to get servertime");
                    i--;
                    continue;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var responseJson = System.Text.Json.JsonSerializer.Deserialize<ServertimeApiResponse>(responseText);
                    if (responseJson != null && responseJson.server_time > 0)
                    {
                        stopwatch.Stop();
                        var clientTime = ((DateTimeOffset)DateTimeOffset.UtcNow.ToUniversalTime()).ToUnixTimeMilliseconds();
                        var rountripDelay = stopwatch.ElapsedMilliseconds;
                        Logger.Debug("Servertime response: " + responseText);
                        //if (response.IsSuccessStatusCode)
                        var serverTime = responseJson.server_time;
                        var estimatedServerReceiveTime = serverTime + rountripDelay / 2;
                        var timeOffset = estimatedServerReceiveTime - clientTime;
                        offsetTimeSum += timeOffset;
                    }
                    else
                    {
                        if (timeout-- <= 0)
                            throw new Exception("Failed to get servertime");
                        i--;
                        continue;
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new Exception("Unauthorized API key");
                }
            }
        }

        var offset = offsetTimeSum / numSamples;
        Logger.Debug("CLIENT-SERVER OFFSET: " + offset.ToString());
        return offset;
    }

    struct HspPoint
    {
        public double Position; // From 0 to 1
        public DateTime Time;
    }


    protected class PutPointsApiResponse
    {
        public PutPointsApiResponseResult? result { get; set; }
        public ErrorApiResponse? error { get; set; }

        public override string ToString()
        {
            return result?.ToString() ?? "Empty";
        }
    }

    protected class PutPointsApiResponseResult
    {
        public int points { get; set; }
        public int max_points { get; set; }
        public int current_point { get; set; }
        public int current_time { get; set; }
        public int first_point_time { get; set; }
        public int last_point_time { get; set; }
        public int tail_point_stream_index { get; set; }

        public override string ToString()
        {
            return $"points={points}, current_point={current_point}, current_time={current_time}, last_point_time={last_point_time}, tail_point_stream_index={tail_point_stream_index}";
        }
    }


    public class AuthenticationApiResponse
    {
        public string token_id { get; set; }
        public string token { get; set; }
        public string renew { get; set; }
    }


    public class ErrorApiResponse
    {
        public int code { get; set; }
        public string name { get; set; }
        public string message { get; set; }
        public bool connected { get; set; }
    }


    public class ServertimeApiResponse
    {
        public long server_time { get; set; }
    }
}
