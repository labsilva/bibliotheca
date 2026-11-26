using Jint;
using Jint.Runtime;

namespace Bibliotheca.Backend.Extractors;

internal static class JintExtractorExecutor
{
    private static readonly SemaphoreSlim EvaluationSlots = new(4);

    internal static async Task<string> EvaluateAsync(string script, string operation, string isbn,
        Func<string, string, string, Task<string>> call, CancellationToken token,
        string? extractorId = null, ILogger? logger = null, bool structuredInput = false, string hostBindings = "")
    {
        await EvaluationSlots.WaitAsync(token);
        try
        {
            // Jint's memory constraint counts allocations on its starting thread.
            // Keep the promise pump there and dispatch asynchronous host work off that thread.
            return await Task.Factory.StartNew(
                () => Evaluate(script, operation, isbn, call, token, extractorId, logger, structuredInput, hostBindings),
                token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        finally { EvaluationSlots.Release(); }
    }

    private static string Evaluate(string script, string operation, string isbn,
        Func<string, string, string, Task<string>> call, CancellationToken token, string? extractorId, ILogger? logger,
        bool structuredInput, string hostBindings)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        logger?.LogDebug("Evaluating extractor {ExtractorId} {Operation} in Jint", extractorId, operation);
        var engine = new Engine(options =>
        {
            options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
            options.MaxStatements(100_000).LimitMemory(32 * 1024 * 1024).LimitRecursion(64).CancellationToken(token);
        });
        engine.SetValue("hostCall", (string method, string input, string argument) =>
            Task.Run(() => call(method, input, argument), token));
        engine.SetValue("operation", operation);
        engine.SetValue("extractorInput", isbn);
        engine.SetValue("structuredInput", structuredInput);
        Jint.Native.JsValue result;
        try
        {
            result = engine.Evaluate("""
                (async (call, operation, input, structured, stringify, parse) => {
                    delete globalThis.hostCall;
                    const host = Object.freeze({
                """ + "\n" + hostBindings + "\n" + """
                    });
                """ + "\n" + script + "\n" + """
                    return stringify(await extract(host, operation, structured ? parse(input) : input));
                })(hostCall, operation, extractorInput, structuredInput, JSON.stringify, JSON.parse)
                """, source: extractorId).UnwrapIfPromise(token);
        }
        catch (ExecutionCanceledException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger?.LogWarning("Jint evaluation for extractor {ExtractorId} {Operation} failed after {ElapsedMs} ms: {FailureType}",
                extractorId, operation, elapsed.ElapsedMilliseconds, exception.GetType().Name);
            throw;
        }
        logger?.LogDebug("Jint evaluation for extractor {ExtractorId} {Operation} completed in {ElapsedMs} ms",
            extractorId, operation, elapsed.ElapsedMilliseconds);
        if (!result.IsString() || result.AsString().Length > 32_768)
            throw new InvalidDataException("Extractor output is missing or exceeds its size limit.");
        return result.AsString();
    }
}
