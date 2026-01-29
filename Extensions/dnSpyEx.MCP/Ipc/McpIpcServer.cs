using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dnSpyEx.MCP.Ipc {
	sealed class McpIpcServer : IDisposable {
		const string DefaultPipeName = "dnSpyEx.MCP";
		const string PipeEnvVar = "DNSPYEX_MCP_PIPE";

		readonly McpRequestHandler handler;
		readonly string pipeName;
		CancellationTokenSource? cts;
		Task? serverTask;

		public McpIpcServer(McpRequestHandler handler, string? pipeName = null) {
			this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
			var envPipe = Environment.GetEnvironmentVariable(PipeEnvVar);
			var resolved = string.IsNullOrWhiteSpace(pipeName) ? envPipe : pipeName;
			this.pipeName = string.IsNullOrWhiteSpace(resolved) ? DefaultPipeName : resolved!;
		}

		public void Start() {
			if (cts is not null)
				return;
			cts = new CancellationTokenSource();
			serverTask = Task.Run(() => RunAsync(cts.Token));
		}

		public void Dispose() {
			if (cts is null)
				return;
			cts.Cancel();
			try {
				serverTask?.Wait(TimeSpan.FromSeconds(2));
			}
			catch (AggregateException) {
			}
			cts.Dispose();
			cts = null;
			serverTask = null;
		}

		async Task RunAsync(CancellationToken token) {
			while (!token.IsCancellationRequested) {
				using var pipe = new NamedPipeServerStream(
					pipeName,
					PipeDirection.InOut,
					1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous);

				try {
					await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) {
					return;
				}
				catch (Exception ex) {
					Debug.WriteLine(ex);
					await Task.Delay(250, token).ConfigureAwait(false);
					continue;
				}

				await HandleClientAsync(pipe, token).ConfigureAwait(false);
			}
		}

		async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token) {
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			using var reader = new StreamReader(pipe, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
			using var writer = new StreamWriter(pipe, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

			while (!token.IsCancellationRequested && pipe.IsConnected) {
				string? line;
				try {
					line = await reader.ReadLineAsync().ConfigureAwait(false);
				}
				catch (IOException) {
					break;
				}

				if (line is null)
					break;
				if (string.IsNullOrWhiteSpace(line))
					continue;

				JObject? request;
				try {
					request = JObject.Parse(line);
				}
				catch (JsonException) {
					await WriteErrorAsync(writer, null, -32700, "Parse error").ConfigureAwait(false);
					continue;
				}

				var response = handler.Handle(request);
				if (response is null)
					continue;

				await writer.WriteLineAsync(response.ToString(Formatting.None)).ConfigureAwait(false);
			}
		}

		static Task WriteErrorAsync(StreamWriter writer, JToken? id, int code, string message) {
			var error = new JObject {
				["code"] = code,
				["message"] = message,
			};
			var response = new JObject {
				["jsonrpc"] = "2.0",
				["id"] = id,
				["error"] = error,
			};
			return writer.WriteLineAsync(response.ToString(Formatting.None));
		}
	}
}
