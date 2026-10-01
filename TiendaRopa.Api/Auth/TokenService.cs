using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TiendaRopa.Api.Auth;

/// <summary>
/// Emisión de JWT para clientes y administradores.
///
/// Clave de firma: en Development, si no está configurada, se genera una clave
/// efímera al arrancar (los tokens viven sólo mientras vive el proceso) para no
/// escribir secretos en el repositorio. En producción se configura con la
/// variable de entorno <c>Auth__SigningKey</c>.
/// </summary>
public sealed class TokenService(IConfiguration configuration, IHostEnvironment environment)
{
    public const string RolClaim = "rol";
    public const string IdClienteClaim = "idCliente";

    private readonly byte[] _clave = ObtenerClave(configuration, environment);

    /// <summary>Clave de firma simétrica (efímera en Development si no está configurada).</summary>
    public byte[] Clave => _clave;

    public (string Token, DateTime ExpiresAtUtc) Crear(
        int idUsuario,
        string email,
        string nombre,
        string apellido,
        string rol,
        int? idCliente)
    {
        var expires = DateTime.UtcNow.AddHours(12);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, idUsuario.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("idUsuario", idUsuario.ToString()),
            new("nombre", nombre),
            new("apellido", apellido),
            new(RolClaim, rol),
        };

        if (idCliente is { } cliente)
        {
            claims.Add(new Claim(IdClienteClaim, cliente.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_clave),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static byte[] ObtenerClave(IConfiguration configuration, IHostEnvironment environment)
    {
        var clave = configuration["Auth:SigningKey"];

        if (!string.IsNullOrWhiteSpace(clave))
        {
            return Encoding.UTF8.GetBytes(clave);
        }

        if (environment.IsDevelopment())
        {
            var efemera = new byte[64];
            System.Security.Cryptography.RandomNumberGenerator.Fill(efemera);
            return efemera;
        }

        throw new InvalidOperationException(
            "Falta Auth:SigningKey. Configurá la variable de entorno Auth__SigningKey.");
    }
}
