using Ciribob.DCS.SimpleRadio.Standalone.Common.Network.Client.Commands;
using NLog;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ciribob.DCS.SimpleRadio.Standalone.Lua
{
    internal class CommandService : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public static readonly Encoding Encoding = Encoding.UTF8;
        readonly Lock _sendLock = new();
        readonly Task _worker;
        readonly CancellationToken _token;
        readonly string _name;
        NamedPipeServerStream _pipe;

        public CommandService(string name, CancellationToken token)
        {
            _token = token;
            _name = name;
            _worker = Task.Run(Process, token);
        }

        async void Process()
        {
            // Outer loop, to guarantee that our service can self restart.
            var token = _token;
            var pipeName = $"dcssimpleradiostandalone\\{_name}";
            while (true)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous); 
                    lock (_sendLock)
                    {
                        _pipe = pipe;
                    }

                    await _pipe.WaitForConnectionAsync(token);

                    // Inner processing loop.
                    // If we find cancellation here, simply out and let the outer loop handle it.
                    await PumpMessages(token);
                }
                catch (OperationCanceledException e)
                {
                    // Will be thrown if it comes from one of the Async.
                    // Check if it's our token, in which case rethrow.
                    // Otherwise just eat up.
                    if (e.CancellationToken == token)
                    {
                        throw;
                    }

                    // Other cancellation, rebuild the pipe.
                }
                catch
                {
                    // Eat errors, recreate the pipe.
                }
                finally
                {
                    lock (_sendLock)
                    {
                        _pipe = null;
                    }
                }
            }
        }

        async Task PumpMessages(CancellationToken token)
        {
            while (_pipe.IsConnected && !token.IsCancellationRequested)
            {
                var message = await PumpMessage(token);
                if (!string.IsNullOrEmpty(message))
                {
                    try
                    {
                        var command = JsonSerializer.Deserialize(message, SourceGenerationContext.Default.SRSCommand);
                        switch (command.Command)
                        {
                            case CommandType.LOS_REQUEST:
                                SRS.Instance.losRequests.Enqueue(command.LOSRequest);
                                break;
                            case CommandType.RADIO_INFO:
                                SRS.Instance.Radios = command.CombinedRadios;
                                break;
                            default:
                                break;
                        }

                    }
                    catch
                    {
                        // ignore malformatted.
                    }
                }
            }
        }

        public ValueTask SendAsync(string message)
        {
            return SendAsync(Encoding.GetBytes(message));
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> message)
        {
            if (message.IsEmpty)
            {
                return default;
            }

            // This may happen concurrently to the incoming processing loop
            // We want to make sure the pipe object doesn't get cleared beneath us.
            lock (_sendLock)
            {
                if (_pipe == null || !_pipe.IsConnected)
                {
                    // Fail. too early!
                    throw new InvalidOperationException("Pipe is not available.");
                }

                return _pipe.WriteAsync(message, _token);
            }
        }

        async Task<string> PumpMessage(CancellationToken token)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[1024];
            do
            {
                var read = await _pipe.ReadAsync(buffer, token);
                await stream.WriteAsync(buffer.AsMemory(0, read), token);
            } while (!_pipe.IsMessageComplete && !token.IsCancellationRequested);
            return Encoding.GetString(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        }

        public void Dispose()
        {
            _worker.Dispose(); 
        }
    }
}
