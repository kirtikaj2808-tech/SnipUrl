namespace AuthService.Domain.Entities;

public class User
{
    public int Id { get; set; }

    // User's email - used as the login username
    public string Email { get; set; } = string.Empty;

    // BCrypt hash of the password - NEVER store plain text passwords
    // e.g. "$2a$11$abc123..." - the hash contains the salt built-in
    public string PasswordHash { get; set; } = string.Empty;

    // Display name for the user
    public string Username { get; set; } = string.Empty;

    // Role e.g. "User" or "Admin" - used for authorization
    public string Role { get; set; } = "User";

    // When this account was created
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
