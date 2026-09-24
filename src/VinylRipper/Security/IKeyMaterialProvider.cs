namespace VinylRipper.Security;

/// <summary>
/// Origen del secreto del que se deriva la clave AES. Cada plataforma puede aportar el suyo
/// (p. ej. Windows podría envolverlo con DPAPI); el core sólo necesita bytes estables.
/// </summary>
public interface IKeyMaterialProvider
{
    byte[] GetKeyMaterial();
}

/// <summary>
/// Secreto derivado de la identidad de la máquina y del usuario. Evita que el token quede
/// en claro en disco y que el JSON sirva tal cual en otro equipo; no protege frente a un
/// atacante con sesión en esta misma cuenta de usuario.
/// </summary>
public sealed class MachineKeyMaterialProvider : IKeyMaterialProvider
{
    public byte[] GetKeyMaterial()
    {
        var text = $"{Environment.MachineName}\u001f{Environment.UserName}\u001f{Environment.UserDomainName}\u001fVinylRipper";
        return System.Text.Encoding.UTF8.GetBytes(text);
    }
}

/// <summary>Secreto fijo, para tests.</summary>
public sealed class StaticKeyMaterialProvider(byte[] material) : IKeyMaterialProvider
{
    public byte[] GetKeyMaterial() => material;
}
