using System.Diagnostics;
using BuildingBlocks.Application.Dispatching;
using BuildingBlocks.Application.Results;

namespace BuildingBlocks.Infrastructure.Dispatching;

public sealed class ActivityRequestTelemetrySessionFactory : IRequestTelemetrySessionFactory, IDisposable
{
    public const string ActivitySourceName = "BuildingBlocks.Dispatcher";

    private readonly ActivitySource _activitySource = new(ActivitySourceName);

    public IRequestTelemetrySession Start(RequestTelemetryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var activity = _activitySource.StartActivity(context.RequestType.Name, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("dispatcher.request.type", context.RequestType.FullName);
            activity.SetTag("dispatcher.request.kind", context.RequestKind);
            activity.SetTag("correlation.id", context.CorrelationId);
            activity.SetTag("request.id", context.RequestId);

            if (!string.IsNullOrWhiteSpace(context.ModuleKey))
            {
                activity.SetTag("dispatcher.module.key", context.ModuleKey);
            }
        }

        return new ActivityRequestTelemetrySession(activity);
    }

    public void Dispose()
    {
        _activitySource.Dispose();
    }

    private sealed class ActivityRequestTelemetrySession : IRequestTelemetrySession
    {
        private readonly Activity? _activity;

        public ActivityRequestTelemetrySession(Activity? activity)
        {
            _activity = activity;
        }

        public void Complete()
        {
            if (_activity is null)
            {
                return;
            }

            _activity.SetTag("dispatcher.outcome", "success");
            _activity.SetStatus(ActivityStatusCode.Ok);
        }

        public void Fail(Error error)
        {
            ArgumentNullException.ThrowIfNull(error);

            if (_activity is null)
            {
                return;
            }

            _activity.SetTag("dispatcher.outcome", "failure");
            _activity.SetTag("error.code", error.Code);
            _activity.SetTag("error.kind", error.Kind.ToString());
            _activity.SetStatus(ActivityStatusCode.Error, error.Code);
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if (_activity is null)
            {
                return;
            }

            _activity.SetTag("dispatcher.outcome", "exception");
            _activity.SetTag("exception.type", exception.GetType().FullName);
            _activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        }

        public void Cancel()
        {
            if (_activity is null)
            {
                return;
            }

            _activity.SetTag("dispatcher.outcome", "cancelled");
            _activity.SetStatus(ActivityStatusCode.Error, "cancelled");
        }

        public void Dispose()
        {
            _activity?.Dispose();
        }
    }
}
