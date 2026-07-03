namespace Ecorex.Application.Common;

/// <summary>
/// Cifra/descifra secretos en reposo (tokens Evolution, llaves, etc.). La implementacion
/// usa proteccion de datos del framework. Los valores cifrados son los unicos que se persisten.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
