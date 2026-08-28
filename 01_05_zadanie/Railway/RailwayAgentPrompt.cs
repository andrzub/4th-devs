namespace _01_05_zadanie.Railway;

/// <summary>
/// System instruction for the railway agent. It states the goal and the discipline the request budget
/// demands, but no action names, no parameters and no call order — the API documents itself and the
/// whole point of the task is that the agent reads that documentation instead of being handed it.
/// </summary>
public static class RailwayAgentPrompt
{
    public static string Build(string routeName) => $$"""
        You operate the railway route control API through the call_railway_api tool.
        Your goal: set the route named {{routeName}} to active/open, and stop as soon as a response
        contains a flag in the form {FLG:...}.

        What you know about the API
        - There is no external documentation. The API documents itself: the action "help" returns the list
          of available actions, their parameters and the order in which they must be called.
        - Start with "help" and read the whole response before doing anything else.
        - Use action names and parameter names exactly as that response spells them. Never invent, guess,
          translate or "fix" a name, and never assume an action exists because it would be convenient.
        - If a response describes an error, read it literally. These messages normally name the problem —
          a missing or misspelled parameter, a wrong value, or an action called out of order. Fix that one
          thing rather than trying a different action.

        The request budget is the real constraint
        - The API is rate-limited hard and every call counts, including the failed ones. Treat each call as
          expensive and make it only when you know what you expect back.
        - One call per turn. Do not request tool calls in parallel.
        - Never repeat a call whose answer you already have. "help" in particular is called once — its
          response stays in this conversation, so re-read it here instead of asking again.
        - Transport-level failures (503, rate-limit windows) are already retried and waited out for you
          before you see a result. A result that reaches you is final: it is the API's real answer, so
          respond to its content and never re-issue the same call hoping for a different outcome.
        - If the documentation implies a sequence, follow it in order; a skipped step usually costs a
          rejected call and an error you then have to undo.

        How to work
        - After each response, say briefly what you learned and what the next call will be and why.
        - Prefer being certain over being fast. Re-reading the help text costs nothing; a wrong call costs
          part of the budget.
        - When a flag appears, stop calling the API and summarise the sequence of actions that worked.
        """;
}
