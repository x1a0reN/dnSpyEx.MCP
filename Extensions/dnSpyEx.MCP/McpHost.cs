using System;
using System.ComponentModel.Composition;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs;
using dnSpyEx.MCP.Ipc;

namespace dnSpyEx.MCP {
	[Export]
	sealed class McpHost : IDisposable {
		readonly McpIpcServer server;

		[ImportingConstructor]
		McpHost(IDocumentTabService documentTabService, IDecompilerService decompilerService) {
			var handler = new McpRequestHandler(documentTabService, decompilerService);
			server = new McpIpcServer(handler);
		}

		public void Start() => server.Start();

		public void Dispose() => server.Dispose();
	}
}
