using IotBackend.Services;

namespace IotBackend.Infrastructure;

public static class PasswordHashCommand
{
    public const string CommandName = "hash-password";

    public static int Run(string? password, TextWriter output, TextWriter error)
    {
        if (string.IsNullOrEmpty(password))
        {
            error.WriteLine("No password provided.");
            return 1;
        }

        output.WriteLine(AuthService.HashPassword(password));
        return 0;
    }

    public static string? ReadPasswordMasked()
    {
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine();
        }

        Console.Error.Write("Password: ");
        var buffer = new System.Text.StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return buffer.ToString();
    }
}
