using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;
using VinylRipper.Discogs;
using VinylRipper.Preview;

namespace VinylRipper.Windows.ViewModels;

public enum PreviewState { Idle, Loading, Playing }

/// <summary>
/// Reproduce una pista a la vez para escucharla antes de descargarla, y expone el estado para la fila
/// (▶ / spinner / ■) y la tira de reproducción con su barra de progreso.
/// <para>
/// Usa NAudio en vez del <c>MediaPlayer</c> de WPF: ése depende del Windows Media Player antiguo, que
/// Windows 11 ya no siempre instala ("Se requiere Windows Media Player versión 10 o posterior").
/// <see cref="MediaFoundationReader"/> decodifica el m4a con Media Foundation y <see cref="WasapiPlayer"/>
/// lo manda a la salida de audio por defecto.
/// </para>
/// Debe crearse y usarse en el hilo de la interfaz.
/// </summary>
public sealed partial class PreviewPlayer : ObservableObject
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MediaFoundationReader? _reader;
    private WasapiPlayer? _output;

    public PreviewPlayer()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>La reproducción ha fallado (título, motivo).</summary>
    public event Action<string?, string>? PlaybackFailed;

    [ObservableProperty] private PreviewState _state = PreviewState.Idle;
    /// <summary><see cref="TrackSelection.Key"/> de la pista actual, o null.</summary>
    [ObservableProperty] private string? _currentKey;
    [ObservableProperty] private string? _currentTitle;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;

    public bool IsActive => State != PreviewState.Idle;
    public bool IsLoading => State == PreviewState.Loading;
    public bool IsPlaying => State == PreviewState.Playing;

    /// <summary>0..100 para la barra.</summary>
    public double Percent => Duration > TimeSpan.Zero ? Math.Clamp(100.0 * Position.Ticks / Duration.Ticks, 0, 100) : 0;
    public string PositionText => PreviewTime.Format(Position);
    public string DurationText => Duration > TimeSpan.Zero ? PreviewTime.Format(Duration) : "–:––";

    /// <summary>Texto de la tira: "Preparando Artista – Pista…" o "Artista – Pista".</summary>
    public string? StatusLine => State == PreviewState.Loading ? $"Preparando {CurrentTitle}…" : CurrentTitle;

    partial void OnStateChanged(PreviewState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(StatusLine));
    }

    partial void OnCurrentTitleChanged(string? value) => OnPropertyChanged(nameof(StatusLine));

    partial void OnPositionChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PositionText));
    }

    partial void OnDurationChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(DurationText));
    }

    public bool IsCurrent(TrackSelection track) => CurrentKey == track.Key;

    /// <summary>Para lo que suene y marca <paramref name="track"/> como "preparándose" (spinner).</summary>
    public void BeginLoading(TrackSelection track)
    {
        Stop();
        var artist = track.Track.Artist ?? track.Release.Artist;
        CurrentKey = track.Key;
        CurrentTitle = string.IsNullOrWhiteSpace(artist) ? track.Track.Title : $"{artist} – {track.Track.Title}";
        State = PreviewState.Loading;
    }

    /// <summary>Empieza a sonar el archivo ya preparado de la pista actual.</summary>
    public void Play(string path)
    {
        var title = CurrentTitle;
        try
        {
            _reader = new MediaFoundationReader(path);
            _output = new WasapiPlayerBuilder().WithSharedMode().WithLatency(200).Build();
            _output.PlaybackStopped += Output_PlaybackStopped;
            _output.Init(_reader);
            _output.Play();
        }
        catch (Exception ex)
        {
            Stop();
            PlaybackFailed?.Invoke(title, ex.Message);
            return;
        }

        Duration = _reader.TotalTime;
        Position = TimeSpan.Zero;
        State = PreviewState.Playing;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        ReleaseAudio();
        State = PreviewState.Idle;
        CurrentKey = null;
        CurrentTitle = null;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
    }

    /// <summary>Salta a una fracción (0..1) de la canción.</summary>
    public void Seek(double fraction)
    {
        if (State != PreviewState.Playing || _reader is null || Duration <= TimeSpan.Zero) return;
        _reader.CurrentTime = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        Refresh();
    }

    private void Refresh()
    {
        if (State == PreviewState.Playing && _reader is not null) Position = _reader.CurrentTime;
    }

    /// <summary>Fin de la canción o error del dispositivo (p. ej. se desconectan los auriculares).</summary>
    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Puede llegar desde el hilo de audio: lo pasamos al de la interfaz.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Output_PlaybackStopped(sender, e));
            return;
        }
        if (!ReferenceEquals(sender, _output)) return; // una reproducción anterior que ya paramos

        var title = CurrentTitle;
        Stop();
        if (e.Exception is not null) PlaybackFailed?.Invoke(title, e.Exception.Message);
    }

    /// <summary>Suelta el dispositivo de audio y el archivo.</summary>
    private void ReleaseAudio()
    {
        var output = _output;
        var reader = _reader;
        _output = null;
        _reader = null;
        if (output is not null)
        {
            output.PlaybackStopped -= Output_PlaybackStopped;
            try { output.Stop(); } catch { /* ya parado */ }
            output.Dispose();
        }
        reader?.Dispose();
    }
}
