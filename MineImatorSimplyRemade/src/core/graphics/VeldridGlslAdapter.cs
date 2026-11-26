using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Veldrid;

namespace MineImatorSimplyRemade.core.graphics;

/// <summary>
/// Rewrites the project's OpenGL 3.3 style GLSL (loose uniforms, combined samplers, implicit
/// locations, <c>gl_VertexID</c>) into the Vulkan-flavoured GLSL 450 that Veldrid.SPIRV can compile
/// for Vulkan and Direct3D 11. Shader bodies are never edited, so one source file can keep serving
/// both renderers until the OpenGL one is removed.
/// <list type="bullet">
/// <item>Loose uniforms become members of one std140 block (<see cref="UniformBlockName"/>, set 0
/// binding 0). Members of an instance-less block are globals, so the shader body is unchanged.</item>
/// <item>Each <c>sampler2D</c> becomes a texture/sampler pair at consecutive bindings plus a macro
/// that rebuilds the combined sampler, so <c>texture(uScene, uv)</c> still compiles.</item>
/// <item><c>bool</c> uniforms are stored as <c>uint</c> (std140 booleans are not portable).</item>
/// <item>Vertex outputs and fragment inputs receive matching explicit locations by name.</item>
/// <item>With <c>flipClipY</c> the vertex stage negates clip-space Y, so render targets use
/// OpenGL's row order (row 0 is the bottom of the picture). <c>gl_FragCoord</c>, <c>texelFetch</c>
/// and UV conventions then behave exactly as they do in OpenGL.</item>
/// </list>
/// All generated declarations are placed in one header ahead of the first line of code; the
/// original declaration lines are blanked, so everything after the header keeps its line order.
/// </summary>
internal static class VeldridGlslAdapter
{
    public const string UniformBlockName = "NxUniforms";
    public const string BoolStorageSuffix = "_nx";

    /// <summary>One loose uniform inside the generated std140 block.</summary>
    public sealed class UniformMember
    {
        public required string Name { get; init; }
        public required string GlslType { get; init; }
        /// <summary>Zero for a plain value, otherwise the element count.</summary>
        public required int ArrayLength { get; init; }
        public required int Offset { get; init; }
        public required int Size { get; init; }
        /// <summary>Distance between array elements (the element size for non-arrays).</summary>
        public required int Stride { get; init; }
        /// <summary>Literal initializer from the GLSL declaration, one value per component (null when absent).</summary>
        public double[]? Default { get; init; }
        public bool IsBool => GlslType == "bool";
        public bool IsMatrix => GlslType.StartsWith("mat", StringComparison.Ordinal);
        public bool IsInteger => GlslType is "int" or "ivec2" or "ivec3" or "ivec4";
        public bool IsUnsigned => GlslType is "uint" or "uvec2" or "uvec3" or "uvec4";
        /// <summary>Scalar components of one element (matrices count columns * 4).</summary>
        public int Components => IsMatrix ? MatrixColumns(GlslType) * 4 : TypeInfo(GlslType).Components;
    }

    /// <summary>One sampler uniform, with arrays represented as separate texture bindings sharing one sampler.</summary>
    public sealed class SamplerMember
    {
        public required string Name { get; init; }
        public required string GlslType { get; init; }
        public required int TextureBinding { get; init; }
        public required int ArrayLength { get; init; }
        public int SamplerBinding => TextureBinding + ArrayLength;
        internal int TextureSlot { get; set; }
        internal int SamplerSlot { get; set; }
        public required bool Comparison { get; init; }
        public required ShaderStages Stages { get; init; }
    }

    public sealed class UniformBlock
    {
        public required string Name { get; init; }
        public required int Binding { get; init; }
        public required int Size { get; init; }
        public required IReadOnlyList<UniformMember> Members { get; init; }
        public required ShaderStages Stages { get; init; }
    }

    public sealed class Program
    {
        public required string VertexSource { get; init; }
        public required string FragmentSource { get; init; }
        public required IReadOnlyList<UniformMember> Uniforms { get; init; }
        public required IReadOnlyList<UniformBlock> UniformBlocks { get; init; }
        public required IReadOnlyList<SamplerMember> Samplers { get; init; }
        /// <summary>Byte size of the std140 block, rounded up to 16. Zero when no loose uniform exists.</summary>
        public required int UniformSize { get; init; }
        public required ShaderStages UniformStages { get; init; }
        public bool HasUniforms => UniformSize > 0;

        public ResourceLayoutDescription CreateLayoutDescription()
        {
            var elements = new List<ResourceLayoutElementDescription>();
            if (HasUniforms)
                elements.Add(new ResourceLayoutElementDescription(UniformBlockName, ResourceKind.UniformBuffer, UniformStages));
            foreach (var block in UniformBlocks)
                elements.Add(new ResourceLayoutElementDescription(block.Name, ResourceKind.UniformBuffer, block.Stages));
            foreach (var sampler in Samplers)
            {
                for (int i = 0; i < sampler.ArrayLength; i++)
                    elements.Add(new ResourceLayoutElementDescription(TextureResourceName(sampler, i), ResourceKind.TextureReadOnly, sampler.Stages));
                elements.Add(new ResourceLayoutElementDescription(sampler.Name + "_smp", ResourceKind.Sampler, sampler.Stages));
            }
            return new ResourceLayoutDescription(elements.ToArray());
        }
    }

    private sealed record Declaration(string Type, string Name, int ArrayLength, double[]? Default = null);
    private sealed record BlockInfo(string Name, IReadOnlyList<UniformMember> Members, int Size);

    /// <summary>Identifiers that are legal in GLSL 3.3 but reserved from GLSL 4.0 on.</summary>
    private static readonly string[] ReservedSinceGlsl400 =
        ["sample", "patch", "subroutine", "filter", "shared", "common", "partition", "active", "resource"];

    private sealed class StageInfo
    {
        public string Source = "";
        public readonly List<Declaration> Uniforms = new();
        public readonly List<Declaration> Samplers = new();
        public readonly List<BlockInfo> UniformBlocks = new();
    }

    private static readonly Regex UniformBlockRegex = new(
        @"(?<layout>layout\s*\((?<qualifiers>[^)]*)\)\s*)?uniform\s+(?<name>[A-Za-z_]\w*)\s*\{(?<body>[\s\S]*?)\}\s*;",
        RegexOptions.Compiled);
    private static readonly Regex UniformRegex = new(
        @"(?:^|(?<=;))\s*(?:layout\s*\([^)]*\)\s*)?uniform\s+(?:(?:lowp|mediump|highp)\s+)?(?<type>[A-Za-z_]\w*)\s+(?<names>[^;{}]+);",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex InterfaceRegex = new(
        @"^(?<indent>[ \t]*)(?:layout\s*\((?<layout>[^)]*)\)\s*)?(?<interp>(?:(?:flat|smooth|noperspective|centroid|sample|invariant)\s+)*)(?<dir>in|out)\s+(?<type>(?:(?:lowp|mediump|highp)\s+)?[A-Za-z_]\w*)\s+(?<names>[^;{}()]+);[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex InterfaceBlockRegex = new(
        @"^[ \t]*(?:layout\s*\([^)]*\)\s*)?(?:flat\s+|smooth\s+|noperspective\s+)?(?:in|out)\s+[A-Za-z_]\w*\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"^[ \t]*#version[^\n]*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MainRegex = new(@"\bvoid\s+main\s*\(\s*(?:void)?\s*\)", RegexOptions.Compiled);
    private static readonly Regex ConstIntRegex = new(
        @"(?:^[ \t]*const\s+int\s+(?<n1>\w+)\s*=\s*(?<v1>\d+)\s*;|^[ \t]*#define\s+(?<n2>\w+)\s+(?<v2>\d+)\s*$)",
        RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex DeclaratorRegex = new(
        @"^\s*(?<name>[A-Za-z_]\w*)\s*(?:\[\s*(?<len>[^\]]+?)\s*\])?\s*$", RegexOptions.Compiled);
    private static readonly Regex LocationRegex = new(@"location\s*=\s*(?<n>\d+)", RegexOptions.Compiled);

    /// <summary>Converts one vertex/fragment pair into a Veldrid-ready program.</summary>
    /// <param name="vertexSource">GL 3.3 vertex shader, with includes already expanded.</param>
    /// <param name="fragmentSource">GL 3.3 fragment shader, with includes already expanded.</param>
    /// <param name="flipClipY">Negate clip-space Y so targets are stored bottom row first, like OpenGL.</param>
    public static Program Adapt(string vertexSource, string fragmentSource, bool flipClipY, bool remapClipZ = false,
        bool disableShaderGraphs = false)
    {
        string[]? extraDefines = disableShaderGraphs ? ["NX_VELDRID_NO_SHADER_GRAPH"] : null;
        var vertex = new StageInfo { Source = Prepare(vertexSource, extraDefines) };
        var fragment = new StageInfo { Source = Prepare(fragmentSource, extraDefines) };
        Collect(vertex);
        Collect(fragment);

        // Union of loose uniforms in first-seen order. Both stages must agree on every type.
        var merged = new List<Declaration>();
        foreach (var declaration in vertex.Uniforms.Concat(fragment.Uniforms))
        {
            var existing = merged.FirstOrDefault(item => item.Name == declaration.Name);
            if (existing == null) merged.Add(declaration);
            else if (existing.Type != declaration.Type || existing.ArrayLength != declaration.ArrayLength)
                throw new NotSupportedException($"Uniform '{declaration.Name}' is declared with different types in the vertex and fragment shaders.");
        }

        var members = new List<UniformMember>();
        int cursor = 0;
        foreach (var declaration in merged)
        {
            if (declaration.Type == "bool" && declaration.ArrayLength > 0)
                throw new NotSupportedException($"Boolean uniform array '{declaration.Name}' is not supported.");
            var (size, alignment, _) = TypeInfo(declaration.Type);
            int stride = size;
            if (declaration.ArrayLength > 0)
            {
                // std140 rounds the element stride of every array up to a vec4.
                stride = RoundUp(size, 16);
                alignment = 16;
            }
            cursor = RoundUp(cursor, alignment);
            int total = declaration.ArrayLength > 0 ? stride * declaration.ArrayLength : size;
            members.Add(new UniformMember
            {
                Name = declaration.Name, GlslType = declaration.Type, ArrayLength = declaration.ArrayLength,
                Offset = cursor, Size = total, Stride = stride, Default = declaration.Default
            });
            cursor += total;
        }
        int blockSize = members.Count == 0 ? 0 : RoundUp(cursor, 16);

        var uniformBlocks = new List<UniformBlock>();
        int nextResourceBinding = members.Count > 0 ? 1 : 0;
        foreach (var (info, stage) in new[] { (vertex, ShaderStages.Vertex), (fragment, ShaderStages.Fragment) })
            foreach (var declaration in info.UniformBlocks)
            {
                int index = uniformBlocks.FindIndex(block => block.Name == declaration.Name);
                if (index >= 0)
                {
                    var existing = uniformBlocks[index];
                    if (!BlockMembersEqual(existing.Members, declaration.Members) || existing.Size != declaration.Size)
                        throw new NotSupportedException($"Uniform block '{declaration.Name}' differs between the vertex and fragment shaders.");
                    uniformBlocks[index] = new UniformBlock
                    {
                        Name = existing.Name, Binding = existing.Binding, Size = existing.Size, Members = existing.Members,
                        Stages = existing.Stages | stage
                    };
                    continue;
                }
                uniformBlocks.Add(new UniformBlock
                {
                    Name = declaration.Name, Binding = nextResourceBinding++, Size = declaration.Size,
                    Members = declaration.Members, Stages = stage
                });
            }

        var samplers = new List<SamplerMember>();
        int nextBinding = nextResourceBinding;
        int nextTextureSlot = 0, nextSamplerSlot = 0;
        foreach (var (info, stage) in new[] { (vertex, ShaderStages.Vertex), (fragment, ShaderStages.Fragment) })
            foreach (var declaration in info.Samplers)
            {
                int index = samplers.FindIndex(item => item.Name == declaration.Name);
                if (index >= 0)
                {
                    var existing = samplers[index];
                    if (existing.GlslType != declaration.Type || existing.ArrayLength != declaration.ArrayLength)
                        throw new NotSupportedException($"Sampler '{declaration.Name}' is declared with different types.");
                    samplers[index] = new SamplerMember
                    {
                        Name = existing.Name, GlslType = existing.GlslType, TextureBinding = existing.TextureBinding,
                        ArrayLength = existing.ArrayLength, TextureSlot = existing.TextureSlot, SamplerSlot = existing.SamplerSlot,
                        Comparison = existing.Comparison, Stages = existing.Stages | stage
                    };
                    continue;
                }
                samplers.Add(new SamplerMember
                {
                    Name = declaration.Name, GlslType = declaration.Type, TextureBinding = nextBinding,
                    ArrayLength = declaration.ArrayLength, TextureSlot = nextTextureSlot, SamplerSlot = nextSamplerSlot,
                    Comparison = declaration.Type.EndsWith("Shadow", StringComparison.Ordinal), Stages = stage
                });
                nextBinding += declaration.ArrayLength + 1;
                nextTextureSlot += declaration.ArrayLength;
                nextSamplerSlot++;
            }

        ShaderStages uniformStages = ShaderStages.None;
        if (vertex.Uniforms.Count > 0) uniformStages |= ShaderStages.Vertex;
        if (fragment.Uniforms.Count > 0) uniformStages |= ShaderStages.Fragment;

        string vertexOut = Rewrite(vertex, members, uniformBlocks, samplers, ShaderStages.Vertex, flipClipY, remapClipZ, null, out var outputLocations);
        string fragmentOut = Rewrite(fragment, members, uniformBlocks, samplers, ShaderStages.Fragment, false, false, outputLocations, out _);
        return new Program
        {
            VertexSource = vertexOut, FragmentSource = fragmentOut, Uniforms = members, UniformBlocks = uniformBlocks, Samplers = samplers,
            UniformSize = blockSize, UniformStages = uniformStages
        };
    }

    /// <summary>
    /// Strips comments (keeping line numbers), normalises line endings and renames identifiers that
    /// GLSL 4.5 reserves but the original GLSL 3.3 source legitimately uses (for example a function called <c>sample</c>).
    /// </summary>
    private static string Prepare(string source, IEnumerable<string>? extraDefines)
    {
        source = source.Replace("\r\n", "\n").Replace('\r', '\n');
        source = Regex.Replace(source, @"/\*.*?\*/", match => new string('\n', match.Value.Count(c => c == '\n')), RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", "");
        source = ResolveConditionals(source, extraDefines);
        var version = Regex.Match(source, @"^[ \t]*#version[ \t]+(\d+)", RegexOptions.Multiline);
        if (!version.Success || int.Parse(version.Groups[1].Value, CultureInfo.InvariantCulture) < 400)
            foreach (string word in ReservedSinceGlsl400)
                source = Regex.Replace(source, $@"\b{word}\b", "nx_" + word);
        return source;
    }

    private static string ResolveConditionals(string source, IEnumerable<string>? extraDefines)
    {
        var defines = new HashSet<string>(extraDefines ?? [], StringComparer.Ordinal);
        var output = new StringBuilder(source.Length);
        var conditions = new Stack<(bool ParentActive, bool BranchTaken, bool Active, bool ElseSeen)>();
        bool active = true;
        foreach (string line in source.Split('\n'))
        {
            var directive = Regex.Match(line, @"^[ \t]*#\s*(?<name>if|ifdef|ifndef|elif|else|endif|define|undef)\b(?<args>.*)$");
            if (!directive.Success)
            {
                output.Append(active ? line : "").Append('\n');
                continue;
            }

            string name = directive.Groups["name"].Value;
            string args = directive.Groups["args"].Value.Trim();
            switch (name)
            {
                case "ifdef":
                case "ifndef":
                case "if":
                {
                    bool condition = name switch
                    {
                        "ifdef" => defines.Contains(ReadMacroName(args)),
                        "ifndef" => !defines.Contains(ReadMacroName(args)),
                        _ => EvaluateCondition(args, defines)
                    };
                    bool branch = active && condition;
                    conditions.Push((active, branch, branch, false));
                    active = branch;
                    output.Append('\n');
                    break;
                }
                case "elif":
                {
                    if (conditions.Count == 0) throw new NotSupportedException("Preprocessor #elif has no matching #if.");
                    var frame = conditions.Pop();
                    if (frame.ElseSeen) throw new NotSupportedException("Preprocessor #elif cannot follow #else.");
                    bool condition = frame.ParentActive && !frame.BranchTaken && EvaluateCondition(args, defines);
                    frame = (frame.ParentActive, frame.BranchTaken || condition, condition, false);
                    conditions.Push(frame);
                    active = condition;
                    output.Append('\n');
                    break;
                }
                case "else":
                {
                    if (conditions.Count == 0) throw new NotSupportedException("Preprocessor #else has no matching #if.");
                    var frame = conditions.Pop();
                    if (frame.ElseSeen) throw new NotSupportedException("A preprocessor conditional has multiple #else branches.");
                    bool branch = frame.ParentActive && !frame.BranchTaken;
                    conditions.Push((frame.ParentActive, frame.BranchTaken || branch, branch, true));
                    active = branch;
                    output.Append('\n');
                    break;
                }
                case "endif":
                {
                    if (conditions.Count == 0) throw new NotSupportedException("Preprocessor #endif has no matching #if.");
                    var frame = conditions.Pop();
                    active = frame.ParentActive;
                    output.Append('\n');
                    break;
                }
                case "define" when active:
                {
                    string macro = ReadMacroName(args);
                    defines.Add(macro);
                    output.Append(line).Append('\n');
                    break;
                }
                case "undef" when active:
                    defines.Remove(ReadMacroName(args));
                    output.Append(line).Append('\n');
                    break;
                default:
                    output.Append(active ? line : "").Append('\n');
                    break;
            }
        }
        if (conditions.Count != 0) throw new NotSupportedException("A preprocessor conditional has no matching #endif.");
        return output.ToString();
    }

    private static string ReadMacroName(string text)
    {
        var match = Regex.Match(text, @"^(?<name>[A-Za-z_]\w*)");
        if (!match.Success) throw new NotSupportedException($"Cannot parse preprocessor macro '{text}'.");
        return match.Groups["name"].Value;
    }

    private static bool EvaluateCondition(string expression, HashSet<string> defines)
    {
        expression = Regex.Replace(expression, @"defined\s*(?:\(\s*(?<paren>[A-Za-z_]\w*)\s*\)|(?<bare>[A-Za-z_]\w*))",
            match => defines.Contains(match.Groups["paren"].Success ? match.Groups["paren"].Value : match.Groups["bare"].Value) ? "1" : "0");
        expression = expression.Trim();
        if (expression.StartsWith("!", StringComparison.Ordinal)) return !EvaluateCondition(expression[1..], defines);
        int or = expression.IndexOf("||", StringComparison.Ordinal);
        if (or >= 0) return EvaluateCondition(expression[..or], defines) || EvaluateCondition(expression[(or + 2)..], defines);
        int and = expression.IndexOf("&&", StringComparison.Ordinal);
        if (and >= 0) return EvaluateCondition(expression[..and], defines) && EvaluateCondition(expression[(and + 2)..], defines);
        if (expression.StartsWith('(') && expression.EndsWith(')')) return EvaluateCondition(expression[1..^1], defines);
        if (expression is "1" or "true") return true;
        if (expression is "0" or "false" or "") return false;
        string macro = ReadMacroName(expression);
        if (defines.Contains(macro)) return true;
        throw new NotSupportedException($"Cannot evaluate preprocessor condition '{expression}'.");
    }

    /// <summary>Splits a declarator list on commas that are not nested inside parentheses or brackets.</summary>
    private static List<string> SplitDeclarators(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '(' or '[') depth++;
            else if (text[i] is ')' or ']') depth--;
            else if (text[i] == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>Parses a literal uniform initializer such as <c>2.2</c>, <c>true</c> or <c>vec3(1.0, 0.5, 0.25)</c>.</summary>
    private static double[] ParseInitializer(string type, string text)
    {
        text = text.Trim();
        var (_, _, components) = TypeInfo(type);
        if (type.StartsWith("mat", StringComparison.Ordinal))
            throw new NotSupportedException($"Matrix initializer '{text}' is not supported.");
        string[] values;
        if (components == 1 && !text.Contains('('))
            values = [text];
        else
        {
            var call = Regex.Match(text, @"^(?<type>\w+)\s*\((?<args>.*)\)$", RegexOptions.Singleline);
            if (!call.Success || call.Groups["type"].Value != type)
                throw new NotSupportedException($"Cannot parse the {type} initializer '{text}'.");
            values = SplitDeclarators(call.Groups["args"].Value).ToArray();
        }
        var parsed = values.Select(value =>
        {
            string literal = value.Trim().TrimEnd('f', 'F', 'u', 'U');
            if (literal == "true") return 1.0;
            if (literal == "false") return 0.0;
            if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new NotSupportedException($"Cannot parse the literal '{value.Trim()}' in a uniform initializer.");
            return number;
        }).ToArray();
        if (parsed.Length == 1 && components > 1) return Enumerable.Repeat(parsed[0], components).ToArray();
        if (parsed.Length != components)
            throw new NotSupportedException($"The {type} initializer '{text}' needs {components} values.");
        return parsed;
    }

    private static bool IsSamplerType(string type) =>
        type.StartsWith("sampler", StringComparison.Ordinal) || type.StartsWith("isampler", StringComparison.Ordinal) ||
        type.StartsWith("usampler", StringComparison.Ordinal);

    private static void Collect(StageInfo stage)
    {
        if (InterfaceBlockRegex.IsMatch(stage.Source))
            throw new NotSupportedException("Interface blocks are not supported by the Veldrid GLSL adapter.");
        var constants = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in ConstIntRegex.Matches(stage.Source))
        {
            string name = match.Groups["n1"].Success ? match.Groups["n1"].Value : match.Groups["n2"].Value;
            string value = match.Groups["v1"].Success ? match.Groups["v1"].Value : match.Groups["v2"].Value;
            constants[name] = int.Parse(value, CultureInfo.InvariantCulture);
        }
        foreach (Match match in UniformBlockRegex.Matches(stage.Source))
        {
            if (!match.Groups["qualifiers"].Value.Split(',').Any(value => value.Trim() == "std140"))
                throw new NotSupportedException($"Uniform block '{match.Groups["name"].Value}' must use std140 layout.");
            stage.UniformBlocks.Add(ParseUniformBlock(match.Groups["name"].Value, match.Groups["body"].Value, constants));
        }
        string looseUniformSource = UniformBlockRegex.Replace(stage.Source, "");
        foreach (Match match in UniformRegex.Matches(looseUniformSource))
        {
            string type = match.Groups["type"].Value;
            foreach (string rawDeclarator in SplitDeclarators(match.Groups["names"].Value))
            {
                string declarator = rawDeclarator;
                string? initializer = null;
                int equals = rawDeclarator.IndexOf('=');
                if (equals >= 0)
                {
                    declarator = rawDeclarator[..equals];
                    initializer = rawDeclarator[(equals + 1)..];
                }
                var parsed = DeclaratorRegex.Match(declarator);
                if (!parsed.Success)
                    throw new NotSupportedException($"Cannot parse uniform declarator '{rawDeclarator.Trim()}'.");
                int length = 0;
                if (parsed.Groups["len"].Success)
                {
                    string text = parsed.Groups["len"].Value;
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out length) &&
                        !constants.TryGetValue(text, out length))
                        throw new NotSupportedException($"Cannot resolve the array size '{text}'.");
                    if (length <= 0) throw new NotSupportedException("Uniform arrays need a positive size.");
                }
                var declaration = new Declaration(type, parsed.Groups["name"].Value, length);
                if (IsSamplerType(type))
                {
                    if (initializer != null) throw new NotSupportedException($"Sampler '{declaration.Name}' cannot have an initializer.");
                    SamplerTypes(type);
                    stage.Samplers.Add(declaration with { ArrayLength = Math.Max(1, length) });
                }
                else
                {
                    TypeInfo(type);
                    if (initializer != null)
                    {
                        if (length > 0)
                            throw new NotSupportedException($"Uniform array initializer '{rawDeclarator.Trim()}' is not supported.");
                        declaration = declaration with { Default = ParseInitializer(type, initializer) };
                    }
                    stage.Uniforms.Add(declaration);
                }
            }
        }
    }

    private static BlockInfo ParseUniformBlock(string name, string body, Dictionary<string, int> constants)
    {
        var members = new List<UniformMember>();
        int cursor = 0;
        foreach (string statement in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = statement.Trim();
            if (trimmed.Length == 0) continue;
            var declaration = Regex.Match(trimmed, @"^(?<type>[A-Za-z_]\w*)\s+(?<names>.+)$");
            if (!declaration.Success)
                throw new NotSupportedException($"Cannot parse a member of uniform block '{name}': '{trimmed}'.");
            string type = declaration.Groups["type"].Value;
            var (size, alignment, _) = TypeInfo(type);
            foreach (string rawDeclarator in SplitDeclarators(declaration.Groups["names"].Value))
            {
                var parsed = DeclaratorRegex.Match(rawDeclarator);
                if (!parsed.Success)
                    throw new NotSupportedException($"Cannot parse uniform block member '{rawDeclarator.Trim()}'.");
                int length = 0;
                if (parsed.Groups["len"].Success)
                {
                    string text = parsed.Groups["len"].Value;
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out length) &&
                        !constants.TryGetValue(text, out length))
                        throw new NotSupportedException($"Cannot resolve uniform block array size '{text}'.");
                    if (length <= 0) throw new NotSupportedException("Uniform block arrays need a positive size.");
                }
                int stride = length > 0 ? RoundUp(size, 16) : size;
                int memberAlignment = length > 0 ? 16 : alignment;
                cursor = RoundUp(cursor, memberAlignment);
                int memberSize = length > 0 ? stride * length : size;
                members.Add(new UniformMember
                {
                    Name = parsed.Groups["name"].Value, GlslType = type, ArrayLength = length,
                    Offset = cursor, Size = memberSize, Stride = stride
                });
                cursor += memberSize;
            }
        }
        if (members.Count == 0) throw new NotSupportedException($"Uniform block '{name}' is empty.");
        return new BlockInfo(name, members, RoundUp(cursor, 16));
    }

    private static bool BlockMembersEqual(IReadOnlyList<UniformMember> left, IReadOnlyList<UniformMember> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.GlslType == pair.Second.GlslType &&
            pair.First.ArrayLength == pair.Second.ArrayLength && pair.First.Offset == pair.Second.Offset &&
            pair.First.Size == pair.Second.Size && pair.First.Stride == pair.Second.Stride);

    private static string TextureResourceName(SamplerMember sampler, int element) =>
        sampler.ArrayLength == 1 ? sampler.Name + "_tex" : $"{sampler.Name}_tex_{element}";

    private static string Rewrite(StageInfo stage, IReadOnlyList<UniformMember> members, IReadOnlyList<UniformBlock> uniformBlocks,
        IReadOnlyList<SamplerMember> samplers,
        ShaderStages shaderStage, bool flipClipY, bool remapClipZ, Dictionary<string, int>? inputLocations,
        out Dictionary<string, int> outputLocations)
    {
        string source = stage.Source;

        // Resources: blank the original declarations and collect replacements for the header.
        var header = new StringBuilder();
        if (stage.Uniforms.Count > 0)
            header.Append(BlockDeclaration(members));
        foreach (var declaration in stage.Samplers)
        {
            var sampler = samplers.First(item => item.Name == declaration.Name);
            var (textureType, samplerType) = SamplerTypes(declaration.Type);
            for (int i = 0; i < sampler.ArrayLength; i++)
                header.Append($"layout(set = 0, binding = {sampler.TextureBinding + i}) uniform {textureType} {TextureResourceName(sampler, i)};\n");
            header.Append($"layout(set = 0, binding = {sampler.SamplerBinding}) uniform {samplerType} {declaration.Name}_smp;\n");
            if (sampler.ArrayLength == 1)
                header.Append($"#define {declaration.Name} {declaration.Type}({declaration.Name}_tex, {declaration.Name}_smp)\n");
        }
        foreach (var declaration in stage.Uniforms.Where(item => item.Type == "bool"))
            header.Append($"#define {declaration.Name} ({declaration.Name}{BoolStorageSuffix} != 0u)\n");
        source = UniformRegex.Replace(source, "");
        source = UniformBlockRegex.Replace(source, match =>
        {
            var block = uniformBlocks.First(item => item.Name == match.Groups["name"].Value);
            return $"layout(set = 0, binding = {block.Binding}, std140) uniform {block.Name} {{{match.Groups["body"].Value}}};";
        });
        foreach (var sampler in stage.Samplers.Where(item => item.ArrayLength > 1))
        {
            var member = samplers.First(item => item.Name == sampler.Name);
            var indexedSampler = new Regex($@"\b{Regex.Escape(sampler.Name)}\s*\[\s*(?<index>\d+)\s*\]");
            source = indexedSampler.Replace(source, match =>
            {
                int index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
                if (index >= sampler.ArrayLength)
                    throw new NotSupportedException($"Sampler array '{sampler.Name}' is indexed out of range.");
                return $"{sampler.Type}({TextureResourceName(member, index)}, {sampler.Name}_smp)";
            });
            if (Regex.IsMatch(source, $@"\b{Regex.Escape(sampler.Name)}\s*\["))
                throw new NotSupportedException($"Sampler array '{sampler.Name}' requires literal indices.");
        }
        source = RewriteSamplerFunctions(source);

        source = Regex.Replace(source, @"\bgl_VertexID\b", "gl_VertexIndex");
        source = Regex.Replace(source, @"\bgl_InstanceID\b", "gl_InstanceIndex");

        // Interface variables get explicit locations. Vertex outputs define them, fragment inputs reuse them by name.
        var vertexOutputs = new Dictionary<string, int>(StringComparer.Ordinal);
        outputLocations = vertexOutputs;
        var reserved = new HashSet<int>();
        foreach (Match match in InterfaceRegex.Matches(source))
        {
            var explicitLocation = LocationRegex.Match(match.Groups["layout"].Value);
            if (explicitLocation.Success)
                reserved.Add(int.Parse(explicitLocation.Groups["n"].Value, CultureInfo.InvariantCulture));
        }
        int nextLocation = 0, nextFragmentOutput = 0;
        int NextLocation(int count)
        {
            while (Enumerable.Range(nextLocation, count).Any(reserved.Contains)) nextLocation++;
            int result = nextLocation;
            for (int i = 0; i < count; i++) reserved.Add(result + i);
            nextLocation += count;
            return result;
        }

        source = InterfaceRegex.Replace(source, match =>
        {
            string direction = match.Groups["dir"].Value;
            string layout = match.Groups["layout"].Success ? match.Groups["layout"].Value : "";
            string interpolation = match.Groups["interp"].Value;
            string type = match.Groups["type"].Value;
            var declarations = new List<string>();
            foreach (string declarator in match.Groups["names"].Value.Split(','))
            {
                var parsed = DeclaratorRegex.Match(declarator);
                if (!parsed.Success) throw new NotSupportedException($"Cannot parse declarator '{declarator.Trim()}'.");
                string name = parsed.Groups["name"].Value;
                int arrayLength = 1;
                if (parsed.Groups["len"].Success &&
                    !int.TryParse(parsed.Groups["len"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out arrayLength))
                    throw new NotSupportedException($"Cannot resolve the interface array size in '{declarator.Trim()}'.");
                string arraySuffix = parsed.Groups["len"].Success ? $"[{parsed.Groups["len"].Value}]" : "";
                int count = arrayLength * MatrixColumnsOrOne(type);
                var explicitLocation = LocationRegex.Match(layout);
                string remaining = LocationRegex.Replace(layout, "").Trim(' ', ',', '\t');
                int location;
                if (explicitLocation.Success)
                    location = int.Parse(explicitLocation.Groups["n"].Value, CultureInfo.InvariantCulture);
                else if (direction == "out" && shaderStage == ShaderStages.Fragment)
                {
                    location = nextFragmentOutput;
                    nextFragmentOutput += count;
                }
                else if (direction == "in" && shaderStage == ShaderStages.Fragment &&
                         inputLocations != null && inputLocations.TryGetValue(name, out int known))
                    location = known;
                else
                    location = NextLocation(count);
                if (direction == "out" && shaderStage == ShaderStages.Vertex)
                    vertexOutputs[name] = location;
                string qualifiers = remaining.Length > 0 ? $"location = {location}, {remaining}" : $"location = {location}";
                declarations.Add($"layout({qualifiers}) {interpolation}{direction} {type} {name}{arraySuffix};");
            }
            return match.Groups["indent"].Value + string.Join(" ", declarations);
        });

        if ((flipClipY || remapClipZ) && shaderStage == ShaderStages.Vertex)
        {
            if (!MainRegex.IsMatch(source))
                throw new NotSupportedException("The vertex shader has no main function to wrap.");
            source = MainRegex.Replace(source, "void nx_main()", 1);
            source += "\nvoid main() { nx_main();" + (flipClipY ? " gl_Position.y = -gl_Position.y;" : "") +
                (remapClipZ ? " gl_Position.z = (gl_Position.z + gl_Position.w) * 0.5;" : "") + " }\n";
        }

        source = InsertHeader(source, header.ToString());
        return source;
    }

    private static readonly Regex FunctionSignatureRegex = new(
        @"^(?<indent>[ \t]*)(?<ret>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)\s*\((?<params>[^()]*)\)(?<tail>\s*\{?[ \t]*)$",
        RegexOptions.Compiled);
    private static readonly Regex SamplerParameterRegex = new(
        @"^\s*(?:(?:in|const|lowp|mediump|highp)\s+)*(?<type>[iu]?sampler\w+)\s+(?<name>[A-Za-z_]\w*)\s*$", RegexOptions.Compiled);

    private sealed record SamplerFunction(string Name, int LineIndex, List<(int Index, string Type, string Name)> Samplers,
        int BodyStart, int BodyEnd);

    /// <summary>
    /// Vulkan GLSL cannot pass a constructed combined sampler to a function. Every user function with a sampler
    /// parameter therefore takes a texture and a sampler instead, calls pass both parts, and the combined sampler
    /// is rebuilt at its point of use inside the body, e.g. <c>texture(tex, uv)</c> becomes
    /// <c>texture(sampler2D(tex_tex, tex_smp), uv)</c>. Signatures and calls must each sit on one line.
    /// </summary>
    private static string RewriteSamplerFunctions(string source)
    {
        var lines = source.Split('\n');
        var functions = new List<SamplerFunction>();
        for (int i = 0; i < lines.Length; i++)
        {
            var match = FunctionSignatureRegex.Match(lines[i]);
            if (!match.Success) continue;
            var samplers = new List<(int, string, string)>();
            var parameters = SplitDeclarators(match.Groups["params"].Value);
            for (int p = 0; p < parameters.Count; p++)
            {
                var parameter = SamplerParameterRegex.Match(parameters[p]);
                if (parameter.Success) samplers.Add((p, parameter.Groups["type"].Value, parameter.Groups["name"].Value));
            }
            if (samplers.Count == 0) continue;

            // The body runs from the first opening brace to its matching closing brace.
            int depth = 0, start = -1, end = -1;
            for (int line = i; line < lines.Length && end < 0; line++)
                foreach (char c in lines[line])
                {
                    if (c == '{') { if (depth++ == 0) start = line; }
                    else if (c == '}' && --depth == 0) { end = line; break; }
                }
            if (start < 0 || end < 0) throw new NotSupportedException($"Cannot find the body of '{match.Groups["name"].Value}'.");
            functions.Add(new SamplerFunction(match.Groups["name"].Value, i, samplers, start, end));
        }
        if (functions.Count == 0) return source;

        // Calls first, so pass-through arguments are split before body identifiers are expanded.
        var signatureLines = functions.Select(function => function.LineIndex).ToHashSet();
        for (int i = 0; i < lines.Length; i++)
        {
            if (signatureLines.Contains(i)) continue;
            foreach (var function in functions)
                lines[i] = RewriteCalls(lines[i], function);
        }
        foreach (var function in functions)
        {
            var match = FunctionSignatureRegex.Match(lines[function.LineIndex]);
            var parameters = SplitDeclarators(match.Groups["params"].Value);
            var rewritten = new List<string>();
            for (int p = 0; p < parameters.Count; p++)
            {
                var sampler = function.Samplers.FirstOrDefault(item => item.Index == p);
                if (sampler.Name == null) { rewritten.Add(parameters[p].Trim()); continue; }
                var (textureType, samplerType) = SamplerTypes(sampler.Type);
                rewritten.Add($"{textureType} {sampler.Name}_tex, {samplerType} {sampler.Name}_smp");
            }
            lines[function.LineIndex] =
                $"{match.Groups["indent"].Value}{match.Groups["ret"].Value} {function.Name}({string.Join(", ", rewritten)}){match.Groups["tail"].Value}";
            for (int line = function.LineIndex + 1; line <= function.BodyEnd; line++)
                foreach (var (_, type, name) in function.Samplers)
                    lines[line] = Regex.Replace(lines[line], $@"\b{name}\b", $"{type}({name}_tex, {name}_smp)");
        }
        return string.Join('\n', lines);
    }

    /// <summary>Replaces each sampler argument of calls to <paramref name="function"/> with its texture and sampler parts.</summary>
    private static string RewriteCalls(string line, SamplerFunction function)
    {
        var call = new Regex($@"\b{function.Name}\s*\(");
        int search = 0;
        while (true)
        {
            var match = call.Match(line, search);
            if (!match.Success) return line;
            int open = match.Index + match.Length;
            int depth = 1, close = open;
            for (; close < line.Length && depth > 0; close++)
            {
                if (line[close] is '(' or '[') depth++;
                else if (line[close] is ')' or ']') depth--;
            }
            if (depth != 0) throw new NotSupportedException($"A call to '{function.Name}' spans several lines.");
            close--; // index of the closing parenthesis
            var arguments = SplitDeclarators(line[open..close]);
            foreach (var (index, _, _) in function.Samplers)
            {
                if (index >= arguments.Count) continue;
                string id = arguments[index].Trim();
                if (!Regex.IsMatch(id, @"^[A-Za-z_]\w*$"))
                    throw new NotSupportedException($"Sampler argument '{id}' of '{function.Name}' must be a plain sampler name.");
                arguments[index] = $"{id}_tex, {id}_smp";
            }
            string replacement = string.Join(", ", arguments.Select(argument => argument.Trim()));
            line = line[..open] + replacement + line[close..];
            search = open + replacement.Length;
        }
    }

    /// <summary>Puts generated declarations after the leading preprocessor lines, ahead of the first code line.</summary>
    private static string InsertHeader(string source, string header)
    {
        var lines = source.Split('\n').ToList();
        int versionIndex = lines.FindIndex(line => line.TrimStart().StartsWith("#version", StringComparison.Ordinal));
        if (versionIndex >= 0) lines[versionIndex] = "#version 450";
        else lines.Insert(0, "#version 450");
        int insertAt = 0;
        while (insertAt < lines.Count)
        {
            string trimmed = lines[insertAt].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) insertAt++;
            else break;
        }
        if (header.Length > 0)
            lines.Insert(insertAt, header.TrimEnd('\n'));
        return string.Join('\n', lines);
    }

    private static string BlockDeclaration(IReadOnlyList<UniformMember> members)
    {
        var text = new StringBuilder();
        text.Append($"layout(set = 0, binding = 0, std140) uniform {UniformBlockName} {{\n");
        foreach (var member in members)
        {
            string type = member.IsBool ? "uint" : member.GlslType;
            string name = member.IsBool ? member.Name + BoolStorageSuffix : member.Name;
            text.Append(member.ArrayLength > 0 ? $"    {type} {name}[{member.ArrayLength}];\n" : $"    {type} {name};\n");
        }
        text.Append("};\n");
        return text.ToString();
    }

    private static int RoundUp(int value, int multiple) => (value + multiple - 1) / multiple * multiple;

    private static int MatrixColumns(string type) => type switch { "mat2" => 2, "mat3" => 3, "mat4" => 4, _ => 1 };

    private static int MatrixColumnsOrOne(string type) => MatrixColumns(type);

    /// <summary>std140 size, alignment and component count for the supported types.</summary>
    internal static (int Size, int Alignment, int Components) TypeInfo(string type) => type switch
    {
        "float" or "int" or "uint" or "bool" => (4, 4, 1),
        "vec2" or "ivec2" or "uvec2" => (8, 8, 2),
        "vec3" or "ivec3" or "uvec3" => (12, 16, 3),
        "vec4" or "ivec4" or "uvec4" => (16, 16, 4),
        "mat2" => (32, 16, 8),
        "mat3" => (48, 16, 12),
        "mat4" => (64, 16, 16),
        _ => throw new NotSupportedException($"Uniform type '{type}' is not supported by the Veldrid GLSL adapter.")
    };

    private static (string Texture, string Sampler) SamplerTypes(string type) => type switch
    {
        "sampler2D" => ("texture2D", "sampler"),
        "sampler3D" => ("texture3D", "sampler"),
        "samplerCube" => ("textureCube", "sampler"),
        "sampler2DArray" => ("texture2DArray", "sampler"),
        "sampler2DShadow" => ("texture2D", "samplerShadow"),
        "samplerCubeShadow" => ("textureCube", "samplerShadow"),
        "isampler2D" => ("itexture2D", "sampler"),
        "usampler2D" => ("utexture2D", "sampler"),
        _ => throw new NotSupportedException($"Sampler type '{type}' is not supported by the Veldrid GLSL adapter.")
    };
}
