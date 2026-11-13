using NAudio.Wave;

namespace PopBar;

internal sealed class ReadAloud(Action<string> notify)
{
    CancellationTokenSource? current;
    internal void Stop() => current?.Cancel();

    internal async Task Start(string text)
    {
        Stop();
        using var operation = new CancellationTokenSource();
        current = operation;
        var token = operation.Token;
        var voice = EdgeSpeech.MaleVoice(text);
        try
        {
            using var chunks = EdgeSpeech.Chunks(text).GetEnumerator();
            if (!chunks.MoveNext()) return;
            var synthesis = EdgeSpeech.Synthesize(chunks.Current, voice, token);
            while (true)
            {
                var audio = await synthesis;
                token.ThrowIfCancellationRequested();
                var playback = Play(audio, token);
                if (!chunks.MoveNext())
                {
                    await playback;
                    break;
                }

                // Generate one chunk ahead while the current chunk is playing.
                synthesis = EdgeSpeech.Synthesize(chunks.Current, voice, token);
                await Task.WhenAll(playback, synthesis);
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            notify(T("ReadAloudFailed"));
        }
        finally
        {
            if (current == operation) current = null;
        }
    }

    internal static async Task Play(byte[] audio, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(audio, writable: false);
        using var reader = new Mp3FileReaderBase(stream, format => new AcmMp3FrameDecompressor(format));
        using var output = new WaveOutEvent { DesiredLatency = 120 };
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception == null) finished.TrySetResult();
            else finished.TrySetException(e.Exception);
        };
        output.Init(reader);
        output.Play();
        using var stop = token.Register(output.Stop);
        await finished.Task;
        token.ThrowIfCancellationRequested();
    }
}
