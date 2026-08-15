namespace LocalAsrClient.Core.Abstractions;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
