using System.Text.RegularExpressions;

namespace Architecture.Tests;

public sealed class FrontendFormValidationGuardrailTests
{
    // BP-037 requires that every `<form>` rendered from web/src/** drives its fields through
    // React Hook Form with a Zod resolver, and that each input inside the form carries the
    // aria-invalid/aria-describedby wiring that announces validation state to assistive tech.
    // We enforce the rule by scanning .tsx sources as text: the governance cost of booting a
    // React host from a .NET architecture test is out of proportion to the value, and every
    // violation we care about is lexically visible in the source.

    private static readonly Regex FormOpeningTag = new(
        @"<form\b",
        RegexOptions.Compiled);

    private static readonly Regex InputOpeningTag = new(
        @"<(input|select|textarea)\b",
        RegexOptions.Compiled);

    [Fact]
    public void EveryFormFileImportsReactHookFormAndZodResolver()
    {
        // A form file must pull `useForm` from react-hook-form and `zodResolver` from
        // @hookform/resolvers/zod. The `zodResolver` import is the contract — its presence
        // guarantees that a Zod schema is backing the resolver, even when the schema lives
        // in a sibling helper module (see blogFormModels.ts).
        var offenders = new List<string>();

        foreach (var filePath in EnumerateFormBearingFiles())
        {
            var text = File.ReadAllText(filePath);

            var importsUseForm =
                text.Contains("from 'react-hook-form'", StringComparison.Ordinal)
                || text.Contains("from \"react-hook-form\"", StringComparison.Ordinal);
            var importsZodResolver =
                text.Contains("@hookform/resolvers/zod", StringComparison.Ordinal);

            if (!importsUseForm || !importsZodResolver)
            {
                offenders.Add(
                    $"{ToRepoRelative(filePath)}: file renders <form> but is missing one of [react-hook-form, @hookform/resolvers/zod] imports. "
                    + $"Imports found: useForm={importsUseForm}, zodResolver={importsZodResolver}.");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "BP-037 violation — schema-driven form imports missing:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ValidatedFormInputsCarryAriaInvalidWiring()
    {
        // A field is "validated" when the source file contains a `formState.errors.X` reference
        // (the conditional render branch that shows an error message). Every <input> whose
        // register('X') matches a validated field must carry aria-invalid so that assistive
        // tech announces validation state. Inputs bound to fields with no error branch are
        // not flagged — by convention those are always-valid fields and don't need an ARIA
        // state. This matches the pattern already in AuthPanel.tsx and BlogFeature.tsx.
        var offenders = new List<string>();

        foreach (var filePath in EnumerateFormBearingFiles())
        {
            var text = File.ReadAllText(filePath);
            var validatedFields = ExtractValidatedFieldNames(text);
            if (validatedFields.Count == 0)
            {
                continue;
            }

            foreach (var formRange in FindFormRanges(text))
            {
                var formBody = text[formRange.Start..formRange.End];
                foreach (Match match in InputOpeningTag.Matches(formBody))
                {
                    var tagRange = ExtractOpeningTag(formBody, match.Index);
                    if (tagRange is null)
                    {
                        continue;
                    }

                    var tag = formBody[tagRange.Value.Start..tagRange.Value.End];
                    var registerFieldName = ExtractRegisterFieldName(tag);
                    if (registerFieldName is null)
                    {
                        continue;
                    }

                    if (!validatedFields.Contains(registerFieldName))
                    {
                        continue;
                    }

                    if (!tag.Contains("aria-invalid", StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{ToRepoRelative(filePath)}: <{match.Groups[1].Value}> bound to validated field '{registerFieldName}' "
                            + "is missing aria-invalid. BP-037 requires every input with a Zod error branch to expose aria-invalid "
                            + "and aria-describedby so assistive tech announces validation state.");
                    }
                    else if (!tag.Contains("aria-describedby", StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{ToRepoRelative(filePath)}: <{match.Groups[1].Value}> bound to validated field '{registerFieldName}' "
                            + "has aria-invalid but is missing aria-describedby pointing at the error message.");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "BP-037 violation — validated inputs missing aria wiring:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static readonly Regex ValidatedFieldReference = new(
        @"formState\.errors\.(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex RegisterFieldReference = new(
        @"\.register\(\s*['""](?<name>[A-Za-z_][A-Za-z0-9_]*)['""]",
        RegexOptions.Compiled);

    private static HashSet<string> ExtractValidatedFieldNames(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ValidatedFieldReference.Matches(text))
        {
            set.Add(match.Groups["name"].Value);
        }
        return set;
    }

    private static string? ExtractRegisterFieldName(string tag)
    {
        var match = RegisterFieldReference.Match(tag);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static IEnumerable<string> EnumerateFormBearingFiles()
    {
        var webSrc = RepositoryFiles.PathFromRoot("web", "src");
        if (!Directory.Exists(webSrc))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(webSrc, "*.tsx", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (FormOpeningTag.IsMatch(text))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<FormRange> FindFormRanges(string text)
    {
        var index = 0;
        while (true)
        {
            var open = text.IndexOf("<form", index, StringComparison.Ordinal);
            if (open < 0)
            {
                yield break;
            }

            var close = text.IndexOf("</form>", open, StringComparison.Ordinal);
            if (close < 0)
            {
                yield break;
            }

            yield return new FormRange(open, close);
            index = close + 7;
        }
    }

    private static (int Start, int End)? ExtractOpeningTag(string text, int tagStart)
    {
        // JSX tags may contain `{...expr}`, quoted strings, and self-closing `/`. We track
        // brace and string depth so that we can find the `>` that closes this opening tag
        // and nothing before it.
        var braceDepth = 0;
        var inString = false;
        var stringQuote = '\0';

        for (var index = tagStart; index < text.Length; index++)
        {
            var current = text[index];
            if (inString)
            {
                if (current == stringQuote)
                {
                    inString = false;
                }
                continue;
            }

            switch (current)
            {
                case '"':
                case '\'':
                case '`':
                    inString = true;
                    stringQuote = current;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    break;
                case '>' when braceDepth == 0:
                    return (tagStart, index + 1);
            }
        }

        return null;
    }

    private static string ToRepoRelative(string absolutePath)
    {
        var rootWithSeparator = RepositoryFiles.Root.EndsWith(Path.DirectorySeparatorChar)
            ? RepositoryFiles.Root
            : RepositoryFiles.Root + Path.DirectorySeparatorChar;
        var relative = absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? absolutePath[rootWithSeparator.Length..]
            : absolutePath;
        return relative.Replace('\\', '/');
    }

    private readonly record struct FormRange(int Start, int End);
}
