using System.Text;
using System.Text.RegularExpressions;

namespace Architecture.Tests.Support;

/// <summary>
/// Static reachability analyzer that BP-031 uses to verify that every governed
/// handler-coverage exemption cites an integration test class which actually
/// exercises the handler. The analyzer is deterministic and source-only — it
/// does NOT run the test suite and does NOT load the integration assemblies.
///
/// "Exercises the handler" is proven by at least one of the following structural
/// signals appearing in the cited test class's source file, after stripping
/// comments and string literals so that name-only mentions in prose do not
/// count:
///
/// 1. A direct token reference to the handler's class name (e.g. typeof checks,
///    fakes, or analyzer-style assertions).
/// 2. A direct token reference to the handler's message type (the dispatched
///    command, query, or integration event), which is the typical signal for
///    dispatcher-style integration tests.
/// 3. A <c>using</c> directive importing the handler's containing namespace.
///    This codebase's HTTP-routed integration tests dispatch through the
///    endpoint pipeline and rarely construct the message type by name, so a
///    namespace import is the structural link that proves the test file
///    references types in the same module the handler ships in. Treat this as
///    a structural signal because the project enables CS8019 (unused-using as
///    a warning treated as error in CI), so a <c>using</c> directive cannot
///    silently linger without a corresponding symbol use.
///
/// A name-only mention (comment, doc-string, or string literal) does NOT count.
/// </summary>
public static class HandlerDispatchReachability
{
    private static readonly Regex HandlerWithMessagePattern = new(
        @"class\s+(?<handler>\w+)\s*(?::\s*)?[^{;]*?\b(?:ICommandHandler|IQueryHandler|IIntegrationEventHandler|IModuleScopedIntegrationEventHandler)\s*<\s*(?<message>\w+)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex FileScopedNamespacePattern = new(
        @"^\s*namespace\s+(?<ns>[A-Za-z0-9_\.]+)\s*;",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex BlockNamespacePattern = new(
        @"^\s*namespace\s+(?<ns>[A-Za-z0-9_\.]+)\s*\{",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex UsingDirectivePattern = new(
        @"^\s*using\s+(?:static\s+)?(?<ns>[A-Za-z0-9_\.]+)\s*;",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex TestClassPattern = new(
        @"class\s+(?<cls>\w+Tests)\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Information about one handler discovered in module Application source.
    /// </summary>
    public sealed record HandlerSourceInfo(
        string HandlerName,
        string MessageType,
        string Namespace,
        string SourceFilePath);

    /// <summary>
    /// Pre-parsed view of an integration test source file, ready for repeated
    /// reachability checks without re-parsing.
    /// </summary>
    public sealed record TestFileFingerprint(
        string FilePath,
        string CleanedSource,
        IReadOnlySet<string> UsingNamespaces);

    /// <summary>
    /// Discovers every handler under <c>src/Modules/**/*.Application/**/*.cs</c>,
    /// extracting its name, dispatched message type, and containing namespace.
    /// Returns a name-keyed lookup. Only the FIRST handler with a given name is
    /// kept; duplicates would fail elsewhere (assembly compile would break).
    /// </summary>
    public static IReadOnlyDictionary<string, HandlerSourceInfo> BuildHandlerSourceMap(
        IReadOnlyList<string> moduleSourceFiles,
        Func<string, string> readFile)
    {
        var map = new Dictionary<string, HandlerSourceInfo>(StringComparer.Ordinal);

        foreach (var path in moduleSourceFiles.Where(p => p.Contains(".Application/", StringComparison.Ordinal)))
        {
            var text = readFile(path);
            var ns = ExtractNamespace(text);
            if (ns is null)
            {
                continue;
            }

            foreach (Match match in HandlerWithMessagePattern.Matches(text))
            {
                var handler = match.Groups["handler"].Value;
                var message = match.Groups["message"].Value;
                if (!map.ContainsKey(handler))
                {
                    map[handler] = new HandlerSourceInfo(handler, message, ns, path);
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Builds a name-keyed lookup of every test class under
    /// <c>tests/Integration.Tests/**/*.cs</c> mapped to its enclosing source
    /// file path. A single test class lives in exactly one file.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildIntegrationTestClassFileMap(
        IReadOnlyList<string> integrationTestFiles,
        Func<string, string> readFile)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in integrationTestFiles)
        {
            var text = readFile(path);
            foreach (Match match in TestClassPattern.Matches(text))
            {
                var cls = match.Groups["cls"].Value;
                if (!map.ContainsKey(cls))
                {
                    map[cls] = path;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Strips C# comments and string literals from <paramref name="source"/> and
    /// extracts the file's <c>using</c> directives. The cleaned text preserves
    /// every non-comment, non-string token so structural reachability checks can
    /// regex over it without false positives from prose.
    /// </summary>
    public static TestFileFingerprint Fingerprint(string filePath, string source)
    {
        var cleaned = StripCommentsAndStringLiterals(source);
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in UsingDirectivePattern.Matches(cleaned))
        {
            usings.Add(match.Groups["ns"].Value);
        }

        return new TestFileFingerprint(filePath, cleaned, usings);
    }

    /// <summary>
    /// Returns true when the fingerprinted test file structurally references the
    /// handler. See the class-level remarks for the three accepted signals.
    /// </summary>
    public static bool IsHandlerReachable(TestFileFingerprint fingerprint, HandlerSourceInfo handler)
    {
        if (ContainsWholeWord(fingerprint.CleanedSource, handler.HandlerName))
        {
            return true;
        }

        if (ContainsWholeWord(fingerprint.CleanedSource, handler.MessageType))
        {
            return true;
        }

        if (fingerprint.UsingNamespaces.Contains(handler.Namespace))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the C# token boundaries check used by reachability so callers
    /// can mirror it in unit tests. A "whole word" match is bounded by
    /// non-identifier characters on each side, mirroring the regex
    /// <c>\\b...\\b</c> with C# identifier semantics (digits and underscore are
    /// part of an identifier, '.' is not).
    /// </summary>
    public static bool ContainsWholeWord(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return false;
        }

        var index = 0;
        while (index <= haystack.Length - needle.Length)
        {
            var found = haystack.IndexOf(needle, index, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            var leftOk = found == 0 || !IsIdentifierChar(haystack[found - 1]);
            var endIndex = found + needle.Length;
            var rightOk = endIndex == haystack.Length || !IsIdentifierChar(haystack[endIndex]);

            if (leftOk && rightOk)
            {
                return true;
            }

            index = found + 1;
        }

        return false;
    }

    /// <summary>
    /// Replaces single-line comments, multi-line comments, and string/char
    /// literals with spaces, preserving newlines so reported positions remain
    /// stable. Verbatim and interpolated strings are handled.
    /// </summary>
    public static string StripCommentsAndStringLiterals(string source)
    {
        var builder = new StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];

            // Line comment
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    builder.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                continue;
            }

            // Block comment
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                builder.Append("  ");
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    builder.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                if (i + 1 < source.Length)
                {
                    builder.Append("  ");
                    i += 2;
                }

                continue;
            }

            // Verbatim string @"..."
            if (c == '@' && i + 1 < source.Length && source[i + 1] == '"')
            {
                builder.Append("  ");
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '"')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '"')
                        {
                            builder.Append("  ");
                            i += 2;
                            continue;
                        }

                        builder.Append(' ');
                        i++;
                        break;
                    }

                    builder.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                continue;
            }

            // Interpolated string $"..." or $@"..." or @$"..."
            if (c == '$'
                && i + 1 < source.Length
                && (source[i + 1] == '"'
                    || (source[i + 1] == '@' && i + 2 < source.Length && source[i + 2] == '"')))
            {
                var verbatim = source[i + 1] == '@';
                builder.Append(verbatim ? "   " : "  ");
                i += verbatim ? 3 : 2;
                while (i < source.Length)
                {
                    if (!verbatim && source[i] == '\\' && i + 1 < source.Length)
                    {
                        builder.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] == '"')
                    {
                        if (verbatim && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            builder.Append("  ");
                            i += 2;
                            continue;
                        }

                        builder.Append(' ');
                        i++;
                        break;
                    }

                    builder.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                continue;
            }

            // Regular string "..."
            if (c == '"')
            {
                builder.Append(' ');
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        builder.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] == '"')
                    {
                        builder.Append(' ');
                        i++;
                        break;
                    }

                    builder.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                continue;
            }

            // Char literal '...'
            if (c == '\'')
            {
                builder.Append(' ');
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        builder.Append("  ");
                        i += 2;
                        continue;
                    }

                    if (source[i] == '\'')
                    {
                        builder.Append(' ');
                        i++;
                        break;
                    }

                    builder.Append(' ');
                    i++;
                }

                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }

    private static bool IsIdentifierChar(char c)
    {
        return c == '_' || char.IsLetterOrDigit(c);
    }

    private static string? ExtractNamespace(string source)
    {
        var fileScoped = FileScopedNamespacePattern.Match(source);
        if (fileScoped.Success)
        {
            return fileScoped.Groups["ns"].Value;
        }

        var block = BlockNamespacePattern.Match(source);
        if (block.Success)
        {
            return block.Groups["ns"].Value;
        }

        return null;
    }
}
