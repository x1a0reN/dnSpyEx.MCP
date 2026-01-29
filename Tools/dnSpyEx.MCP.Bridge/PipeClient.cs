using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dnSpyEx.MCP.Bridge {
	sealed class PipeClient : IDisposable {
		const int DefaultConnectTimeoutSeconds = 10;

		readonly string pipeName;
		NamedPipeClientStream? pipe;
		StreamReader? reader;
		StreamWriter? writer;
		readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

		public PipeClient(string pipeName) {
			this.pipeName = string.IsNullOrWhiteSpace(pipeName) ? McpPipeDefaults.DefaultPipeName : pipeName;
		}

		public async Task<JObject> CallAsync(JObject request, CancellationToken token) {
			await gate.WaitAsync(token).ConfigureAwait(false);
			try {
				return await CallCoreAsync(request, token, allowRetry: true).ConfigureAwait(false);
			}
			finally {
				gate.Release();
			}
		}

		async Task<JObject> CallCoreAsync(JObject request, CancellationToken token, bool allowRetry) {
			try {
				await EnsureConnectedAsync(token).ConfigureAwait(false);
				var lineRequest = request.ToString(Formatting.None);
				await writer!.WriteLineAsync(lineRequest).ConfigureAwait(false);
				var line = await reader!.ReadLineAsync().ConfigureAwait(false);
				if (line is null)
					throw new IOException("Pipe closed");
				return JObject.Parse(line);
			}
			catch (IOException) when (allowRetry) {
				ResetPipe();
				return await CallCoreAsync(request, token, allowRetry: false).ConfigureAwait(false);
			}
		}

		async Task EnsureConnectedAsync(CancellationToken token) {
			if (pipe is not null && pipe.IsConnected)
				return;

			ResetPipe();
			pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
			cts.CancelAfter(TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds));
			try {
				await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!token.IsCancellationRequested) {
				throw new TimeoutException($"Timed out connecting to pipe '{pipeName}'.");
			}
			catch {
				ResetPipe();
				throw;
			}

			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			reader = new StreamReader(pipe, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
			writer = new StreamWriter(pipe, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
		}

		void ResetPipe() {
			try { writer?.Dispose(); } catch { }
			try { reader?.Dispose(); } catch { }
			try { pipe?.Dispose(); } catch { }
			writer = null;
			reader = null;
			pipe = null;
		}

		public void Dispose() {
			ResetPipe();
			gate.Dispose();
		}
	}
}
