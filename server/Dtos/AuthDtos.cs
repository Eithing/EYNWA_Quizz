namespace Server.Dtos;

public record RegisterRequest(string Username, string Password);

public record LoginRequest(string Username, string Password);

public record AuthResponse(string Token, string Username);
