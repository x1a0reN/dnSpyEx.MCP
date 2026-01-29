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
		readonly NamedPipeClientStream pipe;
		readonly StreamReader reader;
		readonly StreamWriter writer;
		readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

		public PipeClient(string pipeName) {
			pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			reader = new StreamReader(pipe, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
			writer = new StreamWriter(pipe, encoding, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };
		}

		public async Task ConnectAsync(TimeSpan timeout, CancellationToken token) {
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
			cts.CancelAfter(timeout);
			await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
		}

		public async Task<JObject> CallAsync(JObject request, CancellationToken token) {
			await gate.WaitAsync(token).ConfigureAwait(false);
			try {
				await writer.WriteLineAsync(request.ToString(Formatting.None)).ConfigureAwait(false);
				var line = await reader.ReadLineAsync().ConfigureAwait(false);
				if (line is null)
					throw new IOException("Pipe closed");
				return JObject.Parse(line);
			}
			finally {
				gate.Release();
			}
		}

		public void Dispose() {
			pipe.Dispose();
			reader.Dispose();
			writer.Dispose();
			gate.Dispose();
		}
	}
}
