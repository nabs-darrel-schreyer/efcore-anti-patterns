using System.Diagnostics;

namespace Shop.Shared;

public static class ShopActivity
{
    public const string SourceName = "Shop";
    public const string AntiPatterns = "antipatterns";
    public const string BestPractice = "bestpractice";

    public static readonly ActivitySource Source = new(SourceName);

    // The EF instrumentation starts its activity from a diagnostic listener. That
    // activity's Parent is not always the Shop activity we just started, so tags
    // set only on the parent never reach the SQL span. The ambient scope follows
    // the request, and the enricher copies it onto the command span.
    private static readonly AsyncLocal<Scope?> Ambient = new();

    public static Scope Start(string name, string app, string operation, string service)
    {
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        activity?.SetTag("shop.app", app);
        activity?.SetTag("shop.operation", operation);
        activity?.SetTag("shop.service", service);
        var scope = new Scope(activity, app, operation, service, Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    public static Scope? Current => Ambient.Value;

    public sealed class Scope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly Scope? _previous;
        private bool _disposed;

        internal Scope(Activity? activity, string app, string operation, string service, Scope? previous)
        {
            _activity = activity;
            App = app;
            Operation = operation;
            Service = service;
            _previous = previous;
        }

        public string App { get; }
        public string Operation { get; }
        public string Service { get; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _activity?.Dispose();
            if (ReferenceEquals(Ambient.Value, this))
                Ambient.Value = _previous;
        }
    }
}
