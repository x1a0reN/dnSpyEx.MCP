using System;
using System.Linq;
using System.Windows;
using dnlib.DotNet;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using Newtonsoft.Json.Linq;

namespace dnSpyEx.MCP.Ipc {
	sealed class McpRequestHandler {
		readonly IDocumentTabService documentTabService;
		readonly IDecompilerService decompilerService;
		readonly Logging.IMcpLogger logger;

		public McpRequestHandler(IDocumentTabService documentTabService, IDecompilerService decompilerService, Logging.IMcpLogger logger) {
			this.documentTabService = documentTabService ?? throw new ArgumentNullException(nameof(documentTabService));
			this.decompilerService = decompilerService ?? throw new ArgumentNullException(nameof(decompilerService));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public JObject? Handle(JObject request) {
			var id = request["id"];
			var method = request["method"]?.Value<string>();
			if (string.IsNullOrWhiteSpace(method))
				return MakeError(id, -32600, "Invalid Request");

			try {
				logger.Info($"MCP request: {method}");
				var result = Execute(method!, request["params"] as JObject);
				return id is null ? null : MakeResult(id, result);
			}
			catch (RpcException ex) {
				logger.Warn($"MCP error: {ex.Message}");
				return MakeError(id, ex.Code, ex.Message);
			}
			catch (Exception ex) {
				logger.Error($"MCP exception: {ex.Message}");
				return MakeError(id, -32603, ex.Message);
			}
		}

		JToken Execute(string method, JObject? parameters) {
			return RunOnUi(() => {
				return method switch {
					"listAssemblies" => ListAssemblies(),
					"listNamespaces" => ListNamespaces(parameters),
					"listTypes" => ListTypes(parameters),
					"listMembers" => ListMembers(parameters),
					"decompile" => Decompile(parameters),
					"getSelectedText" => GetSelectedText(),
					_ => throw new RpcException(-32601, $"Method not found: {method}"),
				};
			});
		}

		JToken ListAssemblies() {
			var nodes = documentTabService.DocumentTreeView.GetAllModuleNodes();
			var list = new JArray();
			foreach (var node in nodes) {
				var document = node.Document;
				var module = document.ModuleDef;
				if (module is null)
					continue;
				var asm = document.AssemblyDef ?? module.Assembly;
				var obj = new JObject {
					["moduleName"] = Utf8ToString(module.Name),
					["moduleMvid"] = FormatMvid(module.Mvid),
					["assemblyName"] = asm is null ? string.Empty : Utf8ToString(asm.Name),
					["assemblyFullName"] = asm?.FullName,
					["filename"] = document.Filename,
				};
				list.Add(obj);
			}
			return list;
		}

		JToken ListNamespaces(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var namespaces = module.GetTypes()
				.Select(t => Utf8ToString(t.Namespace))
				.Distinct(StringComparer.Ordinal)
				.OrderBy(s => s, StringComparer.Ordinal)
				.ToArray();
			return new JArray(namespaces);
		}

		JToken ListTypes(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var ns = RequireString(parameters, "namespace");
			var types = module.GetTypes()
				.Where(t => string.Equals(Utf8ToString(t.Namespace), ns, StringComparison.Ordinal))
				.Select(t => new JObject {
					["name"] = Utf8ToString(t.Name),
					["fullName"] = t.FullName,
					["isNested"] = t.IsNested,
					["token"] = (uint)t.MDToken.Raw,
					["moduleMvid"] = FormatMvid(module.Mvid),
				});
			return new JArray(types);
		}

		JToken ListMembers(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var typeToken = RequireUInt(parameters, "typeToken");
			var type = module.ResolveToken(typeToken) as TypeDef;
			if (type is null)
				throw new RpcException(-32602, "Type not found");

			var list = new JArray();
			foreach (var method in type.Methods)
				list.Add(MemberToJson("method", module, method));
			foreach (var field in type.Fields)
				list.Add(MemberToJson("field", module, field));
			foreach (var prop in type.Properties)
				list.Add(MemberToJson("property", module, prop));
			foreach (var ev in type.Events)
				list.Add(MemberToJson("event", module, ev));
			return list;
		}

		JToken Decompile(JObject? parameters) {
			var kind = RequireString(parameters, "kind");
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var decompiler = decompilerService.Decompiler;
			var output = new StringBuilderDecompilerOutput();
			var ctx = new DecompilationContext();

			switch (kind) {
			case "assembly":
				var asm = module.Assembly;
				if (asm is null)
					throw new RpcException(-32602, "Assembly not found");
				decompiler.Decompile(asm, output, ctx);
				break;
			case "module":
				decompiler.Decompile(module, output, ctx);
				break;
			case "namespace":
				var ns = RequireString(parameters, "namespace");
				var types = module.GetTypes()
					.Where(t => string.Equals(Utf8ToString(t.Namespace), ns, StringComparison.Ordinal))
					.ToArray();
				decompiler.DecompileNamespace(ns, types, output, ctx);
				break;
			case "type":
				decompiler.Decompile(ResolveMember<TypeDef>(module, RequireUInt(parameters, "token")), output, ctx);
				break;
			case "method":
				decompiler.Decompile(ResolveMember<MethodDef>(module, RequireUInt(parameters, "token")), output, ctx);
				break;
			case "field":
				decompiler.Decompile(ResolveMember<FieldDef>(module, RequireUInt(parameters, "token")), output, ctx);
				break;
			case "property":
				decompiler.Decompile(ResolveMember<PropertyDef>(module, RequireUInt(parameters, "token")), output, ctx);
				break;
			case "event":
				decompiler.Decompile(ResolveMember<EventDef>(module, RequireUInt(parameters, "token")), output, ctx);
				break;
			default:
				throw new RpcException(-32602, $"Unknown kind: {kind}");
			}

			return new JObject {
				["language"] = decompiler.UniqueNameUI,
				["text"] = output.ToString(),
			};
		}

		JToken GetSelectedText() {
			var viewer = documentTabService.ActiveTab?.UIContext as IDocumentViewer;
			if (viewer is null) {
				return new JObject {
					["hasViewer"] = false,
					["isEmpty"] = true,
					["text"] = string.Empty,
				};
			}

			var selection = viewer.Selection;
			var selectedText = string.Concat(selection.SelectedSpans.Select(span => span.GetText()));
			return new JObject {
				["hasViewer"] = true,
				["isEmpty"] = selection.IsEmpty,
				["caretPosition"] = viewer.Caret.Position.BufferPosition.Position,
				["text"] = selectedText,
			};
		}

		static JObject MemberToJson(string kind, ModuleDef module, IMemberDef member) {
			return new JObject {
				["kind"] = kind,
				["name"] = Utf8ToString(member.Name),
				["fullName"] = member.FullName,
				["token"] = (uint)member.MDToken.Raw,
				["moduleMvid"] = FormatMvid(module.Mvid),
			};
		}

		ModuleDef FindModule(Guid mvid) {
			var node = documentTabService.DocumentTreeView
				.GetAllModuleNodes()
				.FirstOrDefault(n => n.Document.ModuleDef?.Mvid == mvid);
			var module = node?.Document.ModuleDef;
			if (module is null)
				throw new RpcException(-32602, "Module not found");
			return module;
		}

		static T ResolveMember<T>(ModuleDef module, uint token) where T : class {
			var member = module.ResolveToken(token) as T;
			if (member is null)
				throw new RpcException(-32602, "Member not found");
			return member;
		}

		static Guid RequireGuid(JObject? parameters, string name) {
			var value = RequireString(parameters, name);
			if (!Guid.TryParse(value, out var guid))
				throw new RpcException(-32602, $"Invalid GUID for {name}");
			return guid;
		}

		static string RequireString(JObject? parameters, string name) {
			var value = parameters?[name]?.Value<string>();
			if (string.IsNullOrWhiteSpace(value))
				throw new RpcException(-32602, $"Missing parameter: {name}");
			return value;
		}

		static uint RequireUInt(JObject? parameters, string name) {
			var token = parameters?[name]?.Value<uint?>();
			if (token is null)
				throw new RpcException(-32602, $"Missing parameter: {name}");
			return token.Value;
		}

		static JObject MakeResult(JToken? id, JToken result) {
			return new JObject {
				["jsonrpc"] = "2.0",
				["id"] = id,
				["result"] = result,
			};
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

		static T RunOnUi<T>(Func<T> func) {
			var dispatcher = Application.Current?.Dispatcher;
			if (dispatcher is null || dispatcher.CheckAccess())
				return func();
			return dispatcher.Invoke(func);
		}

		static string Utf8ToString(UTF8String? value) =>
			value is null ? string.Empty : value.ToString();

		static string FormatMvid(Guid? mvid) =>
			mvid.HasValue ? mvid.Value.ToString("D") : string.Empty;

		sealed class RpcException : Exception {
			public int Code { get; }
			public RpcException(int code, string message) : base(message) => Code = code;
		}
	}
}
