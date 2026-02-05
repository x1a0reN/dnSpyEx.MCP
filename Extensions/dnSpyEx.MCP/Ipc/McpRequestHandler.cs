using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.Tabs;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.TreeView;
using dnSpy.Contracts.Utilities;
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
					"getAssemblyInfo" => GetAssemblyInfo(parameters),
					"listNamespaces" => ListNamespaces(parameters),
					"listTypes" => ListTypes(parameters),
					"searchTypes" => SearchTypes(parameters),
					"searchMembers" => SearchMembers(parameters),
					"searchStrings" => SearchStrings(parameters),
					"listMembers" => ListMembers(parameters),
					"getTypeInfo" => GetTypeInfo(parameters),
					"getTypeFields" => GetTypeFields(parameters),
					"getTypeProperty" => GetTypeProperty(parameters),
					"decompile" => Decompile(parameters),
					"decompileMethod" => DecompileMember<MethodDef>(parameters),
					"decompileField" => DecompileMember<FieldDef>(parameters),
					"decompileProperty" => DecompileMember<PropertyDef>(parameters),
					"decompileEvent" => DecompileMember<EventDef>(parameters),
					"decompileType" => DecompileType(parameters),
					"getMethodSignature" => GetMethodSignature(parameters),
					"getFieldInfo" => GetFieldInfo(parameters),
					"getEnumInfo" => GetEnumInfo(parameters),
					"getStructInfo" => GetStructInfo(parameters),
					"getInterfaceInfo" => GetInterfaceInfo(parameters),
					"getTypeDependencies" => GetTypeDependencies(parameters),
					"getInheritanceTree" => GetInheritanceTree(parameters),
					"findPathToType" => FindPathToType(parameters),
					"findReferences" => FindReferences(parameters),
					"getCallers" => GetCallers(parameters),
					"getCallees" => GetCallees(parameters),
					"search" => Search(parameters),
					"getSelectedText" => GetSelectedText(),
					"getSelectedMember" => GetSelectedMember(),
					"openInDnSpy" => OpenInDnSpy(parameters),
					"exampleFlow" => ExampleFlow(),
					"dnspy.exampleFlow" => ExampleFlow(),
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

		JToken GetAssemblyInfo(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var asm = module.Assembly;
			var modules = asm?.Modules ?? new List<ModuleDef> { module };
			var moduleList = new JArray(modules.Select(m => new JObject {
				["moduleName"] = Utf8ToString(m.Name),
				["moduleMvid"] = FormatMvid(m.Mvid),
				["filename"] = m.Location ?? string.Empty,
			}));

			var references = new JArray();
			foreach (var asmRef in module.GetAssemblyRefs()) {
				references.Add(new JObject {
					["name"] = Utf8ToString(asmRef.Name),
					["fullName"] = asmRef.FullName,
					["version"] = asmRef.Version?.ToString() ?? string.Empty,
				});
			}

			var resources = new JArray();
			foreach (var resource in module.Resources) {
				resources.Add(new JObject {
					["name"] = Utf8ToString(resource.Name),
					["resourceType"] = resource.GetType().Name,
				});
			}

			return new JObject {
				["assemblyName"] = asm is null ? string.Empty : Utf8ToString(asm.Name),
				["assemblyFullName"] = asm?.FullName ?? string.Empty,
				["version"] = asm?.Version?.ToString() ?? string.Empty,
				["culture"] = Utf8ToString(asm?.Culture),
				["publicKeyToken"] = asm?.PublicKeyToken?.ToString() ?? string.Empty,
				["modules"] = moduleList,
				["references"] = references,
				["resources"] = resources,
				["moduleMvid"] = FormatMvid(module.Mvid),
				["filename"] = module.Location ?? string.Empty,
			};
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
			var ns = RequireStringAllowEmpty(parameters, "namespace");
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

		JToken SearchTypes(JObject? parameters) {
			var pattern = RequireString(parameters, "pattern");
			var caseSensitive = GetBool(parameters, "caseSensitive", false);
			var useRegex = GetBool(parameters, "useRegex", false);
			var searchFrameworkAssemblies = GetBool(parameters, "searchFrameworkAssemblies", true);
			var searchCompilerGeneratedMembers = GetBool(parameters, "searchCompilerGeneratedMembers", true);
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var matcher = PatternMatcher.From(pattern, useRegex, caseSensitive);
			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			var modules = FilterModules(BuildModuleIndex().Values, searchFrameworkAssemblies);

			foreach (var moduleItem in modules) {
				foreach (var type in moduleItem.Module.GetTypes()) {
					if (collector.TooManyResults)
						break;
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(type))
						continue;
					if (!matcher.IsMatch(Utf8ToString(type.Name)) && !matcher.IsMatch(type.FullName))
						continue;
					collector.TryAdd($"type:{moduleItem.ModuleMvid}:{type.MDToken.Raw:X8}", new JObject {
						["kind"] = "type",
						["name"] = Utf8ToString(type.Name),
						["fullName"] = type.FullName,
						["typeKind"] = GetTypeKind(type),
						["token"] = (uint)type.MDToken.Raw,
						["moduleMvid"] = moduleItem.ModuleMvid,
						["documentFilename"] = moduleItem.Filename,
					});
				}
			}

			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
			};
		}

		JToken SearchMembers(JObject? parameters) {
			var pattern = RequireString(parameters, "pattern");
			var caseSensitive = GetBool(parameters, "caseSensitive", false);
			var useRegex = GetBool(parameters, "useRegex", false);
			var searchFrameworkAssemblies = GetBool(parameters, "searchFrameworkAssemblies", true);
			var searchCompilerGeneratedMembers = GetBool(parameters, "searchCompilerGeneratedMembers", true);
			var memberKind = ParseMemberKind(GetString(parameters, "memberKind") ?? "any");
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var matcher = PatternMatcher.From(pattern, useRegex, caseSensitive);
			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			var modules = FilterModules(BuildModuleIndex().Values, searchFrameworkAssemblies);

			foreach (var moduleItem in modules) {
				foreach (var type in moduleItem.Module.GetTypes()) {
					if (collector.TooManyResults)
						break;
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(type))
						continue;

					if (MemberKindMatches(memberKind, MemberKind.Method)) {
						foreach (var method in type.Methods) {
							if (!searchCompilerGeneratedMembers && IsCompilerGenerated(method))
								continue;
							if (!matcher.IsMatch(Utf8ToString(method.Name)) && !matcher.IsMatch(method.FullName))
								continue;
							collector.TryAdd($"method:{moduleItem.ModuleMvid}:{method.MDToken.Raw:X8}", new JObject {
								["kind"] = "method",
								["name"] = Utf8ToString(method.Name),
								["fullName"] = method.FullName,
								["token"] = (uint)method.MDToken.Raw,
								["declaringType"] = type.FullName,
								["declaringTypeToken"] = (uint)type.MDToken.Raw,
								["moduleMvid"] = moduleItem.ModuleMvid,
								["documentFilename"] = moduleItem.Filename,
							});
							if (collector.TooManyResults)
								break;
						}
					}

					if (collector.TooManyResults)
						break;

					if (MemberKindMatches(memberKind, MemberKind.Field)) {
						foreach (var field in type.Fields) {
							if (!searchCompilerGeneratedMembers && IsCompilerGenerated(field))
								continue;
							if (!matcher.IsMatch(Utf8ToString(field.Name)) && !matcher.IsMatch(field.FullName))
								continue;
							collector.TryAdd($"field:{moduleItem.ModuleMvid}:{field.MDToken.Raw:X8}", new JObject {
								["kind"] = "field",
								["name"] = Utf8ToString(field.Name),
								["fullName"] = field.FullName,
								["token"] = (uint)field.MDToken.Raw,
								["declaringType"] = type.FullName,
								["declaringTypeToken"] = (uint)type.MDToken.Raw,
								["moduleMvid"] = moduleItem.ModuleMvid,
								["documentFilename"] = moduleItem.Filename,
							});
							if (collector.TooManyResults)
								break;
						}
					}

					if (collector.TooManyResults)
						break;

					if (MemberKindMatches(memberKind, MemberKind.Property)) {
						foreach (var prop in type.Properties) {
							if (!searchCompilerGeneratedMembers && IsCompilerGenerated(prop))
								continue;
							if (!matcher.IsMatch(Utf8ToString(prop.Name)) && !matcher.IsMatch(prop.FullName))
								continue;
							collector.TryAdd($"property:{moduleItem.ModuleMvid}:{prop.MDToken.Raw:X8}", new JObject {
								["kind"] = "property",
								["name"] = Utf8ToString(prop.Name),
								["fullName"] = prop.FullName,
								["token"] = (uint)prop.MDToken.Raw,
								["declaringType"] = type.FullName,
								["declaringTypeToken"] = (uint)type.MDToken.Raw,
								["moduleMvid"] = moduleItem.ModuleMvid,
								["documentFilename"] = moduleItem.Filename,
							});
							if (collector.TooManyResults)
								break;
						}
					}

					if (collector.TooManyResults)
						break;

					if (MemberKindMatches(memberKind, MemberKind.Event)) {
						foreach (var ev in type.Events) {
							if (!searchCompilerGeneratedMembers && IsCompilerGenerated(ev))
								continue;
							if (!matcher.IsMatch(Utf8ToString(ev.Name)) && !matcher.IsMatch(ev.FullName))
								continue;
							collector.TryAdd($"event:{moduleItem.ModuleMvid}:{ev.MDToken.Raw:X8}", new JObject {
								["kind"] = "event",
								["name"] = Utf8ToString(ev.Name),
								["fullName"] = ev.FullName,
								["token"] = (uint)ev.MDToken.Raw,
								["declaringType"] = type.FullName,
								["declaringTypeToken"] = (uint)type.MDToken.Raw,
								["moduleMvid"] = moduleItem.ModuleMvid,
								["documentFilename"] = moduleItem.Filename,
							});
							if (collector.TooManyResults)
								break;
						}
					}
				}
			}

			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
			};
		}

		JToken SearchStrings(JObject? parameters) {
			var pattern = RequireString(parameters, "pattern");
			var caseSensitive = GetBool(parameters, "caseSensitive", false);
			var useRegex = GetBool(parameters, "useRegex", false);
			var searchFrameworkAssemblies = GetBool(parameters, "searchFrameworkAssemblies", true);
			var searchCompilerGeneratedMembers = GetBool(parameters, "searchCompilerGeneratedMembers", true);
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var matcher = PatternMatcher.From(pattern, useRegex, caseSensitive);
			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			var modules = FilterModules(BuildModuleIndex().Values, searchFrameworkAssemblies);

			foreach (var moduleItem in modules) {
				foreach (var type in moduleItem.Module.GetTypes()) {
					if (collector.TooManyResults)
						break;
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(type))
						continue;
					foreach (var method in type.Methods) {
						if (collector.TooManyResults)
							break;
						if (!searchCompilerGeneratedMembers && IsCompilerGenerated(method))
							continue;
						if (!method.HasBody || method.Body is null)
							continue;
						foreach (var instr in method.Body.Instructions) {
							if (instr.Operand is not string s)
								continue;
							if (!matcher.IsMatch(s))
								continue;
							collector.TryAdd($"string:{moduleItem.ModuleMvid}:{method.MDToken.Raw:X8}:{instr.Offset}", new JObject {
								["kind"] = "string",
								["value"] = s,
								["methodToken"] = (uint)method.MDToken.Raw,
								["methodFullName"] = method.FullName,
								["declaringType"] = type.FullName,
								["declaringTypeToken"] = (uint)type.MDToken.Raw,
								["ilOffset"] = instr.Offset,
								["moduleMvid"] = moduleItem.ModuleMvid,
								["documentFilename"] = moduleItem.Filename,
							});
							if (collector.TooManyResults)
								break;
						}
					}
				}
			}

			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
			};
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

		JToken GetTypeInfo(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			var fields = new JArray(type.Fields.Select(f => FieldInfoToJson(f)));
			var properties = new JArray(type.Properties.Select(p => PropertyInfoToJson(p)));
			var methods = new JArray(type.Methods.Select(m => MethodInfoToJson(m)));
			var events = new JArray(type.Events.Select(e => EventInfoToJson(e)));
			var attributes = new JArray(type.CustomAttributes.Select(a => a.AttributeType?.FullName ?? string.Empty));
			var interfaces = new JArray(type.Interfaces.Select(i => i.Interface?.FullName ?? string.Empty));

			return new JObject {
				["name"] = Utf8ToString(type.Name),
				["fullName"] = type.FullName,
				["namespace"] = Utf8ToString(type.Namespace),
				["baseType"] = type.BaseType?.FullName ?? string.Empty,
				["interfaces"] = interfaces,
				["attributes"] = attributes,
				["isPublic"] = type.IsPublic || type.IsNestedPublic,
				["isNested"] = type.IsNested,
				["isAbstract"] = type.IsAbstract,
				["isSealed"] = type.IsSealed,
				["isStatic"] = type.IsAbstract && type.IsSealed,
				["isValueType"] = type.IsValueType,
				["isEnum"] = type.IsEnum,
				["isInterface"] = type.IsInterface,
				["isDelegate"] = type.IsDelegate,
				["fields"] = fields,
				["properties"] = properties,
				["methods"] = methods,
				["events"] = events,
				["token"] = (uint)type.MDToken.Raw,
				["moduleMvid"] = FormatMvid(module.Mvid),
			};
		}

		JToken GetTypeFields(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			var pattern = GetString(parameters, "pattern") ?? "*";
			var caseSensitive = GetBool(parameters, "caseSensitive", false);
			var useRegex = GetBool(parameters, "useRegex", false);
			var matcher = PatternMatcher.From(pattern, useRegex, caseSensitive);

			var fields = new JArray();
			foreach (var field in type.Fields) {
				if (!matcher.IsMatch(Utf8ToString(field.Name)) && !matcher.IsMatch(field.FullName))
					continue;
				fields.Add(FieldInfoToJson(field));
			}

			return fields;
		}

		JToken GetTypeProperty(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var propertyToken = GetUInt(parameters, "propertyToken");
			PropertyDef? property;
			if (propertyToken.HasValue) {
				property = ResolveMember<PropertyDef>(module, propertyToken.Value);
			}
			else {
				var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
				var name = RequireString(parameters, "name");
				var caseSensitive = GetBool(parameters, "caseSensitive", false);
				var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
				property = type.Properties.FirstOrDefault(p => string.Equals(Utf8ToString(p.Name), name, comparison));
				if (property is null)
					throw new RpcException(-32602, "Property not found");
			}

			return PropertyInfoToJson(property);
		}

		JToken Decompile(JObject? parameters) {
			var kind = RequireString(parameters, "kind");
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			return kind switch {
				"method" => DecompileResolved(ResolveMember<MethodDef>(module, RequireUInt(parameters, "token"))),
				"field" => DecompileResolved(ResolveMember<FieldDef>(module, RequireUInt(parameters, "token"))),
				"property" => DecompileResolved(ResolveMember<PropertyDef>(module, RequireUInt(parameters, "token"))),
				"event" => DecompileResolved(ResolveMember<EventDef>(module, RequireUInt(parameters, "token"))),
				"assembly" or "module" or "namespace" or "type" =>
					throw new RpcException(-32602, "Decompile kind not allowed. Use decompileMethod/decompileField/decompileProperty/decompileEvent."),
				_ => throw new RpcException(-32602, $"Unknown kind: {kind}"),
			};
		}

		JToken DecompileMember<T>(JObject? parameters) where T : class, IMemberDef {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var token = RequireUInt(parameters, "token");
			var member = ResolveMember<T>(module, token);
			return DecompileResolved(member);
		}

		JToken DecompileType(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var token = RequireUInt(parameters, "typeToken");
			var type = ResolveMember<TypeDef>(module, token);
			return DecompileResolved(type);
		}

		JToken GetMethodSignature(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var method = ResolveMember<MethodDef>(module, RequireUInt(parameters, "token"));
			return MethodSignatureToJson(method);
		}

		JToken DecompileResolved(IMemberDef member) {
			var decompiler = decompilerService.Decompiler;
			var output = new StringBuilderDecompilerOutput();
			var ctx = new DecompilationContext();
			switch (member) {
			case MethodDef method:
				decompiler.Decompile(method, output, ctx);
				break;
			case FieldDef field:
				decompiler.Decompile(field, output, ctx);
				break;
			case PropertyDef property:
				decompiler.Decompile(property, output, ctx);
				break;
			case EventDef ev:
				decompiler.Decompile(ev, output, ctx);
				break;
			case TypeDef type:
				decompiler.Decompile(type, output, ctx);
				break;
			default:
				throw new RpcException(-32602, "Unsupported member type");
			}
			return new JObject {
				["language"] = decompiler.UniqueNameUI,
				["text"] = output.ToString(),
			};
		}

		JToken GetFieldInfo(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var field = ResolveMember<FieldDef>(module, RequireUInt(parameters, "token"));
			return new JObject {
				["name"] = Utf8ToString(field.Name),
				["fullName"] = field.FullName,
				["fieldType"] = field.FieldType.FullName,
				["isStatic"] = field.IsStatic,
				["isInitOnly"] = field.IsInitOnly,
				["isLiteral"] = field.IsLiteral,
				["hasConstant"] = field.Constant is not null,
				["constantValue"] = field.Constant?.Value is null ? JValue.CreateNull() : JToken.FromObject(field.Constant.Value),
				["attributes"] = field.Attributes.ToString(),
				["token"] = (uint)field.MDToken.Raw,
				["moduleMvid"] = FormatMvid(module.Mvid),
			};
		}

		JToken GetEnumInfo(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			if (!type.IsEnum)
				throw new RpcException(-32602, "Type is not an enum");

			var values = new JArray();
			foreach (var field in type.Fields.Where(f => f.IsStatic && f.HasConstant && f.Name != "value__")) {
				values.Add(new JObject {
					["name"] = Utf8ToString(field.Name),
					["value"] = field.Constant?.Value is null ? JValue.CreateNull() : JToken.FromObject(field.Constant.Value),
					["token"] = (uint)field.MDToken.Raw,
				});
			}

			return new JObject {
				["name"] = Utf8ToString(type.Name),
				["fullName"] = type.FullName,
				["underlyingType"] = type.GetEnumUnderlyingType()?.FullName ?? string.Empty,
				["values"] = values,
				["token"] = (uint)type.MDToken.Raw,
				["moduleMvid"] = FormatMvid(module.Mvid),
			};
		}

		JToken GetStructInfo(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			if (!type.IsValueType || type.IsEnum)
				throw new RpcException(-32602, "Type is not a struct");

			var fields = new JArray(type.Fields.Select(f => new JObject {
				["name"] = Utf8ToString(f.Name),
				["fieldType"] = f.FieldType.FullName,
				["isStatic"] = f.IsStatic,
				["token"] = (uint)f.MDToken.Raw,
			}));

			return new JObject {
				["name"] = Utf8ToString(type.Name),
				["fullName"] = type.FullName,
				["isExplicitLayout"] = type.IsExplicitLayout,
				["isSequentialLayout"] = type.IsSequentialLayout,
				["packingSize"] = type.ClassLayout?.PackingSize ?? 0,
				["classSize"] = type.ClassLayout?.ClassSize ?? 0,
				["fields"] = fields,
				["token"] = (uint)type.MDToken.Raw,
				["moduleMvid"] = FormatMvid(module.Mvid),
			};
		}

		JToken GetInterfaceInfo(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			if (!type.IsInterface)
				throw new RpcException(-32602, "Type is not an interface");

			var methods = new JArray(type.Methods.Select(m => new JObject {
				["name"] = Utf8ToString(m.Name),
				["fullName"] = m.FullName,
				["token"] = (uint)m.MDToken.Raw,
			}));
			var properties = new JArray(type.Properties.Select(p => new JObject {
				["name"] = Utf8ToString(p.Name),
				["fullName"] = p.FullName,
				["token"] = (uint)p.MDToken.Raw,
			}));
			var events = new JArray(type.Events.Select(e => new JObject {
				["name"] = Utf8ToString(e.Name),
				["fullName"] = e.FullName,
				["token"] = (uint)e.MDToken.Raw,
			}));
			var baseInterfaces = new JArray(type.Interfaces.Select(i => i.Interface?.FullName ?? string.Empty));

			return new JObject {
				["name"] = Utf8ToString(type.Name),
				["fullName"] = type.FullName,
				["baseInterfaces"] = baseInterfaces,
				["methods"] = methods,
				["properties"] = properties,
				["events"] = events,
				["token"] = (uint)type.MDToken.Raw,
				["moduleMvid"] = FormatMvid(module.Mvid),
			};
		}

		JToken GetTypeDependencies(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			var deps = new HashSet<string>(StringComparer.Ordinal);
			CollectTypeDependencies(type, deps);
			var list = deps.OrderBy(s => s, StringComparer.Ordinal).ToArray();
			return new JArray(list);
		}

		JToken GetInheritanceTree(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var type = ResolveMember<TypeDef>(module, RequireUInt(parameters, "typeToken"));
			var maxDepth = GetInt(parameters, "maxDepth", 3);
			if (maxDepth < 1)
				maxDepth = 1;

			var baseChain = new JArray();
			var current = type.BaseType?.ResolveTypeDef();
			while (current is not null) {
				baseChain.Add(current.FullName);
				current = current.BaseType?.ResolveTypeDef();
			}

			var allTypes = GetAllTypes();
			var derivedMap = BuildDerivedTypeMap(allTypes);
			var derived = new JArray();
			var visited = new HashSet<string>(StringComparer.Ordinal);
			var queue = new Queue<(string FullName, int Depth)>();
			queue.Enqueue((type.FullName, 0));
			visited.Add(type.FullName);

			while (queue.Count > 0) {
				var (fullName, depth) = queue.Dequeue();
				if (depth >= maxDepth)
					continue;
				if (!derivedMap.TryGetValue(fullName, out var children))
					continue;
				foreach (var child in children) {
					if (!visited.Add(child.FullName))
						continue;
					derived.Add(new JObject {
						["fullName"] = child.FullName,
						["depth"] = depth + 1,
						["token"] = (uint)child.MDToken.Raw,
						["moduleMvid"] = FormatMvid(child.Module?.Mvid),
					});
					queue.Enqueue((child.FullName, depth + 1));
				}
			}

			return new JObject {
				["baseChain"] = baseChain,
				["derivedTypes"] = derived,
			};
		}

		JToken FindPathToType(JObject? parameters) {
			var fromModule = FindModule(RequireGuid(parameters, "fromModuleMvid"));
			var fromType = ResolveMember<TypeDef>(fromModule, RequireUInt(parameters, "fromTypeToken"));
			var maxDepth = GetInt(parameters, "maxDepth", 4);
			if (maxDepth < 1)
				maxDepth = 1;

			TypeDef? toType = null;
			var toToken = GetUInt(parameters, "toTypeToken");
			if (toToken.HasValue) {
				var toModule = FindModule(RequireGuid(parameters, "toModuleMvid"));
				toType = ResolveMember<TypeDef>(toModule, toToken.Value);
			}
			else {
				var toFullName = RequireString(parameters, "toTypeFullName");
				toType = FindTypeByFullName(toFullName);
			}

			if (toType is null)
				throw new RpcException(-32602, "Target type not found");

			var typeIndex = BuildTypeIndex();
			var comparer = new TypeDefRefComparer();
			var visited = new HashSet<TypeDef>(comparer);
			var prev = new Dictionary<TypeDef, PathEntry>(comparer);
			var depthMap = new Dictionary<TypeDef, int>(comparer);
			var queue = new Queue<TypeDef>();

			queue.Enqueue(fromType);
			visited.Add(fromType);
			depthMap[fromType] = 0;

			while (queue.Count > 0) {
				var current = queue.Dequeue();
				if (ReferenceEquals(current, toType))
					break;
				var depth = depthMap[current];
				if (depth >= maxDepth)
					continue;

				foreach (var edge in EnumerateTypeEdges(current, typeIndex)) {
					if (visited.Contains(edge.Target))
						continue;
					visited.Add(edge.Target);
					prev[edge.Target] = new PathEntry(current, edge.ViaKind, edge.ViaName);
					depthMap[edge.Target] = depth + 1;
					queue.Enqueue(edge.Target);
				}
			}

			if (!visited.Contains(toType)) {
				return new JObject {
					["found"] = false,
					["path"] = new JArray(),
				};
			}

			var path = new List<JObject>();
			var cursor = toType;
			while (!ReferenceEquals(cursor, fromType)) {
				if (!prev.TryGetValue(cursor, out var entry))
					break;
				path.Add(new JObject {
					["from"] = entry.From.FullName,
					["viaKind"] = entry.ViaKind,
					["viaName"] = entry.ViaName,
					["to"] = cursor.FullName,
				});
				cursor = entry.From;
			}

			path.Reverse();
			return new JObject {
				["found"] = true,
				["path"] = new JArray(path),
				["depth"] = path.Count,
			};
		}

		JToken FindReferences(JObject? parameters) {
			var kind = RequireString(parameters, "kind").ToLowerInvariant();
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var token = RequireUInt(parameters, "token");
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			var modules = BuildModuleIndex().Values.ToList();

			switch (kind) {
			case "method": {
				var method = ResolveMember<MethodDef>(module, token);
				FindMethodReferences(modules, new List<MethodTarget> { new MethodTarget(method, null) }, collector);
				break;
			}
			case "field": {
				var field = ResolveMember<FieldDef>(module, token);
				FindFieldReferences(modules, field, collector);
				break;
			}
			case "property": {
				var property = ResolveMember<PropertyDef>(module, token);
				var targets = GetPropertyMethodTargets(property);
				FindMethodReferences(modules, targets, collector);
				break;
			}
			case "event": {
				var ev = ResolveMember<EventDef>(module, token);
				var targets = GetEventMethodTargets(ev);
				FindMethodReferences(modules, targets, collector);
				break;
			}
			case "type": {
				var type = ResolveMember<TypeDef>(module, token);
				FindTypeReferences(modules, type, collector);
				break;
			}
			default:
				throw new RpcException(-32602, $"Unknown kind: {kind}");
			}

			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
			};
		}

		JToken GetCallers(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var method = ResolveMember<MethodDef>(module, RequireUInt(parameters, "token"));
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			var modules = BuildModuleIndex().Values.ToList();
			FindMethodReferences(modules, new List<MethodTarget> { new MethodTarget(method, null) }, collector, callersOnly: true);
			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
			};
		}

		JToken GetCallees(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var method = ResolveMember<MethodDef>(module, RequireUInt(parameters, "token"));
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			if (method.HasBody && method.Body is not null) {
				foreach (var instr in method.Body.Instructions) {
					if (collector.TooManyResults)
						break;
					if (instr.Operand is not IMethod target)
						continue;
					var resolved = target.ResolveMethodDef();
					var name = resolved?.FullName ?? target.FullName;
					var tokenValue = resolved is null ? 0u : (uint)resolved.MDToken.Raw;
					var moduleMvid = resolved?.Module is null ? string.Empty : FormatMvid(resolved.Module.Mvid);
					if (string.IsNullOrEmpty(name))
						continue;
					collector.TryAdd($"callee:{moduleMvid}:{tokenValue}:{name}", new JObject {
						["name"] = resolved?.Name is null ? Utf8ToString(target.Name) : Utf8ToString(resolved.Name),
						["fullName"] = name,
						["token"] = tokenValue,
						["moduleMvid"] = moduleMvid,
					});
				}
			}

			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
			};
		}

		JToken Search(JObject? parameters) {
			var searchText = RequireString(parameters, "searchText");
			if (string.IsNullOrEmpty(searchText))
				return new JObject { ["results"] = new JArray(), ["tooManyResults"] = false };

			var searchKind = ParseSearchKind(GetString(parameters, "searchType") ?? "any");
			var searchLocation = GetString(parameters, "searchLocation") ?? "allFiles";
			var caseSensitive = GetBool(parameters, "caseSensitive", false);
			var matchWholeWords = GetBool(parameters, "matchWholeWords", false);
			var matchAnySearchTerm = GetBool(parameters, "matchAnySearchTerm", false);
			var searchDecompiledData = GetBool(parameters, "searchDecompiledData", true);
			var searchFrameworkAssemblies = GetBool(parameters, "searchFrameworkAssemblies", true);
			var searchCompilerGeneratedMembers = GetBool(parameters, "searchCompilerGeneratedMembers", true);
			var maxResults = GetInt(parameters, "maxResults", DefaultMaxResults);
			if (maxResults <= 0)
				maxResults = DefaultMaxResults;

			var matcher = new SearchMatcher(searchText, caseSensitive, matchWholeWords, matchAnySearchTerm);
			var results = new JArray();
			var collector = new SearchCollector(results, maxResults);
			var scope = GetSearchScope(searchLocation, searchFrameworkAssemblies);

			foreach (var moduleItem in scope.Modules) {
				if (collector.TooManyResults)
					break;
				SearchModule(moduleItem, searchKind, matcher, searchDecompiledData, searchCompilerGeneratedMembers, scope.TypeKeyFilter, collector);
			}

			return new JObject {
				["results"] = results,
				["tooManyResults"] = collector.TooManyResults,
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

		JToken GetSelectedMember() {
			var selection = documentTabService.DocumentTreeView.TreeView.TopLevelSelection.FirstOrDefault();
			if (selection is null) {
				return new JObject {
					["hasSelection"] = false,
				};
			}

			if (selection.GetAncestorOrSelf<MethodNode>() is MethodNode methodNode)
				return WrapSelected(MemberToJson("method", RequireMemberModule(methodNode.MethodDef), methodNode.MethodDef));
			if (selection.GetAncestorOrSelf<FieldNode>() is FieldNode fieldNode)
				return WrapSelected(MemberToJson("field", RequireMemberModule(fieldNode.FieldDef), fieldNode.FieldDef));
			if (selection.GetAncestorOrSelf<PropertyNode>() is PropertyNode propertyNode)
				return WrapSelected(MemberToJson("property", RequireMemberModule(propertyNode.PropertyDef), propertyNode.PropertyDef));
			if (selection.GetAncestorOrSelf<EventNode>() is EventNode eventNode)
				return WrapSelected(MemberToJson("event", RequireMemberModule(eventNode.EventDef), eventNode.EventDef));
			if (selection.GetAncestorOrSelf<TypeNode>() is TypeNode typeNode)
				return WrapSelected(TypeToJson(typeNode.TypeDef));

			return new JObject {
				["hasSelection"] = false,
			};
		}

		JToken OpenInDnSpy(JObject? parameters) {
			var module = FindModule(RequireGuid(parameters, "moduleMvid"));
			var token = RequireUInt(parameters, "token");
			var newTab = GetBool(parameters, "newTab", false);
			var reference = module.ResolveToken(token);
			if (reference is null)
				throw new RpcException(-32602, "Member not found");
			documentTabService.FollowReference(reference, newTab, true);
			return new JObject {
				["opened"] = true,
			};
		}

		JToken ExampleFlow() {
			return new JObject {
				["text"] = ExampleFlowText,
			};
		}

		const int DefaultMaxResults = 5000;

		enum SearchKind {
			Assembly,
			Module,
			Namespace,
			Type,
			Field,
			Method,
			Property,
			Event,
			Param,
			Local,
			ParamLocal,
			AssemblyRef,
			ModuleRef,
			Resource,
			Generic,
			NonGeneric,
			Enum,
			Interface,
			Class,
			Struct,
			Delegate,
			Member,
			Any,
			Literal,
		}

		sealed class ModuleSearchItem {
			public ModuleDef Module { get; }
			public string Filename { get; }
			public string AssemblyName { get; }
			public string ModuleMvid { get; }
			public string Key { get; }

			public ModuleSearchItem(ModuleDef module, string filename, string assemblyName) {
				Module = module;
				Filename = filename ?? string.Empty;
				AssemblyName = assemblyName ?? string.Empty;
				ModuleMvid = FormatMvid(module.Mvid);
				Key = GetModuleKey(module, Filename);
			}
		}

		sealed class SearchScope {
			public IReadOnlyList<ModuleSearchItem> Modules { get; }
			public HashSet<string>? TypeKeyFilter { get; }

			public SearchScope(IReadOnlyList<ModuleSearchItem> modules, HashSet<string>? typeKeyFilter) {
				Modules = modules;
				TypeKeyFilter = typeKeyFilter;
			}
		}

		sealed class SearchCollector {
			readonly JArray results;
			readonly HashSet<string> seen;
			readonly int maxResults;

			public bool TooManyResults { get; private set; }

			public SearchCollector(JArray results, int maxResults) {
				this.results = results;
				this.maxResults = maxResults;
				seen = new HashSet<string>(StringComparer.Ordinal);
			}

			public bool TryAdd(string key, JObject obj) {
				if (TooManyResults)
					return false;
				if (seen.Contains(key))
					return true;
				if (results.Count >= maxResults) {
					TooManyResults = true;
					return false;
				}
				seen.Add(key);
				results.Add(obj);
				return true;
			}
		}

		sealed class SearchMatcher {
			readonly string[] terms;
			readonly bool matchWholeWords;
			readonly bool matchAny;
			readonly StringComparison comparison;

			public SearchMatcher(string searchText, bool caseSensitive, bool matchWholeWords, bool matchAnySearchTerm) {
				terms = SplitTerms(searchText);
				this.matchWholeWords = matchWholeWords;
				matchAny = matchAnySearchTerm;
				comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
			}

			public bool IsMatch(string? text) {
				if (string.IsNullOrEmpty(text))
					return false;
				if (terms.Length == 0)
					return false;
				if (matchAny) {
					foreach (var term in terms) {
						if (ContainsTerm(text, term))
							return true;
					}
					return false;
				}

				foreach (var term in terms) {
					if (!ContainsTerm(text, term))
						return false;
				}
				return true;
			}

			bool ContainsTerm(string text, string term) {
				if (string.IsNullOrEmpty(term))
					return false;
				var index = 0;
				while (true) {
					index = text.IndexOf(term, index, comparison);
					if (index < 0)
						return false;
					if (!matchWholeWords || IsWholeWord(text, index, term.Length))
						return true;
					index += term.Length;
				}
			}

			static bool IsWholeWord(string text, int index, int length) {
				var leftOk = index == 0 || !IsWordChar(text[index - 1]);
				var rightIndex = index + length;
				var rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
				return leftOk && rightOk;
			}

			static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

			static string[] SplitTerms(string text) =>
				text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		}

		SearchScope GetSearchScope(string searchLocation, bool searchFrameworkAssemblies) {
			var moduleIndex = BuildModuleIndex();
			var location = searchLocation.ToLowerInvariant();

			switch (location) {
			case "allfiles":
				return new SearchScope(FilterModules(moduleIndex.Values, searchFrameworkAssemblies), null);
			case "selectedfiles":
				return new SearchScope(FilterModules(GetSelectedModules(moduleIndex), searchFrameworkAssemblies), null);
			case "allfilesinsamedir":
				return new SearchScope(FilterModules(GetModulesInSameDir(moduleIndex), searchFrameworkAssemblies), null);
			case "selectedtype":
				var selectedTypes = GetSelectedTypes();
				var typeKeys = new HashSet<string>(selectedTypes.Select(GetTypeKey), StringComparer.Ordinal);
				var modules = new List<ModuleSearchItem>();
				foreach (var type in selectedTypes) {
					if (type.Module is null)
						continue;
					var key = GetModuleKey(type.Module, type.Module.Location);
					if (moduleIndex.TryGetValue(key, out var item))
						modules.Add(item);
					else
						modules.Add(CreateModuleItem(type.Module));
				}
				return new SearchScope(FilterModules(UniqueModules(modules), searchFrameworkAssemblies), typeKeys);
			default:
				throw new RpcException(-32602, $"Unknown searchLocation: {searchLocation}");
			}
		}

		static List<ModuleSearchItem> FilterModules(IEnumerable<ModuleSearchItem> modules, bool searchFrameworkAssemblies) {
			if (searchFrameworkAssemblies)
				return modules.ToList();
			return modules.Where(m => !FrameworkFileUtils.IsFrameworkAssembly(m.Filename, m.AssemblyName)).ToList();
		}

		Dictionary<string, ModuleSearchItem> BuildModuleIndex() {
			var dict = new Dictionary<string, ModuleSearchItem>(StringComparer.Ordinal);
			foreach (var node in documentTabService.DocumentTreeView.GetAllModuleNodes()) {
				var module = node.Document.ModuleDef;
				if (module is null)
					continue;
				var assemblyName = Utf8ToString(node.Document.AssemblyDef?.Name);
				var item = new ModuleSearchItem(module, node.Document.Filename, assemblyName);
				dict[item.Key] = item;
			}
			return dict;
		}

		IEnumerable<ModuleSearchItem> GetSelectedModules(Dictionary<string, ModuleSearchItem> moduleIndex) {
			foreach (var node in documentTabService.DocumentTreeView.TreeView.TopLevelSelection
				.Select(a => a.GetDocumentNode())
				.Where(a => a is not null)
				.Distinct()) {
				var docNode = node!;
				var module = docNode.Document.ModuleDef;
				if (module is null)
					continue;
				var key = GetModuleKey(module, docNode.Document.Filename);
				if (moduleIndex.TryGetValue(key, out var item))
					yield return item;
				else
					yield return CreateModuleItem(module, docNode.Document.Filename, Utf8ToString(docNode.Document.AssemblyDef?.Name));
			}
		}

		IEnumerable<ModuleSearchItem> GetModulesInSameDir(Dictionary<string, ModuleSearchItem> moduleIndex) {
			var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var item in GetSelectedModules(moduleIndex)) {
				if (File.Exists(item.Filename)) {
					var dir = Path.GetDirectoryName(item.Filename);
					if (!string.IsNullOrEmpty(dir))
						dirs.Add(dir);
				}
			}

			foreach (var item in moduleIndex.Values) {
				if (!File.Exists(item.Filename))
					continue;
				var dir = Path.GetDirectoryName(item.Filename);
				if (dir is not null && dirs.Contains(dir))
					yield return item;
			}
		}

		static List<ModuleSearchItem> UniqueModules(IEnumerable<ModuleSearchItem> modules) {
			var dict = new Dictionary<string, ModuleSearchItem>(StringComparer.Ordinal);
			foreach (var item in modules) {
				if (!dict.ContainsKey(item.Key))
					dict[item.Key] = item;
			}
			return dict.Values.ToList();
		}

		List<TypeDef> GetSelectedTypes() {
			return documentTabService.DocumentTreeView.TreeView.TopLevelSelection
				.Select(a => a.GetAncestorOrSelf<TypeNode>())
				.Where(a => a is not null)
				.Select(a => a!.TypeDef)
				.Where(a => a is not null)
				.Distinct(new TypeDefRefComparer())
				.ToList();
		}

		static ModuleSearchItem CreateModuleItem(ModuleDef module, string? filename = null, string? assemblyName = null) {
			var file = filename ?? module.Location ?? string.Empty;
			var asmName = assemblyName ?? Utf8ToString(module.Assembly?.Name);
			return new ModuleSearchItem(module, file, asmName);
		}

		static string GetModuleKey(ModuleDef module, string? filename) {
			if (module.Mvid.HasValue && module.Mvid.Value != Guid.Empty)
				return module.Mvid.Value.ToString("D");
			if (!string.IsNullOrEmpty(filename))
				return $"file:{filename}";
			var name = module.FullName ?? Utf8ToString(module.Name);
			return $"module:{name}";
		}

		static string GetTypeKey(TypeDef type) {
			var mvid = type.Module is null ? string.Empty : FormatMvid(type.Module.Mvid);
			return $"{mvid}:{type.MDToken.Raw:X8}";
		}

		void SearchModule(ModuleSearchItem moduleItem, SearchKind searchKind, SearchMatcher matcher, bool searchDecompiledData, bool searchCompilerGeneratedMembers, HashSet<string>? typeKeyFilter, SearchCollector collector) {
			var module = moduleItem.Module;
			var moduleMvid = moduleItem.ModuleMvid;
			var typeRestricted = typeKeyFilter is not null && typeKeyFilter.Count > 0;

			if (!typeRestricted && SearchAssemblies(searchKind)) {
				var asm = module.Assembly;
				if (asm is not null && (matcher.IsMatch(Utf8ToString(asm.Name)) || matcher.IsMatch(asm.FullName))) {
					collector.TryAdd($"assembly:{moduleMvid}:{asm.FullName}", new JObject {
						["kind"] = "assembly",
						["name"] = Utf8ToString(asm.Name),
						["fullName"] = asm.FullName,
						["moduleMvid"] = moduleMvid,
						["documentFilename"] = moduleItem.Filename,
					});
				}
			}

			if (collector.TooManyResults)
				return;

			if (!typeRestricted && SearchModules(searchKind)) {
				if (matcher.IsMatch(Utf8ToString(module.Name)) || matcher.IsMatch(module.FullName)) {
					collector.TryAdd($"module:{moduleMvid}", new JObject {
						["kind"] = "module",
						["name"] = Utf8ToString(module.Name),
						["fullName"] = module.FullName,
						["moduleMvid"] = moduleMvid,
						["documentFilename"] = moduleItem.Filename,
					});
				}
			}

			if (collector.TooManyResults)
				return;

			if (!typeRestricted && SearchNamespaces(searchKind)) {
				foreach (var ns in module.GetTypes().Select(t => Utf8ToString(t.Namespace)).Distinct(StringComparer.Ordinal)) {
					if (matcher.IsMatch(ns)) {
						if (!collector.TryAdd($"namespace:{moduleMvid}:{ns}", new JObject {
							["kind"] = "namespace",
							["namespace"] = ns,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			if (collector.TooManyResults)
				return;

			if (!typeRestricted && SearchAssemblyRefs(searchKind)) {
				foreach (var asmRef in module.GetAssemblyRefs()) {
					if (matcher.IsMatch(Utf8ToString(asmRef.Name)) || matcher.IsMatch(asmRef.FullName)) {
						if (!collector.TryAdd($"assemblyRef:{moduleMvid}:{asmRef.FullName}", new JObject {
							["kind"] = "assemblyRef",
							["name"] = Utf8ToString(asmRef.Name),
							["fullName"] = asmRef.FullName,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			if (collector.TooManyResults)
				return;

			if (!typeRestricted && SearchModuleRefs(searchKind)) {
				foreach (var modRef in module.GetModuleRefs()) {
					if (matcher.IsMatch(Utf8ToString(modRef.Name)) || matcher.IsMatch(modRef.FullName)) {
						if (!collector.TryAdd($"moduleRef:{moduleMvid}:{modRef.FullName}", new JObject {
							["kind"] = "moduleRef",
							["name"] = Utf8ToString(modRef.Name),
							["fullName"] = modRef.FullName,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			if (collector.TooManyResults)
				return;

			if (!typeRestricted && SearchResources(searchKind)) {
				foreach (var resource in module.Resources) {
					var resourceName = Utf8ToString(resource.Name);
					if (matcher.IsMatch(resourceName)) {
						if (!collector.TryAdd($"resource:{moduleMvid}:{resourceName}", new JObject {
							["kind"] = "resource",
							["name"] = resourceName,
							["resourceType"] = resource.GetType().Name,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			var types = module.GetTypes().ToList();
			foreach (var type in types) {
				if (collector.TooManyResults)
					return;
				if (typeRestricted && !typeKeyFilter!.Contains(GetTypeKey(type)))
					continue;
				if (!searchCompilerGeneratedMembers && IsCompilerGenerated(type))
					continue;

				if (SearchTypes(searchKind) && TypeMatchesSearchKind(type, searchKind)) {
					if (searchKind != SearchKind.Literal && (matcher.IsMatch(Utf8ToString(type.Name)) || matcher.IsMatch(type.FullName))) {
						if (!collector.TryAdd($"type:{moduleMvid}:{type.MDToken.Raw:X8}", new JObject {
							["kind"] = "type",
							["name"] = Utf8ToString(type.Name),
							["fullName"] = type.FullName,
							["token"] = (uint)type.MDToken.Raw,
							["typeKind"] = GetTypeKind(type),
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
					else if (searchKind == SearchKind.Literal && searchDecompiledData && HasAttributeLiteral(type, matcher)) {
						if (!collector.TryAdd($"typeLiteral:{moduleMvid}:{type.MDToken.Raw:X8}", new JObject {
							["kind"] = "type",
							["name"] = Utf8ToString(type.Name),
							["fullName"] = type.FullName,
							["token"] = (uint)type.MDToken.Raw,
							["typeKind"] = GetTypeKind(type),
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}

				if (collector.TooManyResults)
					return;

				SearchMembers(moduleItem, type, searchKind, matcher, searchDecompiledData, searchCompilerGeneratedMembers, collector);
			}
		}

		void SearchMembers(ModuleSearchItem moduleItem, TypeDef type, SearchKind searchKind, SearchMatcher matcher, bool searchDecompiledData, bool searchCompilerGeneratedMembers, SearchCollector collector) {
			var moduleMvid = moduleItem.ModuleMvid;
			var literalOnly = searchKind == SearchKind.Literal;
			var searchMethodName = searchKind == SearchKind.Any || searchKind == SearchKind.Method || searchKind == SearchKind.Member;

			if (SearchFields(searchKind)) {
				foreach (var field in type.Fields) {
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(field))
						continue;
					var matched = !literalOnly && MatchesNameOrFullName(field, matcher);
					if (!matched && searchDecompiledData && FieldHasLiteral(field, matcher))
						matched = true;
					if (matched) {
						if (!collector.TryAdd($"field:{moduleMvid}:{field.MDToken.Raw:X8}", new JObject {
							["kind"] = "field",
							["name"] = Utf8ToString(field.Name),
							["fullName"] = field.FullName,
							["token"] = (uint)field.MDToken.Raw,
							["declaringType"] = type.FullName,
							["declaringTypeToken"] = (uint)type.MDToken.Raw,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			if (collector.TooManyResults)
				return;

			if (SearchProperties(searchKind)) {
				foreach (var prop in type.Properties) {
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(prop))
						continue;
					var matched = !literalOnly && MatchesNameOrFullName(prop, matcher);
					if (!matched && searchDecompiledData && HasAttributeLiteral(prop, matcher))
						matched = true;
					if (matched) {
						if (!collector.TryAdd($"property:{moduleMvid}:{prop.MDToken.Raw:X8}", new JObject {
							["kind"] = "property",
							["name"] = Utf8ToString(prop.Name),
							["fullName"] = prop.FullName,
							["token"] = (uint)prop.MDToken.Raw,
							["declaringType"] = type.FullName,
							["declaringTypeToken"] = (uint)type.MDToken.Raw,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			if (collector.TooManyResults)
				return;

			if (SearchEvents(searchKind)) {
				foreach (var ev in type.Events) {
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(ev))
						continue;
					var matched = !literalOnly && MatchesNameOrFullName(ev, matcher);
					if (!matched && searchDecompiledData && HasAttributeLiteral(ev, matcher))
						matched = true;
					if (matched) {
						if (!collector.TryAdd($"event:{moduleMvid}:{ev.MDToken.Raw:X8}", new JObject {
							["kind"] = "event",
							["name"] = Utf8ToString(ev.Name),
							["fullName"] = ev.FullName,
							["token"] = (uint)ev.MDToken.Raw,
							["declaringType"] = type.FullName,
							["declaringTypeToken"] = (uint)type.MDToken.Raw,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}
				}
			}

			if (collector.TooManyResults)
				return;

			if (SearchMethods(searchKind) || SearchParams(searchKind) || SearchLocals(searchKind) || searchKind == SearchKind.Literal) {
				foreach (var method in type.Methods) {
					if (!searchCompilerGeneratedMembers && IsCompilerGenerated(method))
						continue;
					var matched = searchMethodName && !literalOnly && MatchesNameOrFullName(method, matcher);
					if (!matched && searchDecompiledData && MethodBodyHasMatch(method, matcher, literalOnly))
						matched = true;
					if (matched) {
						if (!collector.TryAdd($"method:{moduleMvid}:{method.MDToken.Raw:X8}", new JObject {
							["kind"] = "method",
							["name"] = Utf8ToString(method.Name),
							["fullName"] = method.FullName,
							["token"] = (uint)method.MDToken.Raw,
							["declaringType"] = type.FullName,
							["declaringTypeToken"] = (uint)type.MDToken.Raw,
							["moduleMvid"] = moduleMvid,
							["documentFilename"] = moduleItem.Filename,
						}))
							return;
					}

					if (collector.TooManyResults)
						return;

					if (SearchParams(searchKind)) {
						foreach (var param in method.Parameters) {
							if (param.IsHiddenThisParameter || param.IsReturnTypeParameter)
								continue;
							if (matcher.IsMatch(param.Name)) {
								if (!collector.TryAdd($"param:{moduleMvid}:{method.MDToken.Raw:X8}:{param.Index}", new JObject {
									["kind"] = "param",
									["name"] = param.Name ?? string.Empty,
									["index"] = param.Index,
									["methodToken"] = (uint)method.MDToken.Raw,
									["methodFullName"] = method.FullName,
									["declaringType"] = type.FullName,
									["declaringTypeToken"] = (uint)type.MDToken.Raw,
									["moduleMvid"] = moduleMvid,
									["documentFilename"] = moduleItem.Filename,
								}))
									return;
							}
						}
					}

					if (collector.TooManyResults)
						return;

					if (SearchLocals(searchKind) && method.Body is not null) {
						foreach (var local in method.Body.Variables) {
							if (string.IsNullOrEmpty(local.Name))
								continue;
							if (matcher.IsMatch(local.Name)) {
								if (!collector.TryAdd($"local:{moduleMvid}:{method.MDToken.Raw:X8}:{local.Index}", new JObject {
									["kind"] = "local",
									["name"] = local.Name ?? string.Empty,
									["index"] = local.Index,
									["methodToken"] = (uint)method.MDToken.Raw,
									["methodFullName"] = method.FullName,
									["declaringType"] = type.FullName,
									["declaringTypeToken"] = (uint)type.MDToken.Raw,
									["moduleMvid"] = moduleMvid,
									["documentFilename"] = moduleItem.Filename,
								}))
									return;
							}
						}
					}
				}
			}
		}

		static bool SearchAssemblies(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Assembly;
		static bool SearchModules(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Module;
		static bool SearchNamespaces(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Namespace;
		static bool SearchTypes(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Type || kind == SearchKind.Generic ||
			kind == SearchKind.NonGeneric || kind == SearchKind.Enum || kind == SearchKind.Interface ||
			kind == SearchKind.Class || kind == SearchKind.Struct || kind == SearchKind.Delegate;
		static bool SearchFields(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Field || kind == SearchKind.Member || kind == SearchKind.Literal;
		static bool SearchMethods(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Method || kind == SearchKind.Member || kind == SearchKind.Literal;
		static bool SearchProperties(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Property || kind == SearchKind.Member;
		static bool SearchEvents(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Event || kind == SearchKind.Member;
		static bool SearchParams(SearchKind kind) => kind == SearchKind.Param || kind == SearchKind.ParamLocal || kind == SearchKind.Any;
		static bool SearchLocals(SearchKind kind) => kind == SearchKind.Local || kind == SearchKind.ParamLocal || kind == SearchKind.Any;
		static bool SearchAssemblyRefs(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.AssemblyRef;
		static bool SearchModuleRefs(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.ModuleRef;
		static bool SearchResources(SearchKind kind) => kind == SearchKind.Any || kind == SearchKind.Resource || kind == SearchKind.Literal;

		static bool TypeMatchesSearchKind(TypeDef type, SearchKind kind) {
			return kind switch {
				SearchKind.Generic => type.HasGenericParameters,
				SearchKind.NonGeneric => !type.HasGenericParameters,
				SearchKind.Enum => type.IsEnum,
				SearchKind.Interface => type.IsInterface,
				SearchKind.Class => type.IsClass && !type.IsDelegate,
				SearchKind.Struct => type.IsValueType && !type.IsEnum,
				SearchKind.Delegate => type.IsDelegate,
				_ => true,
			};
		}

		static bool MatchesNameOrFullName(IMemberDef member, SearchMatcher matcher) =>
			matcher.IsMatch(Utf8ToString(member.Name)) || matcher.IsMatch(member.FullName);

		static bool FieldHasLiteral(FieldDef field, SearchMatcher matcher) {
			if (field.Constant?.Value is not null && matcher.IsMatch(field.Constant.Value.ToString()))
				return true;
			return HasAttributeLiteral(field, matcher);
		}

		static bool MethodBodyHasMatch(MethodDef method, SearchMatcher matcher, bool literalOnly) {
			if (!method.HasBody)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.Operand is string s) {
					if (matcher.IsMatch(s))
						return true;
					continue;
				}
				if (!literalOnly) {
					if (instr.Operand is not null && matcher.IsMatch(instr.Operand.ToString()))
						return true;
				}
			}
			return false;
		}

		static bool HasAttributeLiteral(IHasCustomAttribute? obj, SearchMatcher matcher) {
			if (obj is null || obj.CustomAttributes.Count == 0)
				return false;
			foreach (var attr in obj.CustomAttributes) {
				foreach (var arg in attr.ConstructorArguments) {
					if (arg.Value is null)
						continue;
					if (matcher.IsMatch(arg.Value.ToString()))
						return true;
				}
				foreach (var arg in attr.NamedArguments) {
					var value = arg.Argument.Value;
					if (value is null)
						continue;
					if (matcher.IsMatch(value.ToString()))
						return true;
				}
			}
			return false;
		}

		static bool IsCompilerGenerated(TypeDef type) {
			if (HasCompilerGeneratedAttribute(type))
				return true;
			var name = Utf8ToString(type.Name);
			return name.Contains('<') && name.Contains('>');
		}

		static bool IsCompilerGenerated(IMemberDef member) {
			if (HasCompilerGeneratedAttribute(member))
				return true;
			var name = Utf8ToString(member.Name);
			return name.Contains('<') && name.Contains('>');
		}

		static bool HasCompilerGeneratedAttribute(IHasCustomAttribute? obj) {
			if (obj is null)
				return false;
			foreach (var attr in obj.CustomAttributes) {
				var attrTypeName = attr.AttributeType?.FullName;
				if (string.Equals(attrTypeName, "System.Runtime.CompilerServices.CompilerGeneratedAttribute", StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		static SearchKind ParseSearchKind(string searchType) {
			switch (searchType.ToLowerInvariant()) {
			case "assembly":
				return SearchKind.Assembly;
			case "module":
				return SearchKind.Module;
			case "namespace":
				return SearchKind.Namespace;
			case "type":
				return SearchKind.Type;
			case "field":
				return SearchKind.Field;
			case "method":
				return SearchKind.Method;
			case "property":
				return SearchKind.Property;
			case "event":
				return SearchKind.Event;
			case "param":
				return SearchKind.Param;
			case "local":
				return SearchKind.Local;
			case "paramlocal":
				return SearchKind.ParamLocal;
			case "assemblyref":
				return SearchKind.AssemblyRef;
			case "moduleref":
				return SearchKind.ModuleRef;
			case "resource":
				return SearchKind.Resource;
			case "generic":
				return SearchKind.Generic;
			case "nongeneric":
				return SearchKind.NonGeneric;
			case "enum":
				return SearchKind.Enum;
			case "interface":
				return SearchKind.Interface;
			case "class":
				return SearchKind.Class;
			case "struct":
				return SearchKind.Struct;
			case "delegate":
				return SearchKind.Delegate;
			case "member":
				return SearchKind.Member;
			case "any":
				return SearchKind.Any;
			case "literal":
				return SearchKind.Literal;
			default:
				throw new RpcException(-32602, $"Unknown searchType: {searchType}");
			}
		}

		sealed class TypeDefRefComparer : IEqualityComparer<TypeDef> {
			public bool Equals(TypeDef? x, TypeDef? y) => ReferenceEquals(x, y);
			public int GetHashCode(TypeDef obj) => RuntimeHelpers.GetHashCode(obj);
		}

		static string GetTypeKind(TypeDef type) {
			if (type.IsEnum)
				return "enum";
			if (type.IsInterface)
				return "interface";
			if (type.IsValueType)
				return "struct";
			if (type.IsClass)
				return "class";
			if (type.IsDelegate)
				return "delegate";
			return "type";
		}

		enum MemberKind {
			Any,
			Method,
			Field,
			Property,
			Event,
		}

		sealed class PatternMatcher {
			readonly Regex regex;

			PatternMatcher(Regex regex) => this.regex = regex;

			public bool IsMatch(string? text) =>
				!string.IsNullOrEmpty(text) && regex.IsMatch(text);

			public static PatternMatcher From(string pattern, bool useRegex, bool caseSensitive) {
				if (string.IsNullOrEmpty(pattern))
					pattern = "*";
				var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
				try {
					var regexPattern = useRegex ? pattern : WildcardToRegex(pattern);
					return new PatternMatcher(new Regex(regexPattern, options));
				}
				catch (ArgumentException ex) {
					throw new RpcException(-32602, $"Invalid pattern: {ex.Message}");
				}
			}

			static string WildcardToRegex(string pattern) {
				var escaped = Regex.Escape(pattern);
				return "^" + escaped.Replace("\\*", ".*").Replace("\\?", ".") + "$";
			}
		}

		readonly struct TypeEdge {
			public TypeDef Target { get; }
			public string ViaKind { get; }
			public string ViaName { get; }

			public TypeEdge(TypeDef target, string viaKind, string viaName) {
				Target = target;
				ViaKind = viaKind;
				ViaName = viaName;
			}
		}

		readonly struct PathEntry {
			public TypeDef From { get; }
			public string ViaKind { get; }
			public string ViaName { get; }

			public PathEntry(TypeDef from, string viaKind, string viaName) {
				From = from;
				ViaKind = viaKind;
				ViaName = viaName;
			}
		}

		readonly struct MethodTarget {
			public MethodDef Method { get; }
			public string? Accessor { get; }

			public MethodTarget(MethodDef method, string? accessor) {
				Method = method;
				Accessor = accessor;
			}
		}

		static readonly string ExampleFlowText =
@"HTTP JSON-RPC example:
POST http://127.0.0.1:13337/rpc
{
  ""jsonrpc"": ""2.0"",
  ""id"": 1,
  ""method"": ""listAssemblies"",
  ""params"": {}
}

Core methods:
- listAssemblies
- getAssemblyInfo { moduleMvid }
- listNamespaces { moduleMvid }
- listTypes { moduleMvid, namespace }
- listMembers { moduleMvid, typeToken }
- getTypeInfo { moduleMvid, typeToken }
- getTypeFields { moduleMvid, typeToken, pattern }
- getTypeProperty { moduleMvid, propertyToken } or { moduleMvid, typeToken, name }
- getMethodSignature { moduleMvid, token }
- decompileMethod/decompileField/decompileProperty/decompileEvent/decompileType
- searchTypes/searchMembers/searchStrings
- findReferences { kind, moduleMvid, token }
- getCallers/getCallees { moduleMvid, token }
- getTypeDependencies { moduleMvid, typeToken }
- getInheritanceTree { moduleMvid, typeToken }
- findPathToType { fromModuleMvid, fromTypeToken, toTypeFullName | toModuleMvid+toTypeToken }
- getSelectedText
- getSelectedMember
- openInDnSpy { moduleMvid, token, newTab }";

		static bool MemberKindMatches(MemberKind requested, MemberKind candidate) =>
			requested == MemberKind.Any || requested == candidate;

		static MemberKind ParseMemberKind(string value) {
			return value.ToLowerInvariant() switch {
				"method" => MemberKind.Method,
				"field" => MemberKind.Field,
				"property" => MemberKind.Property,
				"event" => MemberKind.Event,
				"any" => MemberKind.Any,
				_ => throw new RpcException(-32602, $"Unknown memberKind: {value}"),
			};
		}

		static JObject WrapSelected(JObject obj) {
			obj["hasSelection"] = true;
			return obj;
		}

		static ModuleDef RequireMemberModule(IMemberDef member) =>
			member.Module ?? throw new RpcException(-32602, "Module not found");

		static JObject TypeToJson(TypeDef type) {
			return new JObject {
				["kind"] = "type",
				["name"] = Utf8ToString(type.Name),
				["fullName"] = type.FullName,
				["token"] = (uint)type.MDToken.Raw,
				["moduleMvid"] = FormatMvid(type.Module?.Mvid),
			};
		}

		static JObject FieldInfoToJson(FieldDef field) {
			return new JObject {
				["name"] = Utf8ToString(field.Name),
				["fullName"] = field.FullName,
				["fieldType"] = field.FieldType.FullName,
				["isStatic"] = field.IsStatic,
				["isInitOnly"] = field.IsInitOnly,
				["isLiteral"] = field.IsLiteral,
				["hasConstant"] = field.Constant is not null,
				["constantValue"] = ConstantToJson(field.Constant?.Value),
				["attributes"] = field.Attributes.ToString(),
				["token"] = (uint)field.MDToken.Raw,
				["moduleMvid"] = FormatMvid(field.Module?.Mvid),
			};
		}

		static JObject PropertyInfoToJson(PropertyDef property) {
			var getter = property.GetMethod;
			var setter = property.SetMethod;
			return new JObject {
				["name"] = Utf8ToString(property.Name),
				["fullName"] = property.FullName,
				["propertyType"] = property.PropertySig?.RetType?.FullName ?? string.Empty,
				["hasGetter"] = getter is not null,
				["hasSetter"] = setter is not null,
				["getterToken"] = getter is null ? 0u : (uint)getter.MDToken.Raw,
				["setterToken"] = setter is null ? 0u : (uint)setter.MDToken.Raw,
				["attributes"] = property.Attributes.ToString(),
				["token"] = (uint)property.MDToken.Raw,
				["moduleMvid"] = FormatMvid(property.Module?.Mvid),
			};
		}

		static JObject MethodInfoToJson(MethodDef method) {
			return new JObject {
				["name"] = Utf8ToString(method.Name),
				["fullName"] = method.FullName,
				["signature"] = method.MethodSig?.ToString() ?? string.Empty,
				["isStatic"] = method.IsStatic,
				["isPublic"] = method.IsPublic,
				["isAbstract"] = method.IsAbstract,
				["isVirtual"] = method.IsVirtual,
				["isConstructor"] = method.IsConstructor,
				["token"] = (uint)method.MDToken.Raw,
				["moduleMvid"] = FormatMvid(method.Module?.Mvid),
			};
		}

		static JObject EventInfoToJson(EventDef ev) {
			return new JObject {
				["name"] = Utf8ToString(ev.Name),
				["fullName"] = ev.FullName,
				["eventType"] = ev.EventType?.FullName ?? string.Empty,
				["addToken"] = ev.AddMethod is null ? 0u : (uint)ev.AddMethod.MDToken.Raw,
				["removeToken"] = ev.RemoveMethod is null ? 0u : (uint)ev.RemoveMethod.MDToken.Raw,
				["raiseToken"] = ev.InvokeMethod is null ? 0u : (uint)ev.InvokeMethod.MDToken.Raw,
				["token"] = (uint)ev.MDToken.Raw,
				["moduleMvid"] = FormatMvid(ev.Module?.Mvid),
			};
		}

		static JObject MethodSignatureToJson(MethodDef method) {
			var parameters = new JArray();
			foreach (var param in method.Parameters) {
				if (param.IsHiddenThisParameter || param.IsReturnTypeParameter)
					continue;
				parameters.Add(ParameterInfoToJson(param));
			}

			var genericParams = new JArray(method.GenericParameters.Select(p => Utf8ToString(p.Name)));
			return new JObject {
				["name"] = Utf8ToString(method.Name),
				["fullName"] = method.FullName,
				["returnType"] = method.ReturnType?.FullName ?? string.Empty,
				["parameters"] = parameters,
				["genericParameters"] = genericParams,
				["attributes"] = method.Attributes.ToString(),
				["implAttributes"] = method.ImplAttributes.ToString(),
				["isStatic"] = method.IsStatic,
				["isPublic"] = method.IsPublic,
				["isPrivate"] = method.IsPrivate,
				["isFamily"] = method.IsFamily,
				["isAssembly"] = method.IsAssembly,
				["isAbstract"] = method.IsAbstract,
				["isVirtual"] = method.IsVirtual,
				["isConstructor"] = method.IsConstructor,
				["token"] = (uint)method.MDToken.Raw,
				["moduleMvid"] = FormatMvid(method.Module?.Mvid),
			};
		}

		static JObject ParameterInfoToJson(Parameter param) {
			var paramDef = param.ParamDef;
			return new JObject {
				["name"] = param.Name ?? paramDef?.Name ?? string.Empty,
				["index"] = param.Index,
				["type"] = param.Type?.FullName ?? string.Empty,
				["hasDefault"] = paramDef?.HasDefault ?? false,
				["defaultValue"] = ConstantToJson(paramDef?.Constant?.Value),
				["isOptional"] = paramDef?.IsOptional ?? false,
				["attributes"] = paramDef?.Attributes.ToString() ?? string.Empty,
			};
		}

		static JToken ConstantToJson(object? value) =>
			value is null ? JValue.CreateNull() : JToken.FromObject(value);

		List<TypeDef> GetAllTypes() {
			var list = new List<TypeDef>();
			foreach (var node in documentTabService.DocumentTreeView.GetAllModuleNodes()) {
				var module = node.Document.ModuleDef;
				if (module is null)
					continue;
				list.AddRange(module.GetTypes());
			}
			return list;
		}

		static Dictionary<string, List<TypeDef>> BuildDerivedTypeMap(IEnumerable<TypeDef> types) {
			var map = new Dictionary<string, List<TypeDef>>(StringComparer.Ordinal);
			foreach (var type in types) {
				var baseDef = type.BaseType?.ResolveTypeDef();
				var baseKey = baseDef?.FullName ?? type.BaseType?.FullName;
				if (string.IsNullOrEmpty(baseKey))
					continue;
				if (!map.TryGetValue(baseKey, out var list)) {
					list = new List<TypeDef>();
					map[baseKey] = list;
				}
				list.Add(type);
			}
			return map;
		}

		Dictionary<string, TypeDef> BuildTypeIndex() {
			var dict = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
			foreach (var type in GetAllTypes()) {
				if (!dict.ContainsKey(type.FullName))
					dict[type.FullName] = type;
			}
			return dict;
		}

		TypeDef? FindTypeByFullName(string fullName) {
			var index = BuildTypeIndex();
			return index.TryGetValue(fullName, out var type) ? type : null;
		}

		static IEnumerable<TypeEdge> EnumerateTypeEdges(TypeDef type, Dictionary<string, TypeDef> index) {
			foreach (var field in type.Fields) {
				foreach (var target in ResolveTypeDefs(field.FieldType, index)) {
					yield return new TypeEdge(target, "field", Utf8ToString(field.Name));
				}
			}

			foreach (var property in type.Properties) {
				foreach (var target in ResolveTypeDefs(property.PropertySig?.RetType, index)) {
					yield return new TypeEdge(target, "property", Utf8ToString(property.Name));
				}
			}
		}

		static IEnumerable<TypeDef> ResolveTypeDefs(TypeSig? sig, Dictionary<string, TypeDef> index) {
			if (sig is null)
				yield break;

			if (sig is GenericInstSig gis) {
				foreach (var t in ResolveTypeDefs(gis.GenericType, index))
					yield return t;
				foreach (var arg in gis.GenericArguments) {
					foreach (var t in ResolveTypeDefs(arg, index))
						yield return t;
				}
				yield break;
			}

			if (sig is FnPtrSig fnPtr) {
				var methodSig = fnPtr.MethodSig;
				if (methodSig is not null) {
					foreach (var t in ResolveTypeDefs(methodSig.RetType, index))
						yield return t;
					foreach (var param in methodSig.Params) {
						foreach (var t in ResolveTypeDefs(param, index))
							yield return t;
					}
				}
				yield break;
			}

			switch (sig) {
			case PtrSig ptr:
				foreach (var t in ResolveTypeDefs(ptr.Next, index))
					yield return t;
				yield break;
			case ByRefSig byRef:
				foreach (var t in ResolveTypeDefs(byRef.Next, index))
					yield return t;
				yield break;
			case SZArraySig szArray:
				foreach (var t in ResolveTypeDefs(szArray.Next, index))
					yield return t;
				yield break;
			case ArraySig array:
				foreach (var t in ResolveTypeDefs(array.Next, index))
					yield return t;
				yield break;
			case PinnedSig pinned:
				foreach (var t in ResolveTypeDefs(pinned.Next, index))
					yield return t;
				yield break;
			case ModifierSig modifier:
				foreach (var t in ResolveTypeDefs(modifier.Next, index))
					yield return t;
				yield break;
			}

			var typeRef = sig.ToTypeDefOrRef();
			var resolved = ResolveTypeDef(typeRef, index);
			if (resolved is not null)
				yield return resolved;
		}

		static TypeDef? ResolveTypeDef(ITypeDefOrRef? typeRef, Dictionary<string, TypeDef> index) {
			if (typeRef is null)
				return null;
			if (typeRef is TypeDef def)
				return def;
			var resolved = typeRef.ResolveTypeDef();
			if (resolved is not null)
				return resolved;
			return index.TryGetValue(typeRef.FullName, out var fallback) ? fallback : null;
		}

		static void CollectTypeDependencies(TypeDef type, HashSet<string> set) {
			AddTypeRef(type.BaseType, set);
			foreach (var iface in type.Interfaces)
				AddTypeRef(iface.Interface, set);
			foreach (var field in type.Fields)
				CollectTypeSig(field.FieldType, set);
			foreach (var prop in type.Properties)
				CollectTypeSig(prop.PropertySig?.RetType, set);
			foreach (var method in type.Methods) {
				CollectTypeSig(method.ReturnType, set);
				foreach (var param in method.Parameters) {
					if (param.IsHiddenThisParameter || param.IsReturnTypeParameter)
						continue;
					CollectTypeSig(param.Type, set);
				}
			}
			foreach (var attr in type.CustomAttributes)
				AddTypeRef(attr.AttributeType, set);
		}

		static void CollectTypeSig(TypeSig? sig, HashSet<string> set) {
			if (sig is null)
				return;
			if (sig is GenericInstSig gis) {
				CollectTypeSig(gis.GenericType, set);
				foreach (var arg in gis.GenericArguments)
					CollectTypeSig(arg, set);
				return;
			}
			if (sig is FnPtrSig fnPtr) {
				var methodSig = fnPtr.MethodSig;
				if (methodSig is not null) {
					CollectTypeSig(methodSig.RetType, set);
					foreach (var param in methodSig.Params)
						CollectTypeSig(param, set);
				}
				return;
			}
			switch (sig) {
			case PtrSig ptr:
				CollectTypeSig(ptr.Next, set);
				return;
			case ByRefSig byRef:
				CollectTypeSig(byRef.Next, set);
				return;
			case SZArraySig szArray:
				CollectTypeSig(szArray.Next, set);
				return;
			case ArraySig array:
				CollectTypeSig(array.Next, set);
				return;
			case PinnedSig pinned:
				CollectTypeSig(pinned.Next, set);
				return;
			case ModifierSig modifier:
				CollectTypeSig(modifier.Next, set);
				return;
			}
			AddTypeRef(sig.ToTypeDefOrRef(), set);
		}

		static void AddTypeRef(ITypeDefOrRef? typeRef, HashSet<string> set) {
			if (typeRef is null)
				return;
			if (!string.IsNullOrEmpty(typeRef.FullName))
				set.Add(typeRef.FullName);
		}

		static List<MethodTarget> GetPropertyMethodTargets(PropertyDef property) {
			var targets = new List<MethodTarget>();
			if (property.GetMethod is not null)
				targets.Add(new MethodTarget(property.GetMethod, "get"));
			if (property.SetMethod is not null)
				targets.Add(new MethodTarget(property.SetMethod, "set"));
			return targets;
		}

		static List<MethodTarget> GetEventMethodTargets(EventDef ev) {
			var targets = new List<MethodTarget>();
			if (ev.AddMethod is not null)
				targets.Add(new MethodTarget(ev.AddMethod, "add"));
			if (ev.RemoveMethod is not null)
				targets.Add(new MethodTarget(ev.RemoveMethod, "remove"));
			if (ev.InvokeMethod is not null)
				targets.Add(new MethodTarget(ev.InvokeMethod, "raise"));
			return targets;
		}

		void FindMethodReferences(IEnumerable<ModuleSearchItem> modules, List<MethodTarget> targets, SearchCollector collector, bool callersOnly = false) {
			if (targets.Count == 0)
				return;
			foreach (var moduleItem in modules) {
				foreach (var type in moduleItem.Module.GetTypes()) {
					foreach (var method in type.Methods) {
						if (collector.TooManyResults)
							return;
						if (!method.HasBody || method.Body is null)
							continue;
						var matchedInMethod = false;
						foreach (var instr in method.Body.Instructions) {
							if (collector.TooManyResults)
								return;
							if (instr.Operand is not IMethod candidate)
								continue;
							foreach (var target in targets) {
								if (!MethodMatchesTarget(candidate, target.Method))
									continue;
								var key = callersOnly
									? $"caller:{moduleItem.ModuleMvid}:{method.MDToken.Raw:X8}"
									: $"call:{moduleItem.ModuleMvid}:{method.MDToken.Raw:X8}:{instr.Offset}:{target.Method.MDToken.Raw:X8}";
								var obj = new JObject {
									["kind"] = "method",
									["referenceKind"] = "call",
									["methodToken"] = (uint)method.MDToken.Raw,
									["methodFullName"] = method.FullName,
									["declaringType"] = type.FullName,
									["declaringTypeToken"] = (uint)type.MDToken.Raw,
									["moduleMvid"] = moduleItem.ModuleMvid,
									["documentFilename"] = moduleItem.Filename,
									["targetToken"] = (uint)target.Method.MDToken.Raw,
									["targetFullName"] = target.Method.FullName,
								};
								if (!callersOnly)
									obj["ilOffset"] = instr.Offset;
								if (!string.IsNullOrEmpty(target.Accessor))
									obj["accessor"] = target.Accessor;
								collector.TryAdd(key, obj);
								matchedInMethod = true;
								if (callersOnly)
									break;
							}
							if (callersOnly && matchedInMethod)
								break;
						}
					}
				}
			}
		}

		void FindFieldReferences(IEnumerable<ModuleSearchItem> modules, FieldDef field, SearchCollector collector) {
			foreach (var moduleItem in modules) {
				foreach (var type in moduleItem.Module.GetTypes()) {
					foreach (var method in type.Methods) {
						if (collector.TooManyResults)
							return;
						if (!method.HasBody || method.Body is null)
							continue;
						foreach (var instr in method.Body.Instructions) {
							if (collector.TooManyResults)
								return;
							if (instr.Operand is not IField candidate)
								continue;
							if (!FieldMatchesTarget(candidate, field))
								continue;
							var key = $"fieldref:{moduleItem.ModuleMvid}:{method.MDToken.Raw:X8}:{instr.Offset}:{field.MDToken.Raw:X8}";
							collector.TryAdd(key, new JObject {
								["kind"] = "field",
								["referenceKind"] = "access",
								["methodToken"] = (uint)method.MDToken.Raw,
								["methodFullName"] = method.FullName,
								["declaringType"] = type.FullName,
								["declaringTypeToken"] = (uint)type.MDToken.Raw,
								["moduleMvid"] = moduleItem.ModuleMvid,
								["documentFilename"] = moduleItem.Filename,
								["targetToken"] = (uint)field.MDToken.Raw,
								["targetFullName"] = field.FullName,
								["ilOffset"] = instr.Offset,
							});
						}
					}
				}
			}
		}

		void FindTypeReferences(IEnumerable<ModuleSearchItem> modules, TypeDef target, SearchCollector collector) {
			var targetName = target.FullName;
			foreach (var moduleItem in modules) {
				foreach (var type in moduleItem.Module.GetTypes()) {
					if (collector.TooManyResults)
						return;
					var deps = new HashSet<string>(StringComparer.Ordinal);
					CollectTypeDependencies(type, deps);
					if (!deps.Contains(targetName))
						continue;
					collector.TryAdd($"typeref:{moduleItem.ModuleMvid}:{type.MDToken.Raw:X8}:{target.MDToken.Raw:X8}", new JObject {
						["kind"] = "type",
						["referenceKind"] = "dependency",
						["typeFullName"] = type.FullName,
						["typeToken"] = (uint)type.MDToken.Raw,
						["moduleMvid"] = moduleItem.ModuleMvid,
						["documentFilename"] = moduleItem.Filename,
						["targetToken"] = (uint)target.MDToken.Raw,
						["targetFullName"] = target.FullName,
					});
				}
			}
		}

		static bool MethodMatchesTarget(IMethod candidate, MethodDef target) {
			if (candidate is MethodDef def)
				return ReferenceEquals(def, target) || (def.MDToken.Raw == target.MDToken.Raw && def.Module == target.Module);
			return string.Equals(candidate.FullName, target.FullName, StringComparison.Ordinal);
		}

		static bool FieldMatchesTarget(IField candidate, FieldDef target) {
			if (candidate is FieldDef def)
				return ReferenceEquals(def, target) || (def.MDToken.Raw == target.MDToken.Raw && def.Module == target.Module);
			return string.Equals(candidate.FullName, target.FullName, StringComparison.Ordinal);
		}



		static string? GetString(JObject? parameters, string name) =>
			parameters?[name]?.Value<string>();

		static bool GetBool(JObject? parameters, string name, bool defaultValue) {
			var value = parameters?[name]?.Value<bool?>();
			return value ?? defaultValue;
		}

		static int GetInt(JObject? parameters, string name, int defaultValue) {
			var value = parameters?[name]?.Value<int?>();
			return value ?? defaultValue;
		}

		static uint? GetUInt(JObject? parameters, string name) =>
			parameters?[name]?.Value<uint?>();

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

		static string RequireStringAllowEmpty(JObject? parameters, string name) {
			var value = parameters?[name]?.Value<string>();
			if (value is null)
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
