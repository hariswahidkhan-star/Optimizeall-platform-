using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.DbBench;
using OptimizeAll.Infrastructure.Persistence;

// Test-only tool. Never point it at a production database: "volume" inserts hundreds of thousands of synthetic rows.
//
//   dotnet run --project backend/tools/OptimizeAll.DbBench -- volume --connection "<mysql>" [--scale 1]
//   dotnet run --project backend/tools/OptimizeAll.DbBench -- bench  --connection "<mysql>" --out after.json [--runs 5]
//   dotnet run --project backend/tools/OptimizeAll.DbBench -- report --before before.json --after after.json --out report.md
//
// The database must already be migrated and seeded with the Baseline + Demo profiles (start the API once against it).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: volume|bench|report [options] (see Program.cs)");
    return 2;
}

var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 1; i < args.Length - 1; i += 2) options[args[i].TrimStart('-')] = args[i + 1];
string Opt(string name) => options.TryGetValue(name, out var v) ? v : throw new ArgumentException($"--{name} is required");

switch (args[0])
{
    case "volume":
    {
        await using var db = Db.Create(Opt("connection"));
        var scale = options.TryGetValue("scale", out var s) ? double.Parse(s, CultureInfo.InvariantCulture) : 1.0;
        await new VolumeGenerator(db, scale).RunAsync();
        return 0;
    }
    case "bench":
    {
        var runs = options.TryGetValue("runs", out var r) ? int.Parse(r, CultureInfo.InvariantCulture) : 5;
        var only = options.TryGetValue("only", out var o) ? o : null;
        BenchCases.Legacy = options.TryGetValue("legacy", out var l) && bool.Parse(l);
        var results = await new BenchRunner(Opt("connection"), runs).RunAsync(BenchCases.All(), only);
        await BenchReport.SaveAsync(results, Opt("out"));
        return 0;
    }
    case "report":
    {
        var before = await BenchReport.LoadAsync(Opt("before"));
        var after = await BenchReport.LoadAsync(Opt("after"));
        await File.WriteAllTextAsync(Opt("out"), BenchReport.Markdown(before, after));
        return 0;
    }
    default:
        Console.Error.WriteLine($"unknown command {args[0]}");
        return 2;
}

internal static class Db
{
    public static AppDbContext Create(string connection, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 36)), o => o.MaxBatchSize(500).CommandTimeout(600));
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new AppDbContext(builder.Options, TimeProvider.System);
    }
}
