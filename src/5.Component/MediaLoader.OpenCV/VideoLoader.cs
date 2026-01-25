using ComponentCommon;
using OpenCvSharp;
using Perceptron.Domain.Abstraction.FrameBuffer;
using Perceptron.Domain.Abstraction.MediaLoader;
using Perceptron.Domain.DataStructure;
using Perceptron.Domain.Entity.VideoStream;
using Perceptron.Domain.Setting;
using Serilog;
using System.Diagnostics;

namespace MediaLoader.OpenCV;

public class VideoLoader : ComponentBase, IVideoLoader
{
    public string SourceId { get; private set; }
    public string VideoUri { get; private set; }
    public VideoSpecs Specs { get; private set; }
    public VideoLoaderState State { get; private set; } = VideoLoaderState.Idle;
    public VideoLoaderOptions Options { get; private set; }
    public int VideoStride { get; private set; }
    public int MaxRetries { get; private set; }
    public int RetryDelayMs { get; private set; }
    public bool Loop { get; private set; }

    private VideoCapturePara _param;
    private bool _isLocalVideoFile;

    private VideoCapture _capture = new();
    private IVideoFrameBuffer _buffer;
    private readonly MatPool _matPool = new();
    private MatType _lastMatType = MatType.CV_8UC3;

    private long _frameIndex = 1;

    private CancellationTokenSource _cancellationTokenSource = new();

    private int _retryCount;

    private Action<Frame>? _onFrameRetrieved;
    
    private readonly object _frameCallbackLock = new();
    private readonly object _captureLock = new();
    
    public VideoLoader(Dictionary<string, string>? preferences)
        : base(preferences)
    {
        Log.Information("Initialize OpenCV video capture...");

        LoadPreferences(preferences);

        _param = new VideoCapturePara(Options.AccelerationType, Options.VideoAccelerationDeviceId);
    }

    protected sealed override void LoadPreferences(Dictionary<string, string>? preferences)
    {
        SourceId = VideoLoaderSettings.ParseSourceId(preferences);

        Options = new VideoLoaderOptions()
        {
            VideoCaptureApi = VideoLoaderSettings.ParseVideoCaptureApi(preferences),
            AccelerationType = VideoLoaderSettings.ParseVideoAccelerationType(preferences),
            VideoAccelerationDeviceId = VideoLoaderSettings.ParseVideoAccelerationDeviceId(preferences)
        };

        VideoStride = VideoLoaderSettings.ParseVideoStride(preferences);
        MaxRetries = VideoLoaderSettings.ParseMaxRetries(preferences);
        RetryDelayMs = VideoLoaderSettings.ParseRetryDelayMs(preferences);
        Loop = VideoLoaderSettings.ParseLoop(preferences);
    }

    public void AttachBuffer(IVideoFrameBuffer buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        _buffer = buffer;
    }
    
    public bool Open(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            Log.Error("Video URI is empty or null.");
            return false;
        }

        VideoUri = uri;

        Log.Information($"Open video source ...");
        Log.Information("Video id: {SourceId}, uri: {VideoUri}", VideoUri, VideoUri);

        _isLocalVideoFile = IsLocalVideoFile(VideoUri);

        Close();

        return CreateVideoCapture(uri);
    }

    private static bool IsLocalVideoFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            return uri.IsFile;
        }

        if (Path.IsPathRooted(path)) return true;

        try
        {
            if (File.Exists(path)) return true;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private bool CreateVideoCapture(string uri)
    {
        lock (_captureLock)
        {
            // TODO：it seems that OpenCVSharp 4.11.0 has a bug that can not open video stream with VideoCaptureAPIs and VideoCapturePara.
            //       So we use the default constructor for now.
            //       If you want to use VideoCaptureAPIs and VideoCapturePara, please uncomment the following line.
            //_capture = new VideoCapture(uri, _captureApis, _capturePara);
            //_capture = new VideoCapture(uri, _captureApis);
            _capture = new VideoCapture(uri);
            if (!_capture.IsOpened())
            {
                Log.Error($"Video source '{uri}' can not be opened.");
                _capture.Dispose();

                State = VideoLoaderState.Error;

                return false;
            }

            VideoUri = uri;
            Specs = new VideoSpecs(_capture.FrameWidth, _capture.FrameHeight, _capture.Fps, _capture.FrameCount);
            _frameIndex = 1;

            ShowCaptureConfiguration();

            State = VideoLoaderState.Opened;

            return true;
        }
    }

    private void ShowCaptureConfiguration()
    {
        double backendValue = _capture.Get(VideoCaptureProperties.Backend);
        var backend = (VideoCaptureAPIs)backendValue;

        double accelTypeValue = _capture.Get(VideoCaptureProperties.HwAcceleration);
        var videoAccelerationType = (VideoAccelerationType)accelTypeValue;

        double deviceIdValue = _capture.Get(VideoCaptureProperties.HwDevice);
        var deviceId = (int)deviceIdValue;

        Log.Information($"OpenCV video capture using capture api: {backend.ToString()}, " +
                        $"acceleration type: {videoAccelerationType.ToString()}, " +
                        $"device id: {deviceId}.");
    }

    public void Close()
    {
        Stop();

        lock (_captureLock)
        {
            if (!_capture.IsDisposed)
            {
                if (_capture.IsOpened())
                {
                    _capture.Release();
                }

                _capture.Dispose();
            }
        }

        State = VideoLoaderState.Closed;
    }

    public void Play(bool debugMode = false, int debugFrameCount = 0)
    {
        if (!_capture.IsOpened())
        {
            Log.Error($"Stream source '{VideoUri}' not opened yet.");
            return;
        }

        if (State == VideoLoaderState.Running)
            return;

        State = VideoLoaderState.Running;

        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        var stopwatch = Stopwatch.StartNew();
        while ((State == VideoLoaderState.Running || State == VideoLoaderState.Paused) && !token.IsCancellationRequested)
        {
            if (State == VideoLoaderState.Paused)
            {
                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                }

                Thread.Sleep(10);
                continue;
            }

            if (!stopwatch.IsRunning)
            {
                stopwatch.Start();
            }

            #region retrive specified amount of frame for debug
            if (debugMode && debugFrameCount-- <= 0)
            {
                break;
            }
            #endregion

            Frame? frame = null;
            int sleepMilliSec = 0;

            lock (_captureLock)
            {
                if (!_capture.Grab())
                {
                    // End of video file.
                    if (_capture.FrameCount > 0 && _frameIndex > _capture.FrameCount)
                    {
                        if (Loop)
                        {
                            // Reset to the beginning
                            if (_capture.Set(VideoCaptureProperties.PosFrames, 0))
                            {
                                _frameIndex = 1;
                                // Compensate for the loop iteration in debug mode
                                if (debugMode) debugFrameCount++;
                                continue;
                            }
                        }

                        Stop();
                        break;
                    }

                    // Reconnect video streaming.
                    if (_retryCount++ < MaxRetries)
                    {
                        Log.Warning("Video source grab failed. Attempting to reconnect {RetryCount}/{MaxRetries}.", _retryCount, MaxRetries);

                        // fast retry when error first occur.
                        if (_retryCount > 1)
                        {
                            Thread.Sleep(RetryDelayMs);
                        }

                        if (CreateVideoCapture(VideoUri))
                        {
                            continue;   // re-grab
                        }
                        else
                        {
                            Log.Error("Create video capture failed.");
                            Stop();
                            break;
                        }
                    }
                    else
                    {
                        Log.Error("Maximum reconnection attempts reached. Stopping video streaming.");
                        Stop();
                        break;
                    }
                }

                _retryCount = 0;

                // Stride
                if (_frameIndex++ % VideoStride != 0)
                {
                    continue;
                }

                var image = _matPool.Rent(Specs.Width, Specs.Height, _lastMatType);
                if (!_capture.Retrieve(image))
                {
                    Log.Warning("Retrieve image failed. Skip this frame.");
                    _matPool.Return(image);
                    continue;
                }

                if (image.Empty())
                {
                    Log.Warning("Image invalid. Skip this frame.");
                    _matPool.Return(image);
                    continue;
                }

                if (image.Type() != _lastMatType)
                {
                    _lastMatType = image.Type();
                }

                // Frame attributes
                var frameId = (long)_capture.Get(VideoCaptureProperties.PosFrames);
                var offsetMilliSec = (long)_capture.Get(VideoCaptureProperties.PosMsec);

                long elapsedTimeMs = stopwatch.ElapsedMilliseconds;
                sleepMilliSec = (int)Math.Min(100, offsetMilliSec - elapsedTimeMs);

                Log.Debug($"FM:{offsetMilliSec} EM:{elapsedTimeMs} Diff:{offsetMilliSec - elapsedTimeMs}");

                frame = new Frame(SourceId, frameId, offsetMilliSec, image, (m) => _matPool.Return(m));
            }

            if (_buffer != null)
            {
                _buffer.Enqueue(frame);
            }

            // thread saft event handler
            Action<Frame>? frameCallback;
            lock (_frameCallbackLock)
            {
                frameCallback = _onFrameRetrieved;
            }
            frameCallback?.Invoke(frame);

            // Sleep to match the frame rate only for local video file.
            if (sleepMilliSec > 0 && _isLocalVideoFile)
            {
                Thread.Sleep(sleepMilliSec);
            }
        }

        Close();
    }

    public void Pause()
    {
        if (State == VideoLoaderState.Running)
        {
            State = VideoLoaderState.Paused;
        }
    }

    public void Resume()
    {
        if (State == VideoLoaderState.Paused)
        {
            State = VideoLoaderState.Running;
        }
    }

    public void Stop()
    {
        if (State != VideoLoaderState.Running)
            return;

        _cancellationTokenSource.Cancel();
        State = VideoLoaderState.Stopped;
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        return Task.Factory.StartNew(() => Play(), 
            TaskCreationOptions.LongRunning);
    }

    public Task StopAsync()
    {
        return Task.Factory.StartNew(() => Stop(), 
            TaskCreationOptions.PreferFairness);
    }
    
    public bool Seek(long frameId)
    {
        if (!_isLocalVideoFile)
        {
            Log.Warning("Seek operation is only supported for local video files.");
            return false;
        }

        lock (_captureLock)
        {
            if (!_capture.IsOpened())
                return false;

            if (_capture.FrameCount > 0 && (frameId < 0 || frameId >= _capture.FrameCount))
            {
                return false;
            }

            if (_capture.Set(VideoCaptureProperties.PosFrames, frameId))
            {
                _frameIndex = frameId + 1;
                return true;
            }
            
            return false;
        }
    }

    public bool Seek(TimeSpan timestamp)
    {
        if (!_isLocalVideoFile)
        {
            Log.Warning("Seek operation is only supported for local video files.");
            return false;
        }

        if (timestamp.TotalMilliseconds < 0)
        {
            Log.Warning("Seek timestamp cannot be negative.");
            return false;
        }

        lock (_captureLock)
        {
            if (!_capture.IsOpened())
                return false;

            if (_capture.Set(VideoCaptureProperties.PosMsec, timestamp.TotalMilliseconds))
            {
                var currentFrame = (long)_capture.Get(VideoCaptureProperties.PosFrames);
                _frameIndex = currentFrame + 1;
                return true;
            }

            return false;
        }
    }

    public void SetFrameCallback(Action<Frame>? frameHandler)
    {
        if (frameHandler == null)
            return;

        lock (_frameCallbackLock)
        {
            _onFrameRetrieved += frameHandler;
        }
    }

    public void UnsetFrameCallback(Action<Frame>? frameHandler)
    {
        if (frameHandler == null)
            return;

        lock (_frameCallbackLock)
        {
            _onFrameRetrieved -= frameHandler;
        }
    }
    
    public void Dispose()
    {
        Close();
        _matPool.Dispose();
        _cancellationTokenSource.Dispose();
    }

    
}