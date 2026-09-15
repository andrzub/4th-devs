namespace _03_03_zadanie.Reactor;

public enum ReactorCommand { Start, Reset, Left, Right, Wait }

public static class ReactorCommands
{
    public static readonly IReadOnlyList<string> All = ["start", "reset", "left", "wait", "right"];

    public static string Name(this ReactorCommand command) => command.ToString().ToLowerInvariant();

    public static bool TryParse(string? text, out ReactorCommand command) =>
        Enum.TryParse(text?.Trim(), ignoreCase: true, out command) && Enum.IsDefined(command);

    /// <summary>How the robot's column changes when the command succeeds.</summary>
    public static int ColumnStep(this ReactorCommand command) => command switch
    {
        ReactorCommand.Left => -1,
        ReactorCommand.Right => 1,
        _ => 0
    };

    public static bool MovesRobot(this ReactorCommand command) => command is ReactorCommand.Left or ReactorCommand.Right or ReactorCommand.Wait;
}
