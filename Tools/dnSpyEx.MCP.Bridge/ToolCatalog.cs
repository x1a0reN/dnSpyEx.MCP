using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace dnSpyEx.MCP.Bridge {
	sealed class ToolCatalog {
		public IReadOnlyDictionary<string, ToolDef> Tools { get; }

		public ToolCatalog() {
			var list = new List<ToolDef> {
				new ToolDef(
					"dnspy.listAssemblies",
					"List loaded assemblies and modules.",
					new JObject {
						["type"] = "object",
						["properties"] = new JObject(),
						["additionalProperties"] = false,
					},
					"listAssemblies"),
				new ToolDef(
					"dnspy.listNamespaces",
					"List namespaces in a module.",
					new JObject {
						["type"] = "object",
						["properties"] = new JObject {
							["moduleMvid"] = new JObject {
								["type"] = "string",
								["description"] = "Module MVID GUID",
							},
						},
						["required"] = new JArray("moduleMvid"),
						["additionalProperties"] = false,
					},
					"listNamespaces"),
				new ToolDef(
					"dnspy.listTypes",
					"List types in a module namespace.",
					new JObject {
						["type"] = "object",
						["properties"] = new JObject {
							["moduleMvid"] = new JObject {
								["type"] = "string",
								["description"] = "Module MVID GUID",
							},
							["namespace"] = new JObject {
								["type"] = "string",
								["description"] = "Namespace (empty string for global)",
							},
						},
						["required"] = new JArray("moduleMvid", "namespace"),
						["additionalProperties"] = false,
					},
					"listTypes"),
				new ToolDef(
					"dnspy.listMembers",
					"List members in a type.",
					new JObject {
						["type"] = "object",
						["properties"] = new JObject {
							["moduleMvid"] = new JObject {
								["type"] = "string",
								["description"] = "Module MVID GUID",
							},
							["typeToken"] = new JObject {
								["type"] = "integer",
								["description"] = "Type MD token (uint)",
							},
						},
						["required"] = new JArray("moduleMvid", "typeToken"),
						["additionalProperties"] = false,
					},
					"listMembers"),
				new ToolDef(
					"dnspy.decompile",
					"Decompile a module, assembly, namespace, type, or member.",
					new JObject {
						["type"] = "object",
						["properties"] = new JObject {
							["kind"] = new JObject {
								["type"] = "string",
								["description"] = "assembly|module|namespace|type|method|field|property|event",
							},
							["moduleMvid"] = new JObject {
								["type"] = "string",
								["description"] = "Module MVID GUID",
							},
							["namespace"] = new JObject {
								["type"] = "string",
								["description"] = "Namespace (for kind=namespace)",
							},
							["token"] = new JObject {
								["type"] = "integer",
								["description"] = "Member MD token (for kind=type/method/field/property/event)",
							},
						},
						["required"] = new JArray("kind", "moduleMvid"),
						["additionalProperties"] = false,
					},
					"decompile"),
				new ToolDef(
					"dnspy.getSelectedText",
					"Get the current selection text from the active document tab.",
					new JObject {
						["type"] = "object",
						["properties"] = new JObject(),
						["additionalProperties"] = false,
					},
					"getSelectedText"),
			};

			var map = new Dictionary<string, ToolDef>();
			foreach (var tool in list)
				map[tool.Name] = tool;
			Tools = map;
		}
	}

	sealed class ToolDef {
		public string Name { get; }
		public string Description { get; }
		public JObject InputSchema { get; }
		public string Method { get; }

		public ToolDef(string name, string description, JObject inputSchema, string method) {
			Name = name;
			Description = description;
			InputSchema = inputSchema;
			Method = method;
		}
	}
}
