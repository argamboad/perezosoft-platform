using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Perezosoft.E2E.Tests;

/// <summary>
/// Every full navigation boots the Blazor WebAssembly app from scratch: the loader fetches the runtime
/// and the assemblies, shows "NN%", then hands over to the app. On a busy CI machine that boot can die
/// before the app ever runs — Blazor's own banner, "NN% An unhandled error has occurred.", not a line of
/// ours — and the journey then waits its whole timeout for an element that will never render. This
/// helper does what a person does with that banner: reloads. It also keeps the browser console per page,
/// so a boot that fails for good, and any failed journey, reports what the browser saw.
/// </summary>
public static class BlazorBoot
{
    private const int MaxAttempts = 3;
    private static readonly ConditionalWeakTable<IPage, ConcurrentQueue<string>> Consoles = new();
    private static readonly Lock Gate = new();

    // Either the app rendered its root (the loader is gone) or Blazor showed its error banner.
    private const string BootOutcome = """
        () => {
          const banner = document.getElementById('blazor-error-ui');
          if (banner && getComputedStyle(banner).display !== 'none') {
            const pct = getComputedStyle(document.documentElement).getPropertyValue('--blazor-load-percentage-text');
            return 'error at ' + (pct.trim() || '?');
          }
          return document.querySelector('#app .loading-progress-text') ? null : 'ok';
        }
        """;

    /// <summary>Records console messages, page errors and failed requests for <paramref name="page"/> (idempotent).</summary>
    public static ConcurrentQueue<string> Watch(IPage page)
    {
        lock (Gate)
        {
            if (Consoles.TryGetValue(page, out var existing)) return existing;
            var log = new ConcurrentQueue<string>();
            Consoles.Add(page, log);
            page.Console += (_, m) => Add(log, $"console.{m.Type}: {m.Text}");
            page.PageError += (_, e) => Add(log, $"pageerror: {e}");
            page.RequestFailed += (_, r) => Add(log, $"requestfailed: {r.Method} {r.Url} — {r.Failure}");
            return log;
        }
    }

    public static string ConsoleOf(IPage page) =>
        Consoles.TryGetValue(page, out var log) && !log.IsEmpty ? string.Join("\n", log) : "(browser console empty)";

    /// <summary>Full navigation to <paramref name="url"/>, reloaded while Blazor's boot fails.</summary>
    public static Task GotoAsync(IPage page, string url) => BootAsync(page, () => page.GotoAsync(url), url);

    /// <summary>Full reload of the current page, repeated while Blazor's boot fails.</summary>
    public static Task ReloadAsync(IPage page) => BootAsync(page, () => page.ReloadAsync(), "reload of " + page.Url);

    private static async Task BootAsync(IPage page, Func<Task<IResponse?>> navigate, string what)
    {
        Watch(page);
        for (var attempt = 1; ; attempt++)
        {
            await navigate();
            var outcome = await (await page.WaitForFunctionAsync(BootOutcome, null, new() { Timeout = 60_000 }))
                .JsonValueAsync<string>();
            if (outcome == "ok") return;

            // One line per dead boot, on the live console and in the test's own output (the .trx keeps
            // the latter, and CI's "Slowest journeys" step counts these lines).
            var note = $"[blazor-boot] attempt {attempt}/{MaxAttempts} of {what}: Blazor's loader died ({outcome})";
            TestContext.Progress.WriteLine(note);
            TestContext.Out.WriteLine(note);
            if (attempt == MaxAttempts)
                throw new InvalidOperationException($"{note} — giving up.\nBrowser console:\n{ConsoleOf(page)}");
        }
    }

    private static void Add(ConcurrentQueue<string> log, string line)
    {
        log.Enqueue(line);
        while (log.Count > 200 && log.TryDequeue(out _)) { }
    }
}
