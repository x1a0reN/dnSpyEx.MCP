using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dnSpyEx.MCP.Http {
	sealed class McpHttpServer : IDisposable {
		const int DefaultPort = 13337;
		const string PrefixEnvVar = "DNSPYEX_MCP_HTTP_PREFIX";
		const string PortEnvVar = "DNSPYEX_MCP_HTTP_PORT";

		readonly Ipc.McpRequestHandler handler;
		readonly Logging.IMcpLogger logger;
		readonly string prefix;
		HttpListener? listener;
		CancellationTokenSource? cts;
		Task? serverTask;

		public McpHttpServer(Ipc.McpRequestHandler handler, Logging.IMcpLogger logger, string? prefixOverride = null) {
			this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			prefix = ResolvePrefix(prefixOverride);
		}

		public void Start() {
			if (cts is not null)
				return;
			cts = new CancellationTokenSource();
			listener = new HttpListener();
			listener.Prefixes.Add(prefix);
			listener.Start();
			serverTask = Task.Run(() => RunAsync(cts.Token));
			logger.Info($"MCP http server started: {prefix}");
		}

		public void Dispose() {
			if (cts is null)
				return;
			cts.Cancel();
			try {
				listener?.Stop();
				serverTask?.Wait(TimeSpan.FromSeconds(2.0));
			}
			catch (AggregateException) {
			}
			catch (ObjectDisposedException) {
			}
			listener?.Close();
			listener = null;
			cts.Dispose();
			cts = null;
			serverTask = null;
			logger.Info("MCP http server stopped");
		}

		async Task RunAsync(CancellationToken token) {
			while (!token.IsCancellationRequested) {
				HttpListenerContext context;
				try {
					context = await listener!.GetContextAsync().ConfigureAwait(false);
				}
				catch (ObjectDisposedException) {
					return;
				}
				catch (HttpListenerException) {
					return;
				}
				catch (Exception ex) {
					logger.Error($"MCP http accept failed: {ex.Message}");
					Debug.WriteLine(ex);
					await Task.Delay(200, token).ConfigureAwait(false);
					continue;
				}

				_ = HandleContextAsync(context, token);
			}
		}

		async Task HandleContextAsync(HttpListenerContext context, CancellationToken token) {
			try {
				var request = context.Request;
				if (request.HttpMethod == "GET" && IsHealthPath(request.Url?.AbsolutePath)) {
					await WriteTextAsync(context.Response, 200, "ok").ConfigureAwait(false);
					return;
				}

				if (request.HttpMethod != "POST" || !IsRpcPath(request.Url?.AbsolutePath)) {
					await WriteTextAsync(context.Response, 404, "not found").ConfigureAwait(false);
					return;
				}

				string body;
				using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, true, 4096, leaveOpen: true)) {
					body = await reader.ReadToEndAsync().ConfigureAwait(false);
				}

				if (string.IsNullOrWhiteSpace(body)) {
					await WriteJsonAsync(context.Response, MakeError(null, -32600, "Empty request")).ConfigureAwait(false);
					return;
				}

				JObject? obj;
				try {
					obj = JObject.Parse(body);
				}
				catch (JsonException ex) {
					logger.Warn($"MCP http parse error: {ex.Message}");
					await WriteJsonAsync(context.Response, MakeError(null, -32700, "Parse error")).ConfigureAwait(false);
					return;
				}

				var method = obj["method"]?.Value<string>() ?? "(unknown)";
				logger.Info($"MCP http request: {method}");
				var response = handler.Handle(obj);
				if (response is null) {
					context.Response.StatusCode = 204;
					context.Response.Close();
					return;
				}

				await WriteJsonAsync(context.Response, response).ConfigureAwait(false);
			}
			catch (Exception ex) {
				logger.Error($"MCP http handler error: {ex.Message}");
				try {
					await WriteJsonAsync(context.Response, MakeError(null, -32603, ex.Message)).ConfigureAwait(false);
				}
				catch (Exception) {
				}
			}
		}

		static bool IsRpcPath(string? path) => string.Equals(path, "/rpc", StringComparison.OrdinalIgnoreCase);

		static bool IsHealthPath(string? path) => string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase);

		static async Task WriteTextAsync(HttpListenerResponse response, int statusCode, string text) {
			var bytes = Encoding.UTF8.GetBytes(text);
			response.StatusCode = statusCode;
			response.ContentType = "text/plain; charset=utf-8";
			response.ContentLength64 = bytes.Length;
			await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
			response.Close();
		}

		static async Task WriteJsonAsync(HttpListenerResponse response, JToken token) {
			var json = JsonConvert.SerializeObject(token, Formatting.None);
			var bytes = Encoding.UTF8.GetBytes(json);
			response.StatusCode = 200;
			response.ContentType = "application/json; charset=utf-8";
			response.ContentLength64 = bytes.Length;
			await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
			response.Close();
		}

		static JObject MakeError(JToken? id, int code, string message) {
			return new JObject {
				["jsonrpc"] = "2.0",
				["id"] = id,
				["error"] = new JObject {
					["code"] = code,
					["message"] = message,
				},
			};
		}

		static string ResolvePrefix(string? overridePrefix) {
			var prefix = !string.IsNullOrWhiteSpace(overridePrefix) ? overridePrefix : Environment.GetEnvironmentVariable(PrefixEnvVar);
			if (string.IsNullOrWhiteSpace(prefix)) {
				var envPort = Environment.GetEnvironmentVariable(PortEnvVar);
				var port = DefaultPort;
				if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out var parsed) && parsed > 0)
					port = parsed;
				prefix = $"http://127.0.0.1:{port}/";
			}

			if (!prefix!.EndsWith("/", StringComparison.Ordinal))
				prefix += "/";
			return prefix;
		}
	}
}
