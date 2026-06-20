namespace LocalAsrClient.App.TextInjection;

internal static class InjectionVerificationPolicy
{
    public static bool IsVerificationRequired(string className)
    {
        return InjectionTextVerifier.CanReadBackText(className);
    }

    public static bool TrustClipboardWithoutVerification(string className)
    {
        return string.Equals(className, "Scintilla", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInjectionVerified(string className, string? readBack, string injected)
    {
        if (!IsVerificationRequired(className))
        {
            return true;
        }

        return InjectionTextVerifier.ContainsInjectedText(readBack, injected);
    }
}
