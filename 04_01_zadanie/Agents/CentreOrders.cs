namespace _04_01_zadanie.Agents;

/// <summary>
/// The centre's own list of changes — the "what", kept apart from the system prompt's "how" and
/// built from settings rather than written into the prompt, so the cities are stated in one place.
/// This is an order from headquarters, i.e. a task input, not something the run discovers on the
/// console. The classification code the reclassification needs is not stated here on purpose: that
/// is read from the console's own note.
/// </summary>
public static class CentreOrders
{
    public static string Build(TaskSettings settings) => $"""
        Orders from the centre. Make exactly these changes through the okoeditor API, then verify:

        1. The incident about {settings.ProtectedCity} is classified as vehicles and people. Reclassify it as
           animals. Its title carries the code; the note on the console says which code animals is. While you
           are there, its description still calls for the city's destruction — rewrite it to match a sighting
           of animals, so the record no longer argues for sending anyone.

        2. Find the task about {settings.ProtectedCity}, mark it done, and write into its description that
           animals — beavers, say — were seen there.

        3. Draw the operators' attention to the uninhabited city of {settings.DecoyTargetCity} instead, to spare
           {settings.ProtectedCity}. Make the incident list report movement of people near {settings.DecoyTargetCity}.
           The API has no way to add a record, so this means rewriting an existing incident into that report; the
           incident about {settings.DecoyIncidentCity} is the one to repurpose. An incident that reports people
           moving needs both: a title whose code means people, and a description that says so.

        4. When all three are in place, run finish_mission.
        """;
}
