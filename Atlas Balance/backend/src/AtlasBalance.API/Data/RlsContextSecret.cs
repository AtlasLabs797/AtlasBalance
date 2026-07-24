namespace AtlasBalance.API.Data;

// V-02-06 (RLS-SEC-01): contenedor DI del secreto RLS ya validado por
// Program.cs. Es internal para no exponer el valor a otros ensamblados.
internal sealed class RlsContextSecret
{
    public RlsContextSecret(string secret)
    {
        Secret = secret;
    }

    public string Secret { get; }
}
