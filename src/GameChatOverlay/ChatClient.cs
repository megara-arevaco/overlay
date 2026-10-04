using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GameChatOverlay;

public sealed record ApiMessage(string role, string content);
public sealed record ChatMessage(string Label, string Body, bool IsUser);

public sealed class ChatClient : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(90)
    };
    public const string SystemPrompt = """
        Eres un asesor de estrategia por turnos. Responde en español de forma breve y práctica,
        salvo que el usuario solicite otro idioma. Solo conoces lo que el usuario escribe:
        no ves el juego ni sabes sus reglas exactas. No inventes mecánicas, posiciones o estadísticas.
        Indica las suposiciones y pide aclaraciones cuando sean necesarias. Propón un plan concreto
        con prioridades y riesgos. El usuario toma todas las decisiones y acciones en el juego.
        """;

    public async Task<string> SendAsync(ChatSettings settings, string key,
        IReadOnlyList<ApiMessage> messages, CancellationToken cancellationToken)
    {
        Uri endpoint = SettingsStore.Validate(settings.Endpoint, settings.Model);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(CreatePayload(endpoint, settings.Model, messages)),
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Never expose the provider body: it could echo credentials or the user's prompt.
            string detail = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Revisa la clave API y los permisos del modelo en Ajustes.",
                HttpStatusCode.TooManyRequests => "Límite de solicitudes o saldo agotado. Espera y revisa tu cuenta.",
                HttpStatusCode.NotFound => "Revisa la URL completa del endpoint y el nombre del modelo.",
                _ => "El proveedor no pudo completar la solicitud. Inténtalo de nuevo."
            };
            throw new ChatException($"HTTP {(int)response.StatusCode}. {detail}");
        }
        // Keep a hung or oversized provider response bounded, including body reads.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var body = new MemoryStream();
        byte[] buffer = new byte[8192];
        int bytes;
        while ((bytes = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (body.Length + bytes > 1024 * 1024)
                throw new ChatException("La respuesta supera 1 MB. Pide una respuesta más breve.");
            body.Write(buffer, 0, bytes);
        }
        body.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
        string? text = IsResponsesEndpoint(endpoint)
            ? ExtractResponsesText(json.RootElement)
            : ExtractChatCompletionsText(json.RootElement);
        if (string.IsNullOrWhiteSpace(text))
            throw new ChatException(IsResponsesEndpoint(endpoint)
                ? "El proveedor devolvió una respuesta vacía o incompatible con Responses."
                : "El proveedor devolvió una respuesta vacía o incompatible con Chat Completions.");
        return text;
    }

    private static object CreatePayload(Uri endpoint, string model, IReadOnlyList<ApiMessage> messages)
    {
        if (!IsResponsesEndpoint(endpoint))
            return new ChatCompletionsRequest(model, messages);

        string instructions = string.Join("\n\n", messages.Where(message => message.role == "system").Select(message => message.content));
        var input = messages
            .Where(message => message.role != "system")
            .Select(message => new ResponseInputMessage(message.role, message.content))
            .ToArray();
        return new ResponsesRequest(model, instructions, input);
    }

    private static bool IsResponsesEndpoint(Uri endpoint) =>
        endpoint.AbsolutePath.TrimEnd('/').EndsWith("/responses", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractChatCompletionsText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out JsonElement message) ||
            !message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String)
            return null;
        return content.GetString();
    }

    private static string? ExtractResponsesText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out JsonElement direct) &&
            direct.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(direct.GetString()))
            return direct.GetString();

        if (!root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<string>();
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement entry in content.EnumerateArray())
            {
                if (entry.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? "");
                else if (entry.TryGetProperty("refusal", out JsonElement refusal) && refusal.ValueKind == JsonValueKind.String)
                    parts.Add(refusal.GetString() ?? "");
            }
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    public void Dispose() => _http.Dispose();

    private sealed record ChatCompletionsRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ApiMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream = false);

    private sealed record ResponsesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("instructions")] string Instructions,
        [property: JsonPropertyName("input")] IReadOnlyList<ResponseInputMessage> Input,
        [property: JsonPropertyName("store")] bool Store = false);

    private sealed record ResponseInputMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}

public sealed class ChatException(string message) : Exception(message);
