using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PoliPage.Tests.Contract;

/// <summary>
/// One answer of the mock: an HTTP answer, or a fault. The contract's faults are
/// <c>drop</c> (close the connection without an answer) and <c>hang</c> (never answer). The
/// unit tests add two that send this step's answer badly: <c>cut</c> (the head and half
/// the body, then close) and <c>drip</c> (the head, then the body one byte every 50 ms).
/// </summary>
internal sealed record MockStep(
    int Status,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body,
    string? Fault)
{
    public static MockStep Drop { get; } = new(0, [], [], "drop");

    public static MockStep Hang { get; } = new(0, [], [], "hang");

    public MockStep Cut() => this with { Fault = "cut" };

    public MockStep Drip() => this with { Fault = "drip" };
}

/// <summary>A request the mock received.</summary>
internal sealed record RecordedRequest(
    int Connection,
    string Method,
    string Target,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    byte[] Body,
    long ArrivedAt)
{
    public IReadOnlyList<string>? Header(string name)
        => Headers.TryGetValue(name, out var values) ? values : null;
}

/// <summary>
/// A minimal HTTP/1.1 server on a real loopback socket (<see cref="TcpListener"/>). The
/// <c>n</c>-th request it receives gets the <c>n</c>-th step: an answer, a <c>drop</c>
/// (the connection is closed without an answer) or a <c>hang</c> (no answer until the
/// client gives up). Connections are kept alive between answers, as a real API does, so a
/// client that reuses one is exercised. Each request is recorded with its arrival time on
/// the monotonic clock, once it is fully read.
/// </summary>
internal sealed class MockServer : IAsyncDisposable
{
    private const string ExhaustedBody = "{\"error\":\"MOCK_EXHAUSTED\"}";

    private readonly TcpListener _listener;
    private readonly Func<string, IReadOnlyList<MockStep>> _steps;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<string> _problems = [];
    private readonly List<TcpClient> _clients = [];
    private readonly object _gate = new();
    private readonly Task _acceptLoop;
    private IReadOnlyList<MockStep>? _resolvedSteps;
    private int _connections;

    /// <param name="steps">
    /// The answers in order, built from the server's origin (for the <c>{{mock}}</c>
    /// placeholder) once it is listening.
    /// </param>
    public MockServer(Func<string, IReadOnlyList<MockStep>> steps)
    {
        _steps = steps;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Origin = string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _acceptLoop = AcceptLoopAsync();
    }

    public string Origin { get; }

    public int Connections
    {
        get
        {
            lock (_gate)
                return _connections;
        }
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_gate)
                return [.. _problems];
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        lock (_gate)
        {
            foreach (var client in _clients)
                client.Dispose();
        }

        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }

        _stop.Dispose();
    }

    private IReadOnlyList<MockStep> Steps
    {
        get
        {
            lock (_gate)
                return _resolvedSteps ??= _steps(Origin);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            int connection;
            lock (_gate)
            {
                _clients.Add(client);
                connection = ++_connections;
            }

            _ = ServeConnectionAsync(client, connection);
        }
    }

    private async Task ServeConnectionAsync(TcpClient client, int connection)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var reader = new RequestReader(stream);
                while (!_stop.IsCancellationRequested)
                {
                    var request = await reader.ReadAsync(connection, _stop.Token);
                    if (request is null)
                        return; // the client closed the connection

                    if (!await AnswerAsync(stream, request))
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client went away: nothing to answer.
        }
        catch (InvalidDataException ex)
        {
            Problem($"connection {connection}: {ex.Message}");
        }
    }

    /// <summary>Answers one request; <see langword="false"/> when the connection must close.</summary>
    private async Task<bool> AnswerAsync(NetworkStream stream, RecordedRequest request)
    {
        int index;
        lock (_gate)
        {
            _requests.Add(request);
            index = _requests.Count - 1;
        }

        var steps = Steps;
        if (index >= steps.Count)
        {
            // More requests than the case has answers: the request list shows it.
            await WriteAnswerAsync(
                stream,
                new MockStep(599, [new("Content-Type", "application/json")], Encoding.UTF8.GetBytes(ExhaustedBody), null));
            return true;
        }

        var step = steps[index];
        switch (step.Fault)
        {
            case "drop":
                // Close without an answer: the client sees the connection end.
                return false;
            case "hang":
                // Never answer: wait for the client to give up (it closes the connection) or
                // for the server to stop.
                await WaitForCloseAsync(stream);
                return false;
            case "cut":
                await WriteHeadAsync(stream, step);
                await stream.WriteAsync(step.Body.AsMemory(0, step.Body.Length / 2), _stop.Token);
                await stream.FlushAsync(_stop.Token);
                return false;
            case "drip":
                await WriteHeadAsync(stream, step);
                foreach (var b in step.Body)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), _stop.Token);
                    await stream.WriteAsync(new[] { b }, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }

                return !WantsClose(request);
            default:
                await WriteAnswerAsync(stream, step);
                return !WantsClose(request);
        }
    }

    private async Task WaitForCloseAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        while (await stream.ReadAsync(buffer, _stop.Token) > 0)
        {
            // Discard anything the client sends: it gets no answer.
        }
    }

    private async Task WriteAnswerAsync(NetworkStream stream, MockStep step)
    {
        await WriteHeadAsync(stream, step);
        if (step.Status is not (204 or 304))
            await stream.WriteAsync(step.Body, _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }

    private async Task WriteHeadAsync(NetworkStream stream, MockStep step)
    {
        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {step.Status} {ReasonPhrase(step.Status)}\r\n");
        foreach (var (name, value) in step.Headers)
            head.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
        if (step.Status is not (204 or 304))
            head.Append(CultureInfo.InvariantCulture, $"Content-Length: {step.Body.Length}\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }

    private static bool WantsClose(RecordedRequest request)
        => request.Header("Connection") is { } values
            && values.Any(v => v.Contains("close", StringComparison.OrdinalIgnoreCase));

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        204 => "No Content",
        _ => "Mock",
    };

    private void Problem(string message)
    {
        lock (_gate)
            _problems.Add(message);
    }

    /// <summary>Reads HTTP/1.1 requests (Content-Length or chunked bodies) off one connection.</summary>
    private sealed class RequestReader(NetworkStream stream)
    {
        private const int MaxHeadBytes = 64 * 1024;
        private readonly byte[] _buffer = new byte[16 * 1024];
        private readonly List<byte> _pending = [];

        public async Task<RecordedRequest?> ReadAsync(int connection, CancellationToken cancellationToken)
        {
            var head = await ReadHeadAsync(cancellationToken);
            if (head is null)
                return null;

            var lines = head.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3)
                throw new InvalidDataException($"bad request line: {lines[0]}");

            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                    throw new InvalidDataException($"bad header line: {line}");
                var name = line[..colon].Trim();
                if (!headers.TryGetValue(name, out var values))
                    headers[name] = values = [];
                values.Add(line[(colon + 1)..].Trim());
            }

            var body = await ReadBodyAsync(headers, cancellationToken);
            return new RecordedRequest(
                connection,
                requestLine[0],
                requestLine[1],
                headers.ToDictionary(h => h.Key, h => (IReadOnlyList<string>)h.Value, StringComparer.OrdinalIgnoreCase),
                body,
                Stopwatch.GetTimestamp());
        }

        private async Task<string?> ReadHeadAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var end = IndexOfHeadEnd();
                if (end >= 0)
                {
                    var head = Encoding.ASCII.GetString([.. _pending.Take(end)]);
                    _pending.RemoveRange(0, end + 4);
                    return head;
                }

                if (_pending.Count > MaxHeadBytes)
                    throw new InvalidDataException("request head too large");
                if (!await FillAsync(cancellationToken))
                {
                    if (_pending.Count == 0)
                        return null;
                    throw new InvalidDataException("connection closed mid-request");
                }
            }
        }

        private async Task<byte[]> ReadBodyAsync(Dictionary<string, List<string>> headers, CancellationToken cancellationToken)
        {
            if (headers.TryGetValue("Transfer-Encoding", out var te)
                && te.Exists(v => v.Contains("chunked", StringComparison.OrdinalIgnoreCase)))
            {
                return await ReadChunkedAsync(cancellationToken);
            }

            var length = headers.TryGetValue("Content-Length", out var cl)
                ? int.Parse(cl[0], CultureInfo.InvariantCulture)
                : 0;
            return await ReadExactlyAsync(length, cancellationToken);
        }

        private async Task<byte[]> ReadChunkedAsync(CancellationToken cancellationToken)
        {
            var body = new List<byte>();
            while (true)
            {
                var sizeLine = await ReadLineAsync(cancellationToken);
                var size = int.Parse(sizeLine.Split(';')[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    // Trailers, then the empty line.
                    while ((await ReadLineAsync(cancellationToken)).Length > 0)
                    {
                    }

                    return [.. body];
                }

                body.AddRange(await ReadExactlyAsync(size, cancellationToken));
                await ReadLineAsync(cancellationToken);
            }
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                for (var i = 0; i + 1 < _pending.Count; i++)
                {
                    if (_pending[i] == '\r' && _pending[i + 1] == '\n')
                    {
                        var line = Encoding.ASCII.GetString([.. _pending.Take(i)]);
                        _pending.RemoveRange(0, i + 2);
                        return line;
                    }
                }

                if (!await FillAsync(cancellationToken))
                    throw new InvalidDataException("connection closed mid-body");
            }
        }

        private async Task<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
        {
            while (_pending.Count < length)
            {
                if (!await FillAsync(cancellationToken))
                    throw new InvalidDataException("connection closed mid-body");
            }

            var bytes = _pending.Take(length).ToArray();
            _pending.RemoveRange(0, length);
            return bytes;
        }

        private async Task<bool> FillAsync(CancellationToken cancellationToken)
        {
            var read = await stream.ReadAsync(_buffer, cancellationToken);
            if (read == 0)
                return false;
            _pending.AddRange(_buffer.AsSpan(0, read).ToArray());
            return true;
        }

        private int IndexOfHeadEnd()
        {
            for (var i = 0; i + 3 < _pending.Count; i++)
            {
                if (_pending[i] == '\r' && _pending[i + 1] == '\n' && _pending[i + 2] == '\r' && _pending[i + 3] == '\n')
                    return i;
            }

            return -1;
        }
    }
}
