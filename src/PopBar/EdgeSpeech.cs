using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Xml;

namespace PopBar;

internal static class EdgeSpeech
{
    const string Endpoint = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    const int ChunkLength = 500;
    const int MaxAudioBytes = 2 * 1024 * 1024;

    internal static string MaleVoice(string text)
    {
        bool hasHan = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            // Match kana letters. Shared punctuation such as the middle dot is not evidence of Japanese.
            if (value is >= 0x3041 and <= 0x3096 or >= 0x30a1 and <= 0x30fa or >= 0x31f0 and <= 0x31ff
                or >= 0xff66 and <= 0xff6f or >= 0xff71 and <= 0xff9d)
                return "ja-JP-KeitaNeural";
            hasHan |= value is >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff or >= 0xf900 and <= 0xfaff
                or >= 0x20000 and <= 0x2fa1f or >= 0x30000 and <= 0x323af;
        }
        // Han-only selections can be ambiguous. Default to Mandarin regardless of the UI language.
        return hasHan ? "zh-CN-YunxiNeural" : "en-US-GuyNeural";
    }

    internal static IEnumerable<string> Chunks(string text)
    {
        const string sentenceEnds = ".!?。！？\n";
        int start = 0;
        while (start < text.Length)
        {
            int length = Math.Min(ChunkLength, text.Length - start);
            if (start + length < text.Length)
            {
                var candidate = text.AsSpan(start, length);
                int boundary = candidate.LastIndexOfAny(sentenceEnds);
                if (boundary < length / 2) boundary = candidate.LastIndexOfAny(" \t\r");
                if (boundary >= length / 2) length = boundary + 1;
                else if (char.IsHighSurrogate(text[start + length - 1])) length--;
            }
            var chunk = text.Substring(start, length).Trim();
            start += length;
            if (chunk.Length > 0) yield return chunk;
        }
    }

    internal static async Task<byte[]> Synthesize(string text, string voice, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        using var first = CreateSocket();
        try
        {
            return await Synthesize(first, text, voice, DateTimeOffset.UtcNow, timeout.Token).ConfigureAwait(false);
        }
        catch (WebSocketException) when (first.HttpStatusCode == HttpStatusCode.Forbidden &&
            first.HttpResponseHeaders?.TryGetValue("Date", out var values) == true &&
            DateTimeOffset.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var serverTime))
        {
            // Correct clock skew using the speech service's own response.
            using var retry = CreateSocket();
            return await Synthesize(retry, text, voice, serverTime, timeout.Token).ConfigureAwait(false);
        }
    }

    static ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");
        socket.Options.Cookies = new CookieContainer();
        socket.Options.Cookies.Add(new Cookie("muid", Guid.NewGuid().ToString("N").ToUpperInvariant(), "/", "speech.platform.bing.com"));
        return socket;
    }

    static async Task<byte[]> Synthesize(ClientWebSocket socket, string text, string voice, DateTimeOffset time, CancellationToken token)
    {
        var uri = new Uri($"{Endpoint}?TrustedClientToken={EdgeSpeechToken.ClientToken}&ConnectionId={Guid.NewGuid():N}" +
            $"&Sec-MS-GEC={EdgeSpeechToken.Create(time)}&Sec-MS-GEC-Version=2-{EdgeSpeechToken.BrowserVersion}");
        await socket.ConnectAsync(uri, token).ConfigureAwait(false);

        string timestamp = time.UtcDateTime.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", CultureInfo.InvariantCulture);
        await Send(socket, $"X-Timestamp:{timestamp}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            """{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}""", token)
            .ConfigureAwait(false);
        await Send(socket, $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{timestamp}Z\r\nPath:ssml\r\n\r\n" +
            CreateSsml(text, voice), token).ConfigureAwait(false);

        using var audio = new MemoryStream();
        using var message = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (frame.MessageType == WebSocketMessageType.Close)
                throw new IOException("The speech service closed the connection before finishing.");
            if (message.Length + frame.Count > MaxAudioBytes)
                throw new InvalidDataException("The speech service returned an oversized message.");
            message.Write(buffer, 0, frame.Count);
            if (!frame.EndOfMessage) continue;

            var content = message.GetBuffer().AsMemory(0, (int)message.Length);
            if (frame.MessageType == WebSocketMessageType.Text)
            {
                var reply = Encoding.UTF8.GetString(content.Span);
                var headerEnd = reply.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd >= 0 && reply[..headerEnd].Split("\r\n").Contains("Path:turn.end"))
                    break;
            }
            else
            {
                AppendAudio(audio, content.Span);
            }
            message.SetLength(0);
        }
        if (audio.Length == 0) throw new InvalidDataException("The speech service returned no audio.");
        return audio.ToArray();
    }

    static ValueTask Send(ClientWebSocket socket, string message, CancellationToken token) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message).AsMemory(), WebSocketMessageType.Text, true, token);

    static void AppendAudio(MemoryStream audio, ReadOnlySpan<byte> message)
    {
        if (message.Length < 2) throw new InvalidDataException("Missing audio headers.");
        int offset = 2 + BinaryPrimitives.ReadUInt16BigEndian(message);
        if (offset > message.Length) throw new InvalidDataException("Invalid audio headers.");
        if (offset == message.Length) return;
        var headers = Encoding.ASCII.GetString(message[2..offset]).Split("\r\n");
        if (!headers.Contains("Path:audio") || !headers.Contains("Content-Type:audio/mpeg"))
            throw new InvalidDataException("Unexpected speech audio format.");
        if (audio.Length + message.Length - offset > MaxAudioBytes)
            throw new InvalidDataException("The speech service returned oversized audio.");
        audio.Write(message[offset..]);
    }

    static string CreateSsml(string text, string voice)
    {
        var result = new StringBuilder();
        using var xml = XmlWriter.Create(result, new XmlWriterSettings { OmitXmlDeclaration = true });
        xml.WriteStartElement("speak", "http://www.w3.org/2001/10/synthesis");
        xml.WriteAttributeString("version", "1.0");
        xml.WriteAttributeString("xml", "lang", null, voice[..5]);
        xml.WriteStartElement("voice");
        xml.WriteAttributeString("name", voice);
        xml.WriteStartElement("prosody");
        xml.WriteAttributeString("rate", "+0%");
        xml.WriteAttributeString("pitch", "+0Hz");
        xml.WriteAttributeString("volume", "+0%");
        var spoken = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            spoken.Append(rune.IsBmp && !XmlConvert.IsXmlChar((char)rune.Value) ? " " : rune.ToString());
        xml.WriteString(spoken.ToString());
        xml.WriteEndDocument();
        xml.Flush();
        return result.ToString();
    }
}
