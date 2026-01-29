using System;
using System.ComponentModel.Composition;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Output;
using dnSpyEx.MCP.Logging;
using dnSpyEx.MCP.Ipc;

namespace dnSpyEx.MCP {
	[Export]
	sealed class McpHost : IDisposable {
		readonly McpIpcServer server;

		[ImportingConstructor]
		McpHost(IDocumentTabService documentTabService, IDecompilerService decompilerService, IOutputService outputService) {
			var logger = new McpOutputLogger(outputService, OutputPaneGuid, OutputPaneName);
			var handler = new McpRequestHandler(documentTabService, decompilerService, logger);
			server = new McpIpcServer(handler, logger);
		}

		public void Start() => server.Start();

		public void Dispose() => server.Dispose();

		static readonly Guid OutputPaneGuid = new Guid("A73D6DE4-0D0A-4D03-9E93-7F7A1D4D0E5C");
		const string OutputPaneName = "dnSpyEx.MCP";
	}
}
