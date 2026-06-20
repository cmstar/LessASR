namespace LocalAsrClient.App.Tests.E2E;

public sealed class UiE2EFactAttribute : FactAttribute
{
    public UiE2EFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LESSASR_RUN_UI_E2E"), "1", StringComparison.Ordinal))
        {
            Skip = "Set LESSASR_RUN_UI_E2E=1 to run desktop UI E2E tests.";
        }
    }
}
