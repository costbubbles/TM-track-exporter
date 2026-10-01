using System.CommandLine;
using Tm2Ac.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var root = new RootCommand("Tm2Ac: convert Trackmania Exchange tracks into Assetto Corsa tracks.");
root.Subcommands.Add(SearchCommand.Create());
root.Subcommands.Add(InfoCommand.Create());
root.Subcommands.Add(ConvertCommand.Create());
root.Subcommands.Add(AnalyzeCommand.Create());
root.Subcommands.Add(AssetsCommand.Create());
root.Subcommands.Add(DoctorCommand.Create());
root.Subcommands.Add(DevCommands.Create());

try
{
    return await root.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or NotSupportedException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}
