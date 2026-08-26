using ProxyServer.Llm;
using ProxyServer.Mcp;
using ProxyServer.Mission;
using ProxyServer.Sessions;

namespace ProxyServer.Agent;

/// <summary>
/// One turn of the conversation: append the operator's message to the session history, then run
/// the tool loop until the model answers with plain text.
/// </summary>
public sealed class ProxyAgent(
    ILlmClient llm,
    McpToolGateway gateway,
    ReactorPackageGuard guard,
    ILogger<ProxyAgent> logger)
{
    private const int MaxToolIterations = 6;

    private const string FallbackReply = "Daj mi chwilę, system przesyłek się zamyślił. Napisz jeszcze raz, o którą paczkę chodzi.";

    public async Task<string> HandleAsync(ChatSession session, string operatorMessage, CancellationToken cancellationToken)
    {
        using (await session.LockAsync(cancellationToken))
        {
            session.Touch();

            // The cargo type is only ever stated by the operator, never by the packages API.
            guard.ObserveOperatorMessage(session.SessionId, operatorMessage);

            session.History.Add(Message.User(operatorMessage));

            var reply = await RunToolLoopAsync(session, cancellationToken);

            session.History.Add(Message.Assistant(reply));
            session.TrimIfNeeded();

            return reply;
        }
    }

    private async Task<string> RunToolLoopAsync(ChatSession session, CancellationToken cancellationToken)
    {
        var messages = new List<Message> { Message.System(OperatorPersona.SystemPrompt) };
        messages.AddRange(session.History);

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            var response = await llm.CompleteAsync(new LlmRequest
            {
                Messages = messages,
                Tools = [.. gateway.Tools],
                Temperature = 0.4
            }, cancellationToken);

            if (!response.HasToolCalls)
            {
                var content = response.Content;
                return string.IsNullOrWhiteSpace(content) ? FallbackReply : content.Trim();
            }

            messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));

            // Tool calls and their results belong to the session history as well — the next turn
            // needs to know which packages were already checked and what the system answered.
            session.History.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));

            foreach (var toolCall in response.ToolCalls)
            {
                var arguments = await guard.RewriteArgumentsAsync(session.SessionId, toolCall.FunctionName, toolCall.ArgumentsJson, cancellationToken);

                logger.LogInformation("[{Session}] tool {Tool} {Arguments}", session.SessionId, toolCall.FunctionName, arguments);

                var result = await gateway.InvokeAsync(toolCall.FunctionName, arguments, cancellationToken);
                guard.ObserveToolResult(toolCall.FunctionName, arguments, result);

                logger.LogInformation("[{Session}] tool {Tool} result: {Result}", session.SessionId, toolCall.FunctionName, Truncate(result, 500));

                messages.Add(Message.ToolResult(toolCall.Id, result));
                session.History.Add(Message.ToolResult(toolCall.Id, result));
            }
        }

        logger.LogWarning("[{Session}] tool loop hit the {Limit}-iteration limit", session.SessionId, MaxToolIterations);
        return FallbackReply;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
