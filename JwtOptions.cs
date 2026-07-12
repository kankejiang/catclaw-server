namespace CatClawMusicServer;

public class JwtOptions
{
    public string SecretKey { get; set; } = "";
    public string Issuer { get; set; } = "CatClawMusicServer";
    public string Audience { get; set; } = "CatClawMusicClient";
    public int AccessTokenExpireMinutes { get; set; } = 15;
    public int RefreshTokenExpireDays { get; set; } = 30;
}
