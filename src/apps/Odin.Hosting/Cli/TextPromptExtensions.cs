using Spectre.Console;

namespace Odin.Hosting.Cli;

#nullable enable

public static class TextPromptExtensions
{
    public static TextPrompt<T> OptionalDefaultValue<T>(this TextPrompt<T> prompt, T? value)
    {
        if (value == null)
        {
            return prompt;
        }

        return prompt.DefaultValue(value);
    }
}