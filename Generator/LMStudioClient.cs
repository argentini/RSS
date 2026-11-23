using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Generator;

internal sealed class LMStudioClient(
    HttpClient httpClient,
    GeneratorSettings settings)
{
    public async Task<string> CompleteAsync(
        string systemPrompt,
        string prompt,
        JsonElement responseFormat,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = settings.Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            reasoning_effort = settings.UseReasoning
                ? settings.ReasoningEffort
                : "none",
            response_format = responseFormat,
            temperature = 0,
            stream = true
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromMinutes(
            settings.TimeoutMinutes));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (response.IsSuccessStatusCode == false)
            {
                var error = await response.Content.ReadAsStringAsync(
                    timeout.Token);

                throw new HttpRequestException(
                    $"LM Studio returned {(int)response.StatusCode}: {error}");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);
            var content = new StringBuilder();
            var displayed = new StringBuilder();

            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.StartsWith(
                    "data:",
                    StringComparison.Ordinal) == false)
                    continue;

                var data = line[5..].Trim();

                if (data == "[DONE]")
                    break;

                using var chunk = JsonDocument.Parse(data);
                var delta = ReadDelta(chunk.RootElement);

                if (delta.Display.Length == 0)
                    continue;

                displayed.Append(delta.Display);
                content.Append(delta.Content);
            }

            var result = content.Length > 0
                ? content.ToString()
                : displayed.ToString();

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested == false)
        {
            throw new TimeoutException(
                $"LM Studio exceeded {settings.TimeoutMinutes} minutes.");
        }
    }

    private static ModelDelta ReadDelta(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) == false ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
            return new ModelDelta("", "");

        var choice = choices[0];

        if (choice.TryGetProperty("delta", out var delta) == false)
            return new ModelDelta("", "");

        var display = new StringBuilder();
        var content = new StringBuilder();

        foreach (var property in delta.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String ||
                property.NameEquals("content") == false &&
                property.NameEquals("reasoning") == false &&
                property.NameEquals("reasoning_content") == false &&
                property.NameEquals("refusal") == false)
                continue;

            var token = property.Value.GetString();

            if (string.IsNullOrEmpty(token))
                continue;

            display.Append(token);

            if (property.NameEquals("content"))
                content.Append(token);
        }

        return new ModelDelta(display.ToString(), content.ToString());
    }

    private sealed record ModelDelta(string Display, string Content);
}
