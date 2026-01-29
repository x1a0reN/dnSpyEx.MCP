using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
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
		readonly Logging.IMcpLogger logger;
		readonly string pipeName;
		CancellationTokenSource? cts;
		Task? serverTask;

		public McpIpcServer(McpRequestHandler handler, Logging.IMcpLogger logger, string? pipeName = null) {
			this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			var envPipe = Environment.GetEnvironmentVariable(PipeEnvVar);
			var resolved = string.IsNullOrWhiteSpace(pipeName) ? envPipe : pipeName;
			this.pipeName = string.IsNullOrWhiteSpace(resolved) ? DefaultPipeName : resolved!;
		}

		public void Start() {
			if (cts is not null)
				return;
			cts = new CancellationTokenSource();
			serverTask = Task.Run(() => RunAsync(cts.Token));
			logger.Info($"MCP pipe server started: {pipeName}");
		}

		public void Dispose() {
			if (cts is null)
				return;
			cts.Cancel();
			try {
				serverTask?.Wait(TimeSpan.FromSeconds(2.0));
			}
			catch (AggregateException) {
			}
			cts.Dispose();
			cts = null;
			serverTask = null;
			logger.Info("MCP pipe server stopped");
		}

		async Task RunAsync(CancellationToken token) {
			while (!token.IsCancellationRequested) {
				NamedPipeServerStream pipe;
				try {
					pipe = CreateServerPipe();
				}
				catch (Exception ex) {
					logger.Error($"MCP pipe create failed: {ex.Message}");
					Debug.WriteLine(ex);
					await Task.Delay(500, token).ConfigureAwait(false);
					continue;
				}

				using (pipe) {
					try {
						await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
					}
					catch (OperationCanceledException) {
						return;
					}
					catch (Exception ex) {
						logger.Error($"MCP pipe wait failed: {ex.Message}");
						Debug.WriteLine(ex);
						await Task.Delay(250, token).ConfigureAwait(false);
						continue;
					}

					logger.Info("MCP pipe client connected");
					await HandleClientAsync(pipe, token).ConfigureAwait(false);
					logger.Info("MCP pipe client disconnected");
				}
			}
		}

		async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token) {
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			using var reader = new StreamReader(pipe, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
			using var writer = new StreamWriter(pipe, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

			try {
				while (!token.IsCancellationRequested && pipe.IsConnected) {
					string? line;
					try {
						line = await reader.ReadLineAsync().ConfigureAwait(false);
					}
					catch (IOException ex) {
						logger.Warn($"MCP pipe read failed: {ex.Message}");
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
					catch (JsonException ex) {
						logger.Warn($"MCP pipe: JSON parse error: {ex.Message}");
						await WriteErrorAsync(writer, null, -32700, "Parse error").ConfigureAwait(false);
						continue;
					}

					var response = handler.Handle(request);
					if (response is null)
						continue;

					try {
						await writer.WriteLineAsync(response.ToString(Formatting.None)).ConfigureAwait(false);
					}
					catch (IOException ex) {
						logger.Warn($"MCP pipe write failed: {ex.Message}");
						break;
					}
				}
			}
			catch (Exception ex) {
				logger.Error($"MCP pipe handler crashed: {ex}");
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

		NamedPipeServerStream CreateServerPipe() {
			try {
				var security = BuildPipeSecurity();
#if NETFRAMEWORK
				return new NamedPipeServerStream(
					pipeName,
					PipeDirection.InOut,
					1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous,
					4096,
					4096,
					security);
#else
				return NamedPipeServerStreamAcl.Create(
					pipeName,
					PipeDirection.InOut,
					1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous,
					4096,
					4096,
					security);
#endif
			}
			catch (Exception ex) when (
				ex is NotSupportedException ||
				ex is PlatformNotSupportedException ||
				ex is UnauthorizedAccessException) {
				logger.Warn($"MCP pipe security fallback: {ex.Message}");
				return new NamedPipeServerStream(
					pipeName,
					PipeDirection.InOut,
					1,
					PipeTransmissionMode.Byte,
					PipeOptions.Asynchronous);
			}
		}

		static PipeSecurity BuildPipeSecurity() {
			var security = new PipeSecurity();
			var user = WindowsIdentity.GetCurrent().User;
			if (user is not null)
				security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));

			var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
			security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.ReadWrite, AccessControlType.Allow));

			return security;
		}
	}
}
